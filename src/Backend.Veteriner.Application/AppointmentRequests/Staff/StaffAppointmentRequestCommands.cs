using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Appointments.Reasons;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.AppointmentRequests.Staff;

/// <summary>
/// Talebi onaylar (APPT-011; ADR-042 K4): randevu mevcut <see cref="CreateAppointmentCommand"/> ile oluşur (çalışma saati, slot,
/// klinik/hayvan/hekim çakışması kuralları tek yerde) ve talebe bağlanır; hepsi tek transaction'da. Farklı saat/hekim verilirse
/// yeniden planlanmış sayılır ve sahibe "saat değiştirildi" e-postası gider. Hekim verilmezse o saatte boş uygun hekim atanır.
/// Müşteri/hayvan: portal talebinde bağın müşterisi; bağlantı talebinde personel mevcut müşteriyi seçer ya da önce hızlı kayıtla
/// oluşturur (APPT-017/018 ayrı dalda).
/// </summary>
public sealed record ApproveAppointmentRequestCommand(
    Guid Id,
    Guid? ClientId,
    Guid? PetId,
    DateTime? ScheduledAtUtc,
    Guid? VeterinarianUserId,
    int? DurationMinutes,
    AppointmentRequestEmailOverride? Email)
    : IRequest<Result<AppointmentRequestDetailDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "AppointmentRequest.Approve";
    public string? AuditTarget => $"AppointmentRequestId={Id}";
}

/// <summary>Talebi reddeder; gerekçe zorunlu ve sahibe gider.</summary>
public sealed record RejectAppointmentRequestCommand(Guid Id, string Reason, AppointmentRequestEmailOverride? Email)
    : IRequest<Result<AppointmentRequestDetailDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentRequest.Reject";
    public string? AuditTarget => $"AppointmentRequestId={Id}";
}

/// <summary>Onay/red ekranı için doldurulmuş e-posta (gönderim öncesi düzenlenebilir).</summary>
public sealed record PreviewAppointmentRequestEmailQuery(Guid Id, AppointmentRequestEmailKind Kind, DateTime? ScheduledAtUtc, string? RejectionReason)
    : IRequest<Result<AppointmentRequestEmailPreviewDto>>;

public sealed class ApproveAppointmentRequestCommandValidator : AbstractValidator<ApproveAppointmentRequestCommand>
{
    public ApproveAppointmentRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ClientId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x.PetId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x.VeterinarianUserId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x.DurationMinutes).Must(d => d is null || Appointment.IsValidDurationMinutes(d.Value))
            .WithMessage("Randevu süresi 5-240 dakika arasında olmalıdır.");
        RuleFor(x => x.Email!).SetValidator(new EmailOverrideValidator()).When(x => x.Email is not null);
    }
}

public sealed class RejectAppointmentRequestCommandValidator : AbstractValidator<RejectAppointmentRequestCommand>
{
    public RejectAppointmentRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty()
            .MinimumLength(AppointmentRequest.RejectionReasonMinLength)
            .MaximumLength(AppointmentRequest.RejectionReasonMaxLength);
        RuleFor(x => x.Email!).SetValidator(new EmailOverrideValidator()).When(x => x.Email is not null);
    }
}

public sealed class EmailOverrideValidator : AbstractValidator<AppointmentRequestEmailOverride>
{
    public EmailOverrideValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(ClinicAppointmentRequestEmailTemplate.SubjectMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ClinicAppointmentRequestEmailTemplate.BodyMaxLength);
    }
}

