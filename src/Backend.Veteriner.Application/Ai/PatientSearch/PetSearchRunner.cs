using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Ai.PatientSearch;

/// <summary>
/// Yapılandırılmış hasta araması (AI-006; sözleşme §4): AI çağırmaz. Filtre <see cref="PetSearchFilterRules"/>'tan geçer;
/// klinik ölçütler (muayene, aşı, ilaç) yalnız verilen klinik kapsamındaki kayıtlarda ve kullanıcının okuma izni olan türlerde
/// aranır, hayvan ölçütleri kiracının hayvanlarında (hasta listesiyle aynı). İzin olmayan türe dayanan ölçüt hata döner
/// (sessizce atılsa sonuç genişlerdi). Ölçütler VE ile birleşir; ilaç ölçütü reçete VEYA tedavide aranır.
/// </summary>
public sealed class PetSearchRunner
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Ölçüt başına okunan en çok kayıt (en yeni önce); aşılırsa sonuç <c>Truncated</c>.</summary>
    public const int MaxRowsPerCriterion = 2000;

    public const int MaxMatchesPerType = 3;

    public const string ExaminationType = "Examination";
    public const string VaccinationType = "Vaccination";
    public const string PrescriptionType = "Prescription";
    public const string TreatmentType = "Treatment";

    private readonly IReadRepository<Species> _species;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly TimeProvider _timeProvider;

    public PetSearchRunner(
        IReadRepository<Species> species,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Examination> examinations,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<Treatment> treatments,
        ICurrentUserPermissionChecker permissions,
        TimeProvider timeProvider)
    {
        _species = species;
        _pets = pets;
        _clients = clients;
        _examinations = examinations;
        _vaccinations = vaccinations;
        _prescriptions = prescriptions;
        _treatments = treatments;
        _permissions = permissions;
        _timeProvider = timeProvider;
    }

    /// <summary>İstanbul'un bugünkü günü (göreli tarihler ve yaş için).</summary>
    public DateOnly Today => OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<PetSearchSpeciesRow>> ActiveSpeciesAsync(CancellationToken ct)
        => await _species.ListAsync(new ActiveSpeciesForPetSearchSpec(), ct);

    public async Task<Result<PetSearchResultDto>> SearchAsync(
        Guid tenantId,
        ClinicReadScope scope,
        PetSearchFilter rawFilter,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var today = OperationDayBounds.ToLocalDate(utcNow);

        var species = await ActiveSpeciesAsync(ct);
        var (filter, dropped) = PetSearchFilterRules.Normalize(rawFilter, species.Select(s => s.Code).ToList(), today);
        if (filter.IsEmpty)
            return Result<PetSearchResultDto>.Failure(AiErrors.SearchFilterEmpty);

        var canPrescriptions = _permissions.HasPermission(PermissionCatalog.Prescriptions.Read);
        var canTreatments = _permissions.HasPermission(PermissionCatalog.Treatments.Read);
        var forbidden = new List<string>();
        if (filter.HasExaminationCriterion && !_permissions.HasPermission(PermissionCatalog.Examinations.Read))
            forbidden.Add("muayene kayıtları");
        if (filter.HasVaccineCriterion && !_permissions.HasPermission(PermissionCatalog.Vaccinations.Read))
            forbidden.Add("aşı kayıtları");
        if (filter.HasMedicationCriterion && !canPrescriptions && !canTreatments)
            forbidden.Add("reçete ve tedavi kayıtları");
        if (forbidden.Count > 0)
            return Result<PetSearchResultDto>.Failure(AiErrors.SearchForbidden(string.Join(", ", forbidden)));

        var omittedTypes = new List<string>();
        if (filter.HasMedicationCriterion && !canPrescriptions)
            omittedTypes.Add(PrescriptionType);
        if (filter.HasMedicationCriterion && !canTreatments)
            omittedTypes.Add(TreatmentType);

        var period = new PetSearchPeriod(
            filter.From is { } from ? OperationDayBounds.ForLocalDate(from).DayStartUtc : null,
            filter.To is { } to ? OperationDayBounds.ForLocalDate(to.AddDays(1)).DayStartUtc : null);

        var matches = await MatchClinicalRecordsAsync(tenantId, scope, filter, period, canPrescriptions, canTreatments, utcNow, ct);

        // Gecikmiş aşı tanımı vefat eden hayvanı saymaz (ADR-020 S5).
        var status = filter.Status;
        if (filter.VaccineState == PetSearchVaccineState.Overdue)
        {
            if (status == PetStatus.Deceased)
                return Empty();
            status = PetStatus.Active;
        }

        var criteria = new PetSearchPetCriteria(
            filter.Species?.Select(code => species.First(s => s.Code == code).Id).ToList(),
            filter.Sex,
            filter.Neutered,
            status,
            filter.AgeMinYears is { } min ? today.AddYears(-min) : null,
            filter.AgeMaxYears is { } max ? today.AddYears(-(max + 1)) : null);

        IReadOnlyList<PetSearchPetRow> pagePets;
        int total;
        if (matches is null)
        {
            total = await _pets.CountAsync(new PetsForSearchCountSpec(tenantId, criteria), ct);
            pagePets = total == 0 ? [] : await _pets.ListAsync(new PetsForSearchSpec(tenantId, criteria, null, (page, pageSize)), ct);
        }
        else
        {
            if (matches.Pets.Count == 0)
                return Empty(matches.Truncated);

            // Klinik eşleşmede sıra: en yeni eşleşen kayıt önce.
            var pets = await _pets.ListAsync(new PetsForSearchSpec(tenantId, criteria, matches.Pets.Keys.ToList(), null), ct);
            total = pets.Count;
            pagePets = pets
                .OrderByDescending(p => matches.Pets[p.Id].Max(m => m.OccurredAtUtc))
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        var clientIds = pagePets.Select(p => p.ClientId).Distinct().ToArray();
        var clientNames = clientIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _clients.ListAsync(new ClientsByTenantIdsNameSpec(tenantId, clientIds), ct)).ToDictionary(c => c.Id, c => c.FullName);

        var items = pagePets
            .Select(p => new PetSearchItemDto(
                p.Id,
                p.Name,
                p.SpeciesCode,
                p.SpeciesName,
                p.Breed,
                p.Gender,
                p.IsNeutered,
                p.BirthDate is { } birth ? AgeYears(birth, today) : null,
                p.Status,
                p.ClientId,
                clientNames.GetValueOrDefault(p.ClientId, string.Empty),
                matches?.Pets[p.Id].OrderByDescending(m => m.OccurredAtUtc).ToList() ?? []))
            .ToList();

        return Result<PetSearchResultDto>.Success(new PetSearchResultDto(
            filter, dropped, items, total, page, pageSize, matches?.Truncated ?? false, omittedTypes));

        Result<PetSearchResultDto> Empty(bool truncated = false) => Result<PetSearchResultDto>.Success(
            new PetSearchResultDto(filter, dropped, [], 0, page, pageSize, truncated, omittedTypes));
    }

    /// <summary>Tamamlanmış yaş (doğum günü gelmediyse bir eksik).</summary>
    public static int AgeYears(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
            age--;
        return Math.Max(0, age);
    }

    /// <summary>Klinik ölçütlerin kesişimi: hayvan → eşleşen kayıtlar. Klinik ölçüt yoksa <c>null</c>.</summary>
    private async Task<PetSearchMatches?> MatchClinicalRecordsAsync(
        Guid tenantId,
        ClinicReadScope scope,
        PetSearchFilter filter,
        PetSearchPeriod period,
        bool canPrescriptions,
        bool canTreatments,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (!filter.HasClinicalCriteria)
            return null;

        Dictionary<Guid, List<PetSearchMatchDto>>? result = null;
        var truncated = false;

        void Intersect(Dictionary<Guid, List<PetSearchMatchDto>> next)
        {
            if (result is null)
            {
                result = next;
                return;
            }

            foreach (var petId in result.Keys.ToList())
            {
                if (next.TryGetValue(petId, out var more))
                    result[petId].AddRange(more);
                else
                    result.Remove(petId);
            }
        }

        var overdueWindow = VaccinationDueRule.OverdueWindow(utcNow);

        Dictionary<Guid, List<PetSearchMatchDto>> Group(string type, IReadOnlyList<PetSearchMatchRow> rows)
        {
            truncated |= rows.Count >= MaxRowsPerCriterion;
            return rows
                .GroupBy(r => r.PetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Take(MaxMatchesPerType)
                        .Select(r => new PetSearchMatchDto(
                            type, r.Id, r.OccurredAtUtc, r.Text,
                            r.ScheduledDueAtUtc is { } due && VaccinationDueRule.IsOverdueAt(due, overdueWindow)))
                        .ToList());
        }

        if (filter.HasExaminationCriterion)
        {
            Intersect(Group(ExaminationType, await _examinations.ListAsync(
                new ExaminationsForPetSearchSpec(
                    tenantId, scope.SingleClinicId, scope.AccessibleClinicIds,
                    PetSearchExpressions.LikePatterns(filter.ClinicalTerms), period, MaxRowsPerCriterion),
                ct)));
        }

        if (filter.HasVaccineCriterion && result is not { Count: 0 })
        {
            // Gecikme uygulanmış aşının karşılanmamış sonraki tarihini de kapsar (VaccinationDueRule).
            var fulfilled = await _vaccinations.FulfilledNextDueIdsAsync(
                tenantId, scope.SingleClinicId, scope.AccessibleClinicIds, overdueWindow, ct);
            Intersect(Group(VaccinationType, await _vaccinations.ListAsync(
                new VaccinationsForPetSearchSpec(
                    tenantId, scope.SingleClinicId, scope.AccessibleClinicIds,
                    PetSearchExpressions.LikePatterns(filter.VaccineTerms),
                    filter.VaccineState ?? PetSearchVaccineState.Any, period, utcNow, MaxRowsPerCriterion, fulfilled),
                ct)));
        }

        if (filter.HasMedicationCriterion && result is not { Count: 0 })
        {
            var patterns = PetSearchExpressions.LikePatterns(filter.MedicationTerms);
            var medication = new Dictionary<Guid, List<PetSearchMatchDto>>();
            if (canPrescriptions)
            {
                Merge(medication, Group(PrescriptionType, await _prescriptions.ListAsync(
                    new PrescriptionsForPetSearchSpec(
                        tenantId, scope.SingleClinicId, scope.AccessibleClinicIds, patterns, period, MaxRowsPerCriterion),
                    ct)));
            }

            if (canTreatments)
            {
                Merge(medication, Group(TreatmentType, await _treatments.ListAsync(
                    new TreatmentsForPetSearchSpec(
                        tenantId, scope.SingleClinicId, scope.AccessibleClinicIds, patterns, period, MaxRowsPerCriterion),
                    ct)));
            }

            Intersect(medication);
        }

        return new PetSearchMatches(result ?? [], truncated);
    }

    private static void Merge(Dictionary<Guid, List<PetSearchMatchDto>> target, Dictionary<Guid, List<PetSearchMatchDto>> source)
    {
        foreach (var (petId, list) in source)
        {
            if (target.TryGetValue(petId, out var existing))
                existing.AddRange(list);
            else
                target[petId] = list;
        }
    }

    private sealed record PetSearchMatches(Dictionary<Guid, List<PetSearchMatchDto>> Pets, bool Truncated);
}
