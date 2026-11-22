using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Ai.PetHistorySummary;

/// <summary>
/// Hasta geçmişi özetinin model girdisi (AI-002; ADR-006 kaynakları). Yalnız kullanıcının okuma izni olan türler
/// (<see cref="PetRecordPermissions"/>) ve klinik kapsamındaki kayıtlar okunur; iptal edilenler alınmaz. Müşteri kaydı
/// gönderilmez, serbest metin <see cref="AiTextRedactor"/>'dan geçer. Her kayda kısa bir ref verilir (ör. <c>E1</c>); model
/// yalnız bu ref'lerle kaynak gösterir. Tahmini girdi <see cref="AiUsageGrant.MaxInputTokens"/>'ı aşarsa en eski kayıtlar çıkarılır.
/// </summary>
public sealed class PetHistoryAiContextBuilder
{
    /// <summary>Tür başına okunan en yeni kayıt sayısı.</summary>
    public const int TakePerType = 10;

    public const int ScheduledVaccinationsTake = 5;

    /// <summary>Serbest metin alanı başına en çok karakter.</summary>
    public const int MaxFieldLength = 800;

    /// <summary>Token tahmini: karakter / bu değer (kaba, yukarı yuvarlanır; gerçek token sağlayıcıdan kaydedilir).</summary>
    private const int CharsPerToken = 3;

    private static readonly PetRecordKind[] Kinds =
    [
        PetRecordKind.Examination,
        PetRecordKind.Treatment,
        PetRecordKind.Prescription,
        PetRecordKind.LabResult,
        PetRecordKind.Vaccination,
        PetRecordKind.Hospitalization,
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<LabResult> _labResults;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly TimeProvider _timeProvider;

    public PetHistoryAiContextBuilder(
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Examination> examinations,
        IReadRepository<Treatment> treatments,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<LabResult> labResults,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Hospitalization> hospitalizations,
        ICurrentUserPermissionChecker permissions,
        TimeProvider timeProvider)
    {
        _pets = pets;
        _clients = clients;
        _examinations = examinations;
        _treatments = treatments;
        _prescriptions = prescriptions;
        _labResults = labResults;
        _vaccinations = vaccinations;
        _hospitalizations = hospitalizations;
        _permissions = permissions;
        _timeProvider = timeProvider;
    }

    /// <returns>Hayvan kiracıda yoksa <c>null</c>.</returns>
    public async Task<PetHistoryAiContext?> BuildAsync(
        Guid tenantId, ClinicReadScope scope, Guid petId, int maxInputTokens, CancellationToken ct)
    {
        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct);
        if (pet is null)
            return null;

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
        var names = AiTextRedactor.NameTerms(client?.FullName);
        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);

        var (allowed, omitted) = _permissions.Split(Kinds);
        var entries = new List<Entry>();
        if (scope.AccessibleClinicIds is not { Count: 0 })
            entries.AddRange(await ReadRecordsAsync(tenantId, scope, pet, allowed, today, names, ct));

        var profile = PetProfile(pet, today, names);
        var (included, truncated) = FitToBudget(profile, entries, today, maxInputTokens);

        var sources = new Dictionary<string, PetHistoryAiSourceDto>(StringComparer.OrdinalIgnoreCase)
        {
            [ProfileRef] = new("Pet", pet.Id, null),
        };
        foreach (var e in included)
            sources[e.Ref] = new PetHistoryAiSourceDto(e.Kind.ToString(), e.Id, e.AtUtc);

        var coverage = new PetHistoryAiCoverageDto(
            included.Count == 0 ? null : included.Min(e => e.AtUtc),
            included.Count == 0 ? null : included.Max(e => e.AtUtc),
            included.Count,
            included.GroupBy(e => e.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            omitted.Select(k => k.ToString()).ToList(),
            truncated);

        return new PetHistoryAiContext(Serialize(profile, included, today), sources, coverage);
    }

    private const string ProfileRef = "P1";

