using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Pets.Queries.GetOpenItems;

/// <summary>
/// Hasta kartı "Yapılacaklar" (ADR-020 S1): aşı, lab, ücret bekleyen kalem, sonraki randevu ve açık borç tek yanıtta.
/// Bölüm durumu hasta özetiyle aynı kuraldır (<see cref="PetSectionLoader"/>). Yeni hesap kuralı yoktur: gecikme
/// <see cref="Pet.ReceivesCare"/>, yorum bekleyen <see cref="LabResult.IsAwaitingInterpretation"/>, tutar
/// <see cref="Charge.CalculateAmount"/> ve <see cref="Charge.OutstandingAmount"/> sayaçlarından gelir.
/// </summary>
public sealed class GetPetOpenItemsQueryHandler : IRequestHandler<GetPetOpenItemsQuery, Result<PetOpenItemsDto>>
{
    /// <summary>Yaklaşan aşı penceresi: vade <c>[şimdi, şimdi + 30 gün)</c>.</summary>
    public const int UpcomingVaccinationDays = 30;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly ICurrentUserPermissionChecker _permissionChecker;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<LabResult> _labResults;
    private readonly IReadRepository<ChargeSuggestion> _chargeSuggestions;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetPetOpenItemsQueryHandler> _logger;

    public GetPetOpenItemsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        ICurrentUserPermissionChecker permissionChecker,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<LabResult> labResults,
        IReadRepository<ChargeSuggestion> chargeSuggestions,
        IReadRepository<Charge> charges,
        IReadRepository<Appointment> appointments,
        TimeProvider timeProvider,
        ILogger<GetPetOpenItemsQueryHandler> logger)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _permissionChecker = permissionChecker;
        _pets = pets;
        _clinics = clinics;
        _vaccinations = vaccinations;
        _labResults = labResults;
        _chargeSuggestions = chargeSuggestions;
        _charges = charges;
        _appointments = appointments;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<PetOpenItemsDto>> Handle(GetPetOpenItemsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PetOpenItemsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<PetOpenItemsDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, request.ClinicId ?? _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PetOpenItemsDto>.Failure(scopeResult.Error);

        var clinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value.AccessibleClinicIds;
        var noClinicAccess = accessibleClinicIds is { Count: 0 };
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var canReadCharges = _permissionChecker.HasPermission(PermissionCatalog.Charges.Read);

        var vaccinations = await PetSectionLoader.LoadAsync(
            _permissionChecker.CanRead(PetRecordKind.Vaccination), noClinicAccess, async () =>
            {
                // Vefat eden hayvanda aşı gecikmesi/hatırlatması yoktur (ADR-020 S5); bölüm boş döner.
                if (!pet.IsReceivingCare)
                    return null;

                var rows = await _vaccinations.ListAsync(
                    new ScheduledVaccinationsDueForPetSpec(
                        tenantId, clinicId, pet.Id, nowUtc.AddDays(UpcomingVaccinationDays), accessibleClinicIds), ct);
                if (rows.Count == 0)
                    return null;

                var items = rows.Select(v => new PetOpenItemsVaccinationDto(v.Id, v.VaccineName, v.DueAtUtc!.Value, v.ClinicId)).ToList();
                return new PetOpenItemsVaccinationsDto(
                    items.Where(v => v.DueAtUtc < nowUtc).ToList(),
                    items.Where(v => v.DueAtUtc >= nowUtc).ToList());
            }, _logger, "Vaccinations", ct);

        var labResults = await PetSectionLoader.LoadAsync(
            _permissionChecker.CanRead(PetRecordKind.LabResult), noClinicAccess, async () =>
            {
                var awaitingResult = await _labResults.ListAsync(
                    new OpenLabResultsForPetSpec(tenantId, clinicId, pet.Id, awaitingInterpretation: false, accessibleClinicIds), ct);
                var awaitingInterpretation = await _labResults.ListAsync(
                    new OpenLabResultsForPetSpec(tenantId, clinicId, pet.Id, awaitingInterpretation: true, accessibleClinicIds), ct);
                return awaitingResult.Count == 0 && awaitingInterpretation.Count == 0
                    ? null
                    : new PetOpenItemsLabResultsDto(
                        awaitingResult.Select(ToLabDto).ToList(),
                        awaitingInterpretation.Select(ToLabDto).ToList());
            }, _logger, "LabResults", ct);

        var pendingCharges = await PetSectionLoader.LoadAsync(canReadCharges, noClinicAccess, async () =>
        {
            var rows = await _chargeSuggestions.ListAsync(
                new PendingChargeSuggestionAmountsForPetSpec(tenantId, clinicId, pet.Id, accessibleClinicIds), ct);
            if (rows.Count == 0)
                return null;

            var priced = rows.Where(r => r.SuggestedUnitPrice.HasValue).ToList();
            return new PetOpenItemsPendingChargesDto(
                rows.Count,
                priced.Sum(r => Charge.CalculateAmount(r.Quantity, r.SuggestedUnitPrice!.Value)),
                rows.Count - priced.Count,
                Charge.DefaultCurrency);
        }, _logger, "PendingCharges", ct);

        var nextAppointment = await PetSectionLoader.LoadAsync(
            _permissionChecker.CanRead(PetRecordKind.Appointment), noClinicAccess, async () =>
            {
                // Vefat edenin açık randevusu otomatik iptal edilmez (K4); kartta görünür, elle iptal edilir.
                var a = await _appointments.FirstOrDefaultAsync(
                    new NextScheduledAppointmentForPetSpec(tenantId, clinicId, pet.Id, nowUtc, accessibleClinicIds), ct);
                return a is null
                    ? null
                    : new PetOpenItemsAppointmentDto(
                        a.Id,
                        a.ScheduledAtUtc,
                        a.DurationMinutes,
                        a.AppointmentType,
                        a.ClinicId,
                        await ClinicNameAsync(tenantId, a.ClinicId, ct));
            }, _logger, "NextAppointment", ct);

        var outstanding = await PetSectionLoader.LoadAsync(canReadCharges, noClinicAccess, async () =>
        {
            // Bakiye ucuyla aynı okuma: sahibin kapsamdaki ücret sayaçları; hayvanın payı aynı satırlardan ayrılır.
            var charges = await _charges.ListAsync(
                new ChargesForClientBalanceSpec(tenantId, pet.ClientId, clinicId, accessibleClinicIds), ct);
            var open = charges.Where(c => c.OutstandingAmount > 0).ToList();
            if (open.Count == 0)
                return null;

            var petOpen = open.Where(c => c.PetId == pet.Id).ToList();
            return new PetOpenItemsOutstandingDto(
                petOpen.Sum(c => c.OutstandingAmount),
                petOpen.Count,
                open.Sum(c => c.OutstandingAmount),
                Charge.DefaultCurrency);
        }, _logger, "Outstanding", ct);

        return Result<PetOpenItemsDto>.Success(new PetOpenItemsDto(
            pet.Id,
            pet.ClientId,
            pet.Status,
            nowUtc,
            vaccinations,
            labResults,
            pendingCharges,
            nextAppointment,
            outstanding));
    }

    private static PetOpenItemsLabResultDto ToLabDto(LabResult l)
        => new(l.Id, l.TestName, l.RequestedAtUtc, l.ResultDateUtc, l.ExaminationId, l.VisitId, l.ClinicId);

    private async Task<string> ClinicNameAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var rows = await _clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, [clinicId]), ct);
        return rows.FirstOrDefault()?.Name ?? string.Empty;
    }
}
