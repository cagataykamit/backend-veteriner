using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.Ai.PetHistorySummary;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;

namespace Backend.Veteriner.Application.Ai.ClinicalDrafts;

/// <summary>
/// Muayene ve yatıştan taslak üreten özelliklerin (AI-001/003/004) model girdisi ve hedef kaydı. Kayıt aktif klinikte olmalıdır.
/// Kimlik bilgisi gönderilmez: hayvan adı <see cref="AiTextRedactor.PatientMask"/>, sahip adı <see cref="AiTextRedactor.NameMask"/>
/// olur; müşteri kaydının yalnız adı okunur (metinden gizlemek için). Reçete ve tedavi kayıtları yalnız kullanıcının okuma izni
/// varsa ve muayeneye bağlıysa girer; iptaller alınmaz.
/// </summary>
public sealed class ClinicalAiContextBuilder
{
    /// <summary>Muayeneye bağlı tür başına en çok kayıt.</summary>
    public const int RelatedTake = 10;

    /// <summary>Kayıtlardaki serbest metin alanı başına en çok karakter (kullanıcı notu hariç; onun sınırı komuttadır).</summary>
    public const int MaxFieldLength = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly TimeProvider _timeProvider;

    public ClinicalAiContextBuilder(
        IReadRepository<Examination> examinations,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<Treatment> treatments,
        ICurrentUserPermissionChecker permissions,
        TimeProvider timeProvider)
    {
        _examinations = examinations;
        _hospitalizations = hospitalizations;
        _pets = pets;
        _clients = clients;
        _prescriptions = prescriptions;
        _treatments = treatments;
        _permissions = permissions;
        _timeProvider = timeProvider;
    }