    private async Task<List<Entry>> ReadRecordsAsync(
        Guid tenantId, ClinicReadScope scope, Pet pet, IReadOnlyList<PetRecordKind> allowed, DateOnly today,
        IReadOnlyList<string> names, CancellationToken ct)
    {
        var clinicId = scope.SingleClinicId;
        var clinicIds = scope.AccessibleClinicIds;
        var petId = pet.Id;
        var entries = new List<Entry>();
        string? Text(string? value) => Clip(AiTextRedactor.Redact(value, names));

        if (allowed.Contains(PetRecordKind.Examination))
        {
            var rows = await _examinations.ListAsync(new ExaminationsForPetRecentSpec(tenantId, clinicId, petId, TakePerType, clinicIds), ct);
            entries.AddRange(rows.Select((e, i) => new Entry($"E{i + 1}", PetRecordKind.Examination, e.Id, e.ExaminedAtUtc, Compact(new JsonObject
            {
                ["date"] = Date(e.ExaminedAtUtc),
                ["status"] = e.Status.ToString(),
                ["visitReason"] = Text(e.VisitReason),
                ["anamnesis"] = Text(e.Anamnesis),
                ["findings"] = Text(e.Findings),
                ["assessment"] = Text(e.Assessment),
                ["plan"] = Text(e.Plan),
                ["notes"] = Text(e.Notes),
                ["weightKg"] = e.WeightKg,
                ["temperatureC"] = e.TemperatureC,
                ["heartRateBpm"] = e.HeartRateBpm,
                ["respiratoryRatePerMin"] = e.RespiratoryRatePerMin,
            }))));
        }

        if (allowed.Contains(PetRecordKind.Treatment))
        {
            var rows = await _treatments.ListAsync(new TreatmentsForPetRecentSpec(tenantId, clinicId, petId, TakePerType, clinicIds), ct);
            entries.AddRange(rows.Select((t, i) => new Entry($"T{i + 1}", PetRecordKind.Treatment, t.Id, t.TreatmentDateUtc, Compact(new JsonObject
            {
                ["date"] = Date(t.TreatmentDateUtc),
                ["title"] = Text(t.Title),
                ["description"] = Text(t.Description),
                ["notes"] = Text(t.Notes),
                ["followUpDate"] = Date(t.FollowUpDateUtc),
            }))));
        }

        if (allowed.Contains(PetRecordKind.Prescription))
        {
            var rows = await _prescriptions.ListAsync(new PrescriptionsForPetAiSpec(tenantId, clinicId, petId, TakePerType, clinicIds), ct);
            entries.AddRange(rows.Select((p, i) => new Entry($"R{i + 1}", PetRecordKind.Prescription, p.Id, p.PrescribedAtUtc, Compact(new JsonObject
            {
                ["date"] = Date(p.PrescribedAtUtc),
                ["title"] = Text(p.Title),
                ["content"] = Text(p.Content),
                ["notes"] = Text(p.Notes),
                ["followUpDate"] = Date(p.FollowUpDateUtc),
                ["items"] = new JsonArray(p.Items.OrderBy(x => x.SortOrder).Select(x => (JsonNode)Compact(new JsonObject
                {
                    ["name"] = Text(x.Name),
                    ["dose"] = Text(x.Dose),
                    ["unit"] = Text(x.Unit),
                    ["quantity"] = x.Quantity,
                    ["instructions"] = Text(x.Instructions),
                })).ToArray()),
            }))));
        }

        if (allowed.Contains(PetRecordKind.LabResult))
        {
            var rows = await _labResults.ListAsync(new LabResultsForPetRecentSpec(tenantId, clinicId, petId, TakePerType, clinicIds), ct);
            entries.AddRange(rows.Select((l, i) => new Entry($"L{i + 1}", PetRecordKind.LabResult, l.Id, l.ResultDateUtc ?? l.RequestedAtUtc, Compact(new JsonObject
            {
                ["requestedDate"] = Date(l.RequestedAtUtc),
                ["resultDate"] = Date(l.ResultDateUtc),
                ["status"] = l.Status == LabResultStatus.Requested ? "AwaitingResult" : "Resulted",
                ["testName"] = Text(l.TestName),
                ["result"] = Text(l.ResultText),
                ["interpretation"] = Text(l.Interpretation),
                ["awaitingInterpretation"] = l.AwaitingInterpretation ? true : null,
                ["notes"] = Text(l.Notes),
            }))));
        }

        if (allowed.Contains(PetRecordKind.Vaccination))
        {
            var applied = await _vaccinations.ListAsync(new AppliedVaccinationsForPetAiSpec(tenantId, clinicId, petId, TakePerType, clinicIds), ct);
            var scheduled = await _vaccinations.ListAsync(new ScheduledVaccinationsForPetAiSpec(tenantId, clinicId, petId, ScheduledVaccinationsTake, clinicIds), ct);
            var overdueWindow = VaccinationDueRule.OverdueWindow(today);
            // Uygulanmış aşının karşılanmamış sonraki tarihi de gecikmiş olabilir (VaccinationDueRule).
            var fulfilled = (await _vaccinations.FulfilledNextDueIdsAsync(
                tenantId, clinicId, clinicIds, overdueWindow.FromUtc, overdueWindow.BeforeUtc, ct, petIds: [petId]));
            var index = 0;
            foreach (var v in applied.Concat(scheduled))
            {
                var at = v.AppliedAtUtc ?? v.DueAtUtc ?? v.CreatedAtUtc;
                var isScheduled = v.Status == VaccinationStatus.Scheduled;
                var isOpenDue = VaccinationDueRule.IsOpenDue(v.Id, v.Status, v.AppliedAtUtc, v.DueAtUtc, fulfilled);
                entries.Add(new Entry($"A{++index}", PetRecordKind.Vaccination, v.Id, at, Compact(new JsonObject
                {
                    ["vaccine"] = Text(v.VaccineName),
                    ["status"] = isScheduled ? "Scheduled" : "Applied",
                    ["appliedDate"] = Date(v.AppliedAtUtc),
                    ["dueDate"] = Date(v.DueAtUtc),
                    ["overdue"] = isOpenDue && pet.Status == PetStatus.Active && v.DueAtUtc is { } due && VaccinationDueRule.IsOverdueAt(due, overdueWindow) ? true : null,
                    ["notes"] = Text(v.Notes),
                })));
            }
        }

        if (allowed.Contains(PetRecordKind.Hospitalization))
        {
            var rows = await _hospitalizations.ListAsync(new HospitalizationsForPetRecentSpec(tenantId, clinicId, petId, TakePerType, clinicIds), ct);
            entries.AddRange(rows.Select((h, i) => new Entry($"H{i + 1}", PetRecordKind.Hospitalization, h.Id, h.AdmittedAtUtc, Compact(new JsonObject
            {
                ["admittedDate"] = Date(h.AdmittedAtUtc),
                ["dischargedDate"] = Date(h.DischargedAtUtc),
                ["active"] = h.IsActive ? true : null,
                ["reason"] = Text(h.Reason),
                ["notes"] = Text(h.Notes),
                ["dischargeNote"] = Text(h.DischargeNote),
            }))));
        }

        return entries;
    }

