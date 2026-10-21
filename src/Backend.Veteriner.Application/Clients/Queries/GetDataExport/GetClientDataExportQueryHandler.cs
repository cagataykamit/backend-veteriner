using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Queries.GetBalance;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetDataExport;

/// <summary>
/// Müşteri kartı, rıza, hastalar ve klinik kapsamındaki (aktif klinik bağlamı varsa yalnız o klinik) randevu, muayene özeti,
/// aşı, tedavi, reçete, ücret ve ödemeler. Hastalar kiracı geneli müşteri kaydıdır; diğer bölümler yalnız bu müşterinin
/// hastalarına/müşteri kimliğine bağlı satırlardır. Toplu bakiye <see cref="GetClientBalanceQuery"/> ile aynı kuraldan gelir.
/// </summary>
public sealed class GetClientDataExportQueryHandler
    : IRequestHandler<GetClientDataExportQuery, Result<ClientDataExportDto>>
{
    private const int Max = ClientDataExportConstants.MaxRowsPerSection;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IClinicVeterinarianReader _userNames;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Payment> _payments;

    public GetClientDataExportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IClinicVeterinarianReader userNames,
        ISender sender,
        TimeProvider timeProvider,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Examination> examinations,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Treatment> treatments,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<Charge> charges,
        IReadRepository<Payment> payments)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _userNames = userNames;
        _sender = sender;
        _timeProvider = timeProvider;
        _clients = clients;
        _pets = pets;
        _appointments = appointments;
        _examinations = examinations;
        _vaccinations = vaccinations;
        _treatments = treatments;
        _prescriptions = prescriptions;
        _charges = charges;
        _payments = payments;
    }

    public async Task<Result<ClientDataExportDto>> Handle(GetClientDataExportQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientDataExportDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Result<ClientDataExportDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<ClientDataExportDto>.Failure(scopeResult.Error);

        var clinicId = scopeResult.Value!.SingleClinicId;
        var accessible = scopeResult.Value.AccessibleClinicIds;

        var balance = await _sender.Send(new GetClientBalanceQuery(client.Id), ct);
        if (!balance.IsSuccess)
            return Result<ClientDataExportDto>.Failure(balance.Error);

        var pets = await _pets.ListAsync(new PetsForClientDataExportSpec(tenantId, client.Id), ct);
        var petIds = pets.Select(p => p.Id).ToArray();
        var petNames = pets.ToDictionary(p => p.Id, p => p.Name);
        string PetName(Guid petId) => petNames.GetValueOrDefault(petId, string.Empty);
        string? OptionalPetName(Guid? petId) => petId is { } id ? petNames.GetValueOrDefault(id) : null;

        var appointments = await _appointments.ListAsync(
            new AppointmentsForClientDataExportSpec(tenantId, clinicId, accessible, petIds, Max + 1), ct);
        var examinations = await _examinations.ListAsync(
            new ExaminationsForClientDataExportSpec(tenantId, clinicId, accessible, petIds, Max + 1), ct);
        var vaccinations = await _vaccinations.ListAsync(
            new VaccinationsForClientDataExportSpec(tenantId, clinicId, accessible, petIds, Max + 1), ct);
        var treatments = await _treatments.ListAsync(
            new TreatmentsForClientDataExportSpec(tenantId, clinicId, accessible, petIds, Max + 1), ct);
        var prescriptions = await _prescriptions.ListAsync(
            new PrescriptionsForClientDataExportSpec(tenantId, clinicId, accessible, petIds, Max + 1), ct);
        var charges = await _charges.ListAsync(
            new ChargesForClientDataExportSpec(tenantId, client.Id, clinicId, accessible, Max + 1), ct);
        var payments = await _payments.ListAsync(
            new PaymentsForClientDataExportSpec(tenantId, client.Id, clinicId, accessible, Max + 1), ct);

        string? kvkkRecordedByName = null;
        if (client.KvkkConsentRecordedByUserId is { } recordedBy)
            (await _userNames.GetNamesAsync([recordedBy], ct)).TryGetValue(recordedBy, out kvkkRecordedByName);

        var b = balance.Value!;
        return Result<ClientDataExportDto>.Success(new ClientDataExportDto(
            _timeProvider.GetUtcNow().UtcDateTime,
            clinicId,
            Max,
            new ClientDataExportClientDto(
                client.Id,
                client.FullName,
                client.Phone,
                client.Email,
                client.Address,
                client.CreatedAtUtc,
                client.PreferredContactChannel,
                client.RemindersOptedOut,
                ClientInvoicingSummaryDto.From(client)),
            new ClientDataExportKvkkConsentDto(
                client.KvkkConsentStatus,
                client.KvkkConsentRecordedOn,
                kvkkRecordedByName),
            Section(pets),
            Section(appointments.Select(a => a with { PetName = PetName(a.PetId) })),
            Section(examinations.Select(e => e with { PetName = PetName(e.PetId) })),
            Section(vaccinations.Select(v => v with { PetName = PetName(v.PetId) })),
            Section(treatments.Select(t => t with { PetName = PetName(t.PetId) })),
            Section(prescriptions.Select(p => p with { PetName = PetName(p.PetId) })),
            Section(charges.Select(c => new ClientDataExportChargeDto(
                c.CreatedAtUtc,
                c.Description,
                c.PetId,
                OptionalPetName(c.PetId),
                c.Amount,
                c.AllocatedAmount,
                c.OutstandingAmount,
                c.Currency,
                c.IsVoided,
                c.VoidedAtUtc,
                c.LineKind,
                c.VatRate))),
            Section(payments.Select(p => new ClientDataExportPaymentDto(
                p.PaidAtUtc,
                p.Amount,
                p.AllocatedAmount,
                p.UnallocatedAmount,
                p.Currency,
                p.Method,
                p.PetId,
                OptionalPetName(p.PetId),
                p.IsVoided,
                p.VoidedAtUtc))),
            new ClientDataExportBalanceDto(
                b.Currency,
                b.ChargedAmount,
                b.AllocatedAmount,
                b.OutstandingAmount,
                b.UnallocatedPayments.TotalAmount)));
    }

    /// <summary>Okunan satırlar üst sınır + 1 ile istenir; fazlası varsa kesilir ve işaretlenir.</summary>
    private static ClientDataExportSectionDto<T> Section<T>(IEnumerable<T> rows)
    {
        var list = rows.Take(Max + 1).ToList();
        return list.Count > Max
            ? new ClientDataExportSectionDto<T>(list.Take(Max).ToList(), true)
            : new ClientDataExportSectionDto<T>(list, false);
    }
}