    /// <summary>Aktif klinikteki muayene; başka klinikteki muayene "bulunamadı" döner.</summary>
    public async Task<Result<Examination>> LoadExaminationAsync(Guid tenantId, Guid clinicId, Guid examinationId, CancellationToken ct)
    {
        var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examinationId), ct);
        return exam is not null && exam.ClinicId == clinicId
            ? Result<Examination>.Success(exam)
            : Result<Examination>.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");
    }

    /// <summary>Aktif klinikteki, iptal edilmemiş yatış.</summary>
    public async Task<Result<Hospitalization>> LoadHospitalizationAsync(Guid tenantId, Guid clinicId, Guid hospitalizationId, CancellationToken ct)
    {
        var hospitalization = await _hospitalizations.FirstOrDefaultAsync(new HospitalizationByIdSpec(tenantId, hospitalizationId), ct);
        if (hospitalization is null || hospitalization.ClinicId != clinicId)
            return Result<Hospitalization>.Failure("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

        return hospitalization.CancelledAtUtc is null
            ? Result<Hospitalization>.Success(hospitalization)
            : Result<Hospitalization>.Failure("Hospitalizations.AlreadyCancelled", "İptal edilmiş yatış için taslak hazırlanamaz.");
    }

    /// <summary>
    /// Modele giden JSON: <c>today</c>, <c>patient</c> (tür, ırk, cinsiyet, yaş, uyarılar), istenen kayıtlar ve kullanıcının notu.
    /// </summary>
    /// <param name="examination">Girdiye alınacak muayene (alanları ve bağlı reçete/tedavileri); <c>null</c> ise alınmaz.</param>
    /// <param name="hospitalization">Girdiye alınacak yatış; <c>null</c> ise alınmaz.</param>
    /// <param name="includeExaminationFields">
    /// <c>false</c>: muayene yalnız hedef/kaynak olarak kullanılır, alanları ve bağlı kayıtları gönderilmez (AI-001 yalnız notu böler).
    /// </param>
    /// <param name="userNote">Kullanıcının yazdığı serbest not (temizlenerek <c>note</c> alanına konur).</param>
    /// <param name="vocabulary">
    /// Kliniğin sözlük terimleri (<c>vocabulary</c>); sesle yazılmış nottaki terim hatalarını düzeltmek için (AI-001). Kişi verisi içermez.
    /// </param>
    public async Task<ClinicalAiContext> BuildAsync(
        Guid tenantId,
        Guid petId,
        Examination? examination,
        Hospitalization? hospitalization,
        bool includeExaminationFields,
        string? userNote,
        CancellationToken ct,
        IReadOnlyList<string>? vocabulary = null)
    {
        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct);
        var client = pet is null ? null : await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
        var names = AiTextRedactor.NameTerms(client?.FullName);
        var patientNames = AiTextRedactor.PatientNameTerms(pet?.Name);
        string? Text(string? value, int max = MaxFieldLength) => Clip(AiTextRedactor.Redact(value, names, patientNames), max);

        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        var sources = new List<AiSourceRef>();
        var input = new JsonObject
        {
            ["today"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["patient"] = pet is null ? null : PatientProfile(pet, today),
        };

        if (examination is not null)
        {
            sources.Add(new AiSourceRef(nameof(Examination), examination.Id));
            if (includeExaminationFields)
            {
                input["examination"] = PetHistoryAiContextBuilder.Compact(new JsonObject
                {
                    ["date"] = PetHistoryAiContextBuilder.Date(examination.ExaminedAtUtc),
                    ["visitReason"] = Text(examination.VisitReason),
                    ["anamnesis"] = Text(examination.Anamnesis),
                    ["findings"] = Text(examination.Findings),
                    ["assessment"] = Text(examination.Assessment),
                    ["plan"] = Text(examination.Plan),
                    ["notes"] = Text(examination.Notes),
                    ["weightKg"] = examination.WeightKg,
                    ["temperatureC"] = examination.TemperatureC,
                    ["heartRateBpm"] = examination.HeartRateBpm,
                    ["respiratoryRatePerMin"] = examination.RespiratoryRatePerMin,
                });
                await AddRelatedAsync(tenantId, examination, input, sources, Text, ct);
            }
        }

        if (hospitalization is not null)
        {
            sources.Add(new AiSourceRef(nameof(Hospitalization), hospitalization.Id));
            input["hospitalization"] = PetHistoryAiContextBuilder.Compact(new JsonObject
            {
                ["admittedDate"] = PetHistoryAiContextBuilder.Date(hospitalization.AdmittedAtUtc),
                ["dischargedDate"] = PetHistoryAiContextBuilder.Date(hospitalization.DischargedAtUtc),
                ["plannedDischargeDate"] = PetHistoryAiContextBuilder.Date(hospitalization.PlannedDischargeAtUtc),
                ["reason"] = Text(hospitalization.Reason),
                ["notes"] = Text(hospitalization.Notes),
                ["dischargeNote"] = Text(hospitalization.DischargeNote),
            });
        }

        if (pet is not null)
            sources.Add(new AiSourceRef(nameof(Pet), pet.Id));

        if (Clip(AiTextRedactor.Redact(userNote, names, patientNames), int.MaxValue) is { } note)
            input["note"] = note;

        if (vocabulary is { Count: > 0 })
            input["vocabulary"] = new JsonArray(vocabulary.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

        return new ClinicalAiContext(PetHistoryAiContextBuilder.Compact(input).ToJsonString(JsonOptions), sources, pet?.Name);
    }

    private async Task AddRelatedAsync(
        Guid tenantId, Examination examination, JsonObject input, List<AiSourceRef> sources, Func<string?, int, string?> text, CancellationToken ct)
    {
        if (_permissions.CanRead(PetRecordKind.Prescription))
        {
            var rows = await _prescriptions.ListAsync(new PrescriptionsForExaminationAiSpec(tenantId, examination.ClinicId, examination.Id, RelatedTake), ct);
            sources.AddRange(rows.Select(p => new AiSourceRef(nameof(Prescription), p.Id)));
            input["prescriptions"] = rows.Count == 0 ? null : new JsonArray(rows.Select(p => (JsonNode)PetHistoryAiContextBuilder.Compact(new JsonObject
            {
                ["date"] = PetHistoryAiContextBuilder.Date(p.PrescribedAtUtc),
                ["title"] = text(p.Title, MaxFieldLength),
                ["content"] = text(p.Content, MaxFieldLength),
                ["notes"] = text(p.Notes, MaxFieldLength),
                ["followUpDate"] = PetHistoryAiContextBuilder.Date(p.FollowUpDateUtc),
                ["items"] = new JsonArray(p.Items.OrderBy(x => x.SortOrder).Select(x => (JsonNode)PetHistoryAiContextBuilder.Compact(new JsonObject
                {
                    ["name"] = text(x.Name, MaxFieldLength),
                    ["dose"] = text(x.Dose, MaxFieldLength),
                    ["unit"] = text(x.Unit, MaxFieldLength),
                    ["quantity"] = x.Quantity,
                    ["instructions"] = text(x.Instructions, MaxFieldLength),
                })).ToArray()),
            })).ToArray());
        }

        if (_permissions.CanRead(PetRecordKind.Treatment))
        {
            var rows = await _treatments.ListAsync(new TreatmentsForExaminationAiSpec(tenantId, examination.ClinicId, examination.Id, RelatedTake), ct);
            sources.AddRange(rows.Select(t => new AiSourceRef(nameof(Treatment), t.Id)));
            input["treatments"] = rows.Count == 0 ? null : new JsonArray(rows.Select(t => (JsonNode)PetHistoryAiContextBuilder.Compact(new JsonObject
            {
                ["date"] = PetHistoryAiContextBuilder.Date(t.TreatmentDateUtc),
                ["title"] = text(t.Title, MaxFieldLength),
                ["description"] = text(t.Description, MaxFieldLength),
                ["notes"] = text(t.Notes, MaxFieldLength),
                ["followUpDate"] = PetHistoryAiContextBuilder.Date(t.FollowUpDateUtc),
            })).ToArray());
        }
    }

    /// <summary>Hasta profili: ad, çip, pasaport, not gibi tanımlayıcı ya da serbest alanlar gönderilmez.</summary>
    private static JsonObject PatientProfile(Pet pet, DateOnly today) => PetHistoryAiContextBuilder.Compact(new JsonObject
    {
        ["species"] = pet.Species?.Name,
        ["breed"] = pet.Breed,
        ["sex"] = pet.Gender switch { PetGender.Male => "Erkek", PetGender.Female => "Dişi", _ => null },
        ["neutered"] = pet.IsNeutered,
        ["age"] = PetHistoryAiContextBuilder.Age(pet.BirthDate, pet.DeceasedOn ?? today),
        ["profileWeightKg"] = pet.Weight,
        ["alerts"] = pet.AlertFlags == PetAlertFlags.None
            ? null
            : new JsonArray(PetAlertFlagNames.ToNames(pet.AlertFlags).Select(n => (JsonNode)n).ToArray()),
    });

    private static string? Clip(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}

/// <param name="UserContent">Modele giden JSON.</param>
/// <param name="Sources">Girdiye giren kayıtlar (denetim ve taslağın kaynakları).</param>
/// <param name="PetName">Yanıttaki <see cref="AiTextRedactor.PatientMask"/>'ı geri koymak için (sağlayıcıya gitmez).</param>
public sealed record ClinicalAiContext(string UserContent, IReadOnlyList<AiSourceRef> Sources, string? PetName);