public sealed class ApproveAppointmentRequestCommandHandler
    : IRequestHandler<ApproveAppointmentRequestCommand, Result<AppointmentRequestDetailDto>>
{
    private readonly StaffAppointmentRequestAccess _access;
    private readonly IRepository<AppointmentRequest> _requests;
    private readonly IReadRepository<AppointmentReason> _reasons;
    private readonly IReadRepository<Pet> _pets;
    private readonly AppointmentAvailabilityService _availability;
    private readonly AppointmentRequestEmailComposer _emails;
    private readonly IClientContext _clientContext;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public ApproveAppointmentRequestCommandHandler(
        StaffAppointmentRequestAccess access,
        IRepository<AppointmentRequest> requests,
        IReadRepository<AppointmentReason> reasons,
        IReadRepository<Pet> pets,
        AppointmentAvailabilityService availability,
        AppointmentRequestEmailComposer emails,
        IClientContext clientContext,
        ISender sender,
        TimeProvider timeProvider)
    {
        _access = access;
        _requests = requests;
        _reasons = reasons;
        _pets = pets;
        _availability = availability;
        _emails = emails;
        _clientContext = clientContext;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentRequestDetailDto>> Handle(ApproveAppointmentRequestCommand request, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var found = await _access.FindAsync(request.Id, tracking: true, ct);
        if (!found.IsSuccess)
            return Result<AppointmentRequestDetailDto>.Failure(found.Error);
        var r = found.Value!;

        var pending = r.EnsurePending();
        if (!pending.IsSuccess)
            return Result<AppointmentRequestDetailDto>.Failure(pending.Error);

        var clientId = request.ClientId ?? r.ClientId;
        var petId = request.PetId ?? r.PetId;
        if (clientId is null || petId is null)
            return Fail("AppointmentRequests.Validation", "Onay için müşteri ve hayvan seçilmelidir.");
        if (r.ClientId is { } linked && clientId != linked)
            return Fail("AppointmentRequests.Validation", "Portal talebi bağlı müşteri dışında bir müşteriye onaylanamaz.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(r.TenantId, petId.Value), ct);
        if (pet is null || pet.ClientId != clientId)
            return Fail("Pets.NotFound", "Hayvan bulunamadı veya seçilen müşteriye ait değil.");

        var startUtc = AppointmentRequestIntake.NormalizeUtc(request.ScheduledAtUtc ?? r.RequestedStartUtc);
        var duration = request.DurationMinutes ?? r.DurationMinutes;
        var reason = await _reasons.FirstOrDefaultAsync(new AppointmentReasonByIdSpec(r.TenantId, r.ClinicId, r.ReasonId), ct);

        var vetId = request.VeterinarianUserId;
        if (vetId is null)
        {
            var (hasVets, free) = await _availability.FindFreeVeterinariansAsync(r.TenantId, r.ClinicId, reason, startUtc, duration, ct);
            if (hasVets && free.Count == 0)
                return Fail("AppointmentRequests.SlotUnavailable", "Bu saatte uygun hekim yok; farklı saat ya da hekim seçin.");
            vetId = free.FirstOrDefault() is { } first && first != Guid.Empty ? first : null;
        }

        var created = await _sender.Send(
            new CreateAppointmentCommand(
                r.ClinicId,
                pet.Id,
                startUtc,
                AppointmentType.Other,
                null,
                string.IsNullOrWhiteSpace(r.OwnerNote) ? $"Online talep: {r.ReasonName}" : $"Online talep: {r.ReasonName}\nSahip notu: {r.OwnerNote}",
                duration,
                reason is { IsActive: true } ? reason.Id : null,
                vetId),
            ct);
        if (!created.IsSuccess)
            return Result<AppointmentRequestDetailDto>.Failure(created.Error);

        var approved = r.Approve(created.Value, clientId.Value, pet.Id, startUtc, vetId, userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!approved.IsSuccess)
            return Result<AppointmentRequestDetailDto>.Failure(approved.Error);

        var kind = r.WasRescheduled ? AppointmentRequestEmailKind.Rescheduled : AppointmentRequestEmailKind.Approved;
        await _emails.SendAsync(r, kind, ToMessage(request.Email), ct);

        try
        {
            await _requests.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı karar kazandı; başarısız sonuç bu randevuyu da geri alır.
            return Fail("AppointmentRequests.ConcurrencyConflict", "Talep aynı anda değişti; listeyi yenileyip tekrar deneyin.");
        }

        return await _sender.Send(new GetAppointmentRequestQuery(r.Id), ct);
    }

    internal static OwnerPortalEmails.Message? ToMessage(AppointmentRequestEmailOverride? edited)
        => edited is null ? null : new OwnerPortalEmails.Message(edited.Subject.Trim(), edited.Body.Trim());

    private static Result<AppointmentRequestDetailDto> Fail(string code, string message)
        => Result<AppointmentRequestDetailDto>.Failure(code, message);
}

public sealed class RejectAppointmentRequestCommandHandler
    : IRequestHandler<RejectAppointmentRequestCommand, Result<AppointmentRequestDetailDto>>
{
    private readonly StaffAppointmentRequestAccess _access;
    private readonly IRepository<AppointmentRequest> _requests;
    private readonly AppointmentRequestEmailComposer _emails;
    private readonly IClientContext _clientContext;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public RejectAppointmentRequestCommandHandler(
        StaffAppointmentRequestAccess access,
        IRepository<AppointmentRequest> requests,
        AppointmentRequestEmailComposer emails,
        IClientContext clientContext,
        ISender sender,
        TimeProvider timeProvider)
    {
        _access = access;
        _requests = requests;
        _emails = emails;
        _clientContext = clientContext;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentRequestDetailDto>> Handle(RejectAppointmentRequestCommand request, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
            return Result<AppointmentRequestDetailDto>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var found = await _access.FindAsync(request.Id, tracking: true, ct);
        if (!found.IsSuccess)
            return Result<AppointmentRequestDetailDto>.Failure(found.Error);
        var r = found.Value!;

        var rejected = r.Reject(request.Reason, userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!rejected.IsSuccess)
            return Result<AppointmentRequestDetailDto>.Failure(rejected.Error);

        await _emails.SendAsync(r, AppointmentRequestEmailKind.Rejected, ApproveAppointmentRequestCommandHandler.ToMessage(request.Email), ct);

        try
        {
            await _requests.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<AppointmentRequestDetailDto>.Failure(
                "AppointmentRequests.ConcurrencyConflict",
                "Talep aynı anda değişti; listeyi yenileyip tekrar deneyin.");
        }

        return await _sender.Send(new GetAppointmentRequestQuery(r.Id), ct);
    }
}

public sealed class PreviewAppointmentRequestEmailQueryHandler
    : IRequestHandler<PreviewAppointmentRequestEmailQuery, Result<AppointmentRequestEmailPreviewDto>>
{
    private readonly StaffAppointmentRequestAccess _access;
    private readonly AppointmentRequestEmailComposer _emails;

    public PreviewAppointmentRequestEmailQueryHandler(StaffAppointmentRequestAccess access, AppointmentRequestEmailComposer emails)
    {
        _access = access;
        _emails = emails;
    }

    public async ValueTask<Result<AppointmentRequestEmailPreviewDto>> Handle(PreviewAppointmentRequestEmailQuery request, CancellationToken ct)
    {
        var found = await _access.FindAsync(request.Id, tracking: false, ct);
        if (!found.IsSuccess)
            return Result<AppointmentRequestEmailPreviewDto>.Failure(found.Error);

        var message = await _emails.ComposeAsync(
            found.Value!,
            request.Kind,
            request.ScheduledAtUtc is { } s ? AppointmentRequestIntake.NormalizeUtc(s) : null,
            request.RejectionReason,
            ct);
        return Result<AppointmentRequestEmailPreviewDto>.Success(new AppointmentRequestEmailPreviewDto(request.Kind, message.Subject, message.Body));
    }
}
