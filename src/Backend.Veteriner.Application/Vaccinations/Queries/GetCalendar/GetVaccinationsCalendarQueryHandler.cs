using System.Globalization;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Vaccinations.Calendar;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetCalendar;

/// <summary>
/// Takvim aşı katmanı: klinikte aralıktaki planlı aşılar, sonraki uygulama tarihleri ve uygulanan aşılar. Gecikmiş aşı yalnız vadesi
/// görüntülenen aralıktaysa gelir (bugüne taşınmaz). Karşılanmış sonraki tarih ve vefat eden hayvanın vadesi gösterilmez. "Durum" filtresi
/// (<c>States</c>) sunucuda uygulanır.
/// Kurallar <see cref="VaccinationCalendarRules"/>.
/// </summary>
public sealed class GetVaccinationsCalendarQueryHandler
    : IRequestHandler<GetVaccinationsCalendarQuery, Result<IReadOnlyList<VaccinationCalendarItemDto>>>
{
    /// <summary>Randevu takvimiyle aynı en geniş aralık.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(45);

    private static readonly StringComparer TurkishIgnoreCase = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly TimeProvider _timeProvider;

    public GetVaccinationsCalendarQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _vaccinations = vaccinations;
        _pets = pets;
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<IReadOnlyList<VaccinationCalendarItemDto>>> Handle(GetVaccinationsCalendarQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (!request.DateFromUtc.HasValue || !request.DateToUtc.HasValue)
        {
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Failure(
                "Vaccinations.Calendar.DateWindowRequired",
                "dateFromUtc ve dateToUtc zorunludur.");
        }

        var fromUtc = NormalizeToUtc(request.DateFromUtc.Value);
        var toUtc = NormalizeToUtc(request.DateToUtc.Value);
        if (toUtc <= fromUtc)
        {
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Failure(
                "Vaccinations.Calendar.InvalidDateWindow",
                "dateToUtc, dateFromUtc'den büyük olmalıdır.");
        }

        if (toUtc - fromUtc > MaxWindow)
        {
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Failure(
                "Vaccinations.Calendar.DateWindowTooLarge",
                "Tarih aralığı en fazla 45 gün olabilir.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Failure(ClinicContextRule.Mismatch("Vaccinations"));

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (requestedClinicId is null)
        {
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Failure(
                "Vaccinations.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi. Aşılar klinik kapsamı olmadan listelenemez.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Failure(scopeResult.Error);

        var effectiveClinicId = scopeResult.Value!.SingleClinicId ?? requestedClinicId.Value;
        var rows = await _vaccinations.ListAsync(new VaccinationsCalendarSpec(tenantId, effectiveClinicId, fromUtc, toUtc), ct);
        if (rows.Count == 0)
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Success(Array.Empty<VaccinationCalendarItemDto>());

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var states = request.States is { Count: > 0 } s ? s.ToHashSet() : null;
        var entries = Expand(rows, fromUtc, toUtc)
            .Where(e => states is null || states.Contains(VaccinationCalendarRules.State(e.Kind, e.AtUtc, utcNow)))
            .ToList();

        var dueEntries = entries.Where(e => e.Kind != VaccinationCalendarKind.Applied).ToList();
        if (dueEntries.Count > 0)
        {
            var duePetIds = dueEntries.Select(e => e.Row.PetId).Distinct().ToArray();
            var notReceivingCare = (await _pets.ListAsync(new PetIdsNotReceivingCareSpec(tenantId, duePetIds), ct)).ToHashSet();
            var fulfilled = await FulfilledNextDueIdsAsync(tenantId, dueEntries, scopeResult.Value.AccessibleClinicIds, ct);
            entries.RemoveAll(e => e.Kind != VaccinationCalendarKind.Applied
                                   && (notReceivingCare.Contains(e.Row.PetId) || (e.Kind == VaccinationCalendarKind.NextDue && fulfilled.Contains(e.Row.Id))));
        }

        if (entries.Count == 0)
            return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Success(Array.Empty<VaccinationCalendarItemDto>());

        var petIds = entries.Select(e => e.Row.PetId).Distinct().ToArray();
        var petById = (await _pets.ListAsync(new PetsByTenantIdsNameClientSpec(tenantId, petIds), ct)).ToDictionary(p => p.Id);
        var clientIds = petById.Values.Select(p => p.ClientId).Distinct().ToArray();
        var clientNameById = clientIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _clients.ListAsync(new ClientsByTenantIdsNameSpec(tenantId, clientIds), ct)).ToDictionary(c => c.Id, c => c.FullName);

        var items = entries
            .Select(e =>
            {
                petById.TryGetValue(e.Row.PetId, out var pet);
                var clientId = pet?.ClientId ?? Guid.Empty;
                return new VaccinationCalendarItemDto(
                    e.Row.Id,
                    e.Kind,
                    VaccinationCalendarRules.State(e.Kind, e.AtUtc, utcNow),
                    VaccinationCalendarRules.LocalDate(e.AtUtc),
                    e.AtUtc,
                    e.Row.ClinicId,
                    e.Row.PetId,
                    pet?.Name ?? string.Empty,
                    clientId,
                    clientNameById.GetValueOrDefault(clientId, string.Empty),
                    e.Row.VaccineDefinitionId,
                    e.Row.VaccineName,
                    e.Row.Notes,
                    VaccinationCalendarRules.TimeSpecified(e.Kind, e.Row.DueTimeSpecified));
            })
            .OrderBy(i => i.AtUtc)
            .ThenBy(i => i.PetName, TurkishIgnoreCase)
            .ThenBy(i => i.VaccineName, TurkishIgnoreCase)
            .ToList();

        return Result<IReadOnlyList<VaccinationCalendarItemDto>>.Success(items);
    }

    /// <summary>Bir kayıt aralıkta iki satır verebilir: uygulandığı gün ve sonraki tarihi.</summary>
    private static IEnumerable<CalendarEntry> Expand(IEnumerable<VaccinationCalendarRow> rows, DateTime fromUtc, DateTime toUtc)
    {
        static bool InRange(DateTime? at, DateTime from, DateTime to) => at is { } v && v >= from && v < to;

        foreach (var r in rows)
        {
            if (r.Status == VaccinationStatus.Scheduled && InRange(r.DueAtUtc, fromUtc, toUtc))
                yield return new CalendarEntry(r, VaccinationCalendarKind.Planned, r.DueAtUtc!.Value);

            if (r.Status != VaccinationStatus.Applied)
                continue;

            if (InRange(r.AppliedAtUtc, fromUtc, toUtc))
                yield return new CalendarEntry(r, VaccinationCalendarKind.Applied, r.AppliedAtUtc!.Value);
            if (InRange(r.DueAtUtc, fromUtc, toUtc) && r.AppliedAtUtc is not null)
                yield return new CalendarEntry(r, VaccinationCalendarKind.NextDue, r.DueAtUtc!.Value);
        }
    }

    private async Task<HashSet<Guid>> FulfilledNextDueIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<CalendarEntry> dueEntries,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        CancellationToken ct)
    {
        var sources = dueEntries
            .Where(e => e.Kind == VaccinationCalendarKind.NextDue)
            .Select(e => new VaccinationDose(e.Row.Id, e.Row.PetId, e.Row.VaccineDefinitionId, e.Row.VaccineName, e.Row.AppliedAtUtc!.Value))
            .ToList();
        if (sources.Count == 0)
            return [];

        var later = await _vaccinations.ListAsync(
            new AppliedVaccinationDosesForPetsSpec(
                tenantId,
                sources.Select(s => s.PetId).Distinct().ToArray(),
                sources.Min(s => s.AppliedAtUtc),
                accessibleClinicIds),
            ct);
        var laterByPet = later.ToLookup(d => d.PetId);

        return sources
            .Where(s => VaccinationCalendarRules.IsNextDueFulfilled(s, laterByPet[s.PetId]))
            .Select(s => s.Id)
            .ToHashSet();
    }

    private static DateTime NormalizeToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private sealed record CalendarEntry(VaccinationCalendarRow Row, VaccinationCalendarKind Kind, DateTime AtUtc);
}
