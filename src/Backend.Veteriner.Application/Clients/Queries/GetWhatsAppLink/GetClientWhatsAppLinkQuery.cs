using System.Globalization;
using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Clients.Queries.GetWhatsAppLink;

/// <summary>Personelin WhatsApp ile iletebileceği hazır mesaj türü (PORTAL-010; ADR-031 K7).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WhatsAppLinkKind
{
    PortalInvite = 0,
    AppointmentReminder = 1,
    VaccinationReminder = 2,
}

public sealed record ClientWhatsAppLinkDto(string PhoneE164, string Url, string Text);

/// <summary>
/// Müşterinin WhatsApp sohbetini hazır metinle açan <c>wa.me</c> bağlantısı (AK-6: önce bağlantı, API sonra). Sistem göndermez,
/// teslim durumu tutulmaz; mesajı personel kendi WhatsApp'ından yollar. Metin asgari veri taşır (hayvan adı, tarih; teşhis yok),
/// davet metninde ham bağlantı yer almaz (bağlantı yalnız e-postadadır).
/// </summary>
public sealed record GetClientWhatsAppLinkQuery(Guid ClientId, WhatsAppLinkKind? Kind, Guid? TargetId)
    : IRequest<Result<ClientWhatsAppLinkDto>>, IAuditableRequest
{
    public string AuditAction => "Clients.WhatsAppLink";
    public string? AuditTarget => $"ClientId={ClientId};Kind={Kind};TargetId={TargetId}";
}

public sealed class GetClientWhatsAppLinkQueryValidator : AbstractValidator<GetClientWhatsAppLinkQuery>
{
    public GetClientWhatsAppLinkQueryValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Kind).NotNull().IsInEnum();
        RuleFor(x => x.TargetId).NotEmpty().When(x => x.Kind is WhatsAppLinkKind.AppointmentReminder or WhatsAppLinkKind.VaccinationReminder);
    }
}

public sealed class GetClientWhatsAppLinkQueryHandler : IRequestHandler<GetClientWhatsAppLinkQuery, Result<ClientWhatsAppLinkDto>>
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly TenantPlanFeatureEvaluator _planFeatures;

    public GetClientWhatsAppLinkQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Client> clients,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Vaccination> vaccinations,
        TenantPlanFeatureEvaluator planFeatures)
    {
        _planFeatures = planFeatures;
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clients = clients;
        _clinics = clinics;
        _pets = pets;
        _appointments = appointments;
        _vaccinations = vaccinations;
    }

    public async ValueTask<Result<ClientWhatsAppLinkDto>> Handle(GetClientWhatsAppLinkQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Fail("Clients.NotFound", "Müşteri bulunamadı.");

        if (!TurkishMobilePhone.TryNormalize(client.Phone, out var digits) || digits is null)
            return Fail("Clients.WhatsAppPhoneInvalid", "Müşteri kaydında geçerli bir Türkiye cep telefonu yok.");

        var text = request.Kind switch
        {
            WhatsAppLinkKind.PortalInvite => await InviteTextAsync(tenantId, client, ct),
            WhatsAppLinkKind.AppointmentReminder => await AppointmentTextAsync(tenantId, client, request.TargetId!.Value, ct),
            _ => await VaccinationTextAsync(tenantId, client, request.TargetId!.Value, ct),
        };
        if (!text.IsSuccess)
            return Result<ClientWhatsAppLinkDto>.Failure(text.Error);

        return Result<ClientWhatsAppLinkDto>.Success(
            new ClientWhatsAppLinkDto($"+{digits}", $"https://wa.me/{digits}?text={Uri.EscapeDataString(text.Value!)}", text.Value!));
    }

    private async Task<Result<string>> InviteTextAsync(Guid tenantId, Client client, CancellationToken ct)
    {
        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.OwnerPortal, ct);
        if (!planAllows.IsSuccess)
            return Result<string>.Failure(planAllows.Error);

        var eligibility = OwnerPortalInvitePolicy.CanInvite(client);
        if (!eligibility.IsSuccess)
            return Result<string>.Failure(eligibility.Error);

        if (_clinicContext.ClinicId is not { } clinicId)
            return Result<string>.Failure("OwnerPortal.ClinicRequired", "Daveti gönderen klinik belirtilmelidir (aktif klinik).");

        var clinicName = await ClinicNameAsync(tenantId, clinicId, ct);
        return Result<string>.Success(
            $"Merhaba, {clinicName} sizi hasta sahibi portalına davet etti. Davet bağlantısı e-posta adresinize gönderildi.");
    }

    private async Task<Result<string>> AppointmentTextAsync(Guid tenantId, Client client, Guid appointmentId, CancellationToken ct)
    {
        // Hatırlatma kuralı tek yerde: Client.AcceptsReminders (RemindersOptedOut).
        if (client.RemindersOptedOut)
            return Result<string>.Failure("Clients.RemindersOptedOut", "Müşteri hatırlatma istemiyor.");

        var appointment = await _appointments.FirstOrDefaultAsync(new AppointmentByIdSpec(tenantId, appointmentId), ct);
        var pet = appointment is null ? null : await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, appointment.PetId), ct);
        if (appointment is null || pet is null || pet.ClientId != client.Id || appointment.Status != AppointmentStatus.Scheduled)
            return Result<string>.Failure("Clients.WhatsAppTargetNotFound", "Planlı randevu bulunamadı.");

        var clinicName = await ClinicNameAsync(tenantId, appointment.ClinicId, ct);
        var when = OperationDayBounds.ToLocal(appointment.ScheduledAtUtc).ToString("dd.MM.yyyy HH:mm", Turkish);
        return Result<string>.Success($"Merhaba, {pet.Name} için {when} randevunuzu hatırlatırız. {clinicName}");
    }

    private async Task<Result<string>> VaccinationTextAsync(Guid tenantId, Client client, Guid vaccinationId, CancellationToken ct)
    {
        // Hatırlatma kuralı tek yerde: Client.AcceptsReminders (RemindersOptedOut).
        if (client.RemindersOptedOut)
            return Result<string>.Failure("Clients.RemindersOptedOut", "Müşteri hatırlatma istemiyor.");

        var vaccination = await _vaccinations.FirstOrDefaultAsync(new VaccinationByIdSpec(tenantId, vaccinationId), ct);
        var pet = vaccination is null ? null : await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, vaccination.PetId), ct);
        if (vaccination is null || pet is null || pet.ClientId != client.Id || vaccination.Status != VaccinationStatus.Scheduled)
            return Result<string>.Failure("Clients.WhatsAppTargetNotFound", "Planlı aşı bulunamadı.");

        var clinicName = await ClinicNameAsync(tenantId, vaccination.ClinicId, ct);
        var due = vaccination.DueAtUtc is { } d ? $" ({OperationDayBounds.ToLocal(d).ToString("dd.MM.yyyy", Turkish)})" : string.Empty;
        return Result<string>.Success($"Merhaba, {pet.Name} için {vaccination.VaccineName} aşısının zamanı yaklaşıyor{due}. {clinicName}");
    }

    private async Task<string> ClinicNameAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
        => (await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct))?.Name ?? "Kliniğiniz";

    private static Result<ClientWhatsAppLinkDto> Fail(string code, string message) => Result<ClientWhatsAppLinkDto>.Failure(code, message);
}