    /// <summary>Hayvan profili (<c>Pets.Read</c>): ad, çip, pasaport gibi tanımlayıcılar gönderilmez.</summary>
    private static JsonObject PetProfile(Pet pet, DateOnly today, IReadOnlyList<string> names) => Compact(new JsonObject
    {
        ["ref"] = ProfileRef,
        ["species"] = pet.Species?.Name,
        ["breed"] = pet.Breed,
        ["sex"] = pet.Gender switch { PetGender.Male => "Erkek", PetGender.Female => "Dişi", _ => null },
        ["neutered"] = pet.IsNeutered,
        ["age"] = Age(pet.BirthDate, pet.DeceasedOn ?? today),
        ["status"] = pet.Status.ToString(),
        ["deceasedOn"] = pet.DeceasedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["profileWeightKg"] = pet.Weight,
        ["alerts"] = pet.AlertFlags == PetAlertFlags.None
            ? null
            : new JsonArray(PetAlertFlagNames.ToNames(pet.AlertFlags).Select(n => (JsonNode)n).ToArray()),
        ["alertNote"] = Clip(AiTextRedactor.Redact(pet.AlertNote, names)),
        ["notes"] = Clip(AiTextRedactor.Redact(pet.Notes, names)),
    });

    /// <summary>En yeni kayıtlar kalır; tahmini girdi bütçeyi aşarsa en eski kayıt çıkarılır (profil hep kalır).</summary>
    private static (IReadOnlyList<Entry> Included, bool Truncated) FitToBudget(
        JsonObject profile, List<Entry> entries, DateOnly today, int maxInputTokens)
    {
        var ordered = entries.OrderByDescending(e => e.AtUtc).ThenBy(e => e.Kind).ToList();
        var budgetChars = (long)Math.Max(0, maxInputTokens) * CharsPerToken - PetHistoryAiPrompt.System.Length;
        var size = Serialize(profile, [], today).Length + ordered.Sum(e => (long)e.Render().ToJsonString(JsonOptions).Length + 1);

        var truncated = false;
        while (ordered.Count > 0 && size > budgetChars)
        {
            var oldest = ordered[^1];
            size -= oldest.Render().ToJsonString(JsonOptions).Length + 1;
            ordered.RemoveAt(ordered.Count - 1);
            truncated = true;
        }

        return (ordered, truncated);
    }

    private static string Serialize(JsonObject profile, IReadOnlyList<Entry> entries, DateOnly today)
        => new JsonObject
        {
            ["today"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["patient"] = profile.DeepClone(),
            ["records"] = new JsonArray(entries.Select(e => (JsonNode)e.Render()).ToArray()),
        }.ToJsonString(JsonOptions);

    internal static string? Age(DateOnly? birthDate, DateOnly asOf)
    {
        if (birthDate is not { } birth || birth > asOf)
            return null;

        var months = (asOf.Year - birth.Year) * 12 + asOf.Month - birth.Month - (asOf.Day < birth.Day ? 1 : 0);
        return months >= 12 ? $"{months / 12} yıl {months % 12} ay" : $"{months} ay";
    }

    internal static string? Date(DateTime? utc)
        => utc is { } value ? OperationDayBounds.ToLocalDate(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static string? Clip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        return trimmed.Length <= MaxFieldLength ? trimmed : trimmed[..MaxFieldLength] + "…";
    }

    /// <summary>Boş alanlar gönderilmez (token tasarrufu).</summary>
    internal static JsonObject Compact(JsonObject obj)
    {
        foreach (var key in obj.Where(p => p.Value is null).Select(p => p.Key).ToList())
            obj.Remove(key);
        return obj;
    }

    private sealed record Entry(string Ref, PetRecordKind Kind, Guid Id, DateTime AtUtc, JsonObject Payload)
    {
        /// <summary>Modele giden biçim: önce <c>ref</c> ve <c>type</c>, sonra alanlar.</summary>
        public JsonObject Render()
        {
            var obj = new JsonObject { ["ref"] = Ref, ["type"] = Kind.ToString() };
            foreach (var (key, value) in Payload)
                obj[key] = value?.DeepClone();
            return obj;
        }
    }
}

/// <param name="UserContent">Modele giden JSON (hayvan profili + kayıtlar, en yeni önce).</param>
/// <param name="Sources">Ref → kaynak kayıt (yalnız gönderilen kayıtlar ve hayvan profili).</param>
public sealed record PetHistoryAiContext(
    string UserContent,
    IReadOnlyDictionary<string, PetHistoryAiSourceDto> Sources,
    PetHistoryAiCoverageDto Coverage)
{
    public bool HasClinicalRecords => Coverage.RecordCount > 0;
}
