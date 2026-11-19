using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Ai;

/// <summary>
/// Taslak üreten klinik özellikler uçtan uca (AI_DRAFTS_API_CONTRACT.md §7; AI-001/003/004), HTTP + gerçek SQL Server (LocalDB),
/// sahte sağlayıcı (gerçek API çağrısı yok): üretim hiçbir klinik kayda yazmaz, kimlik bilgisi modele gitmez, kabul mevcut
/// komutlarla yazar ve kayıtta "AI taslağından" izi görünür.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AiClinicalDraftIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string OriginalFindings = "Genel durum iyi";
    private const string OriginalNotes = "Önceki not";
    private const string OwnerName = "Taslak Sahibi";

    private static readonly string[] ExamPermissions =
    [
        PermissionCatalog.Ai.Use,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Examinations.Update,
    ];

    private static readonly string[] CarePermissions =
    [
        PermissionCatalog.Ai.Use,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Hospitalizations.Read,
        PermissionCatalog.CareInstructions.Read,
        PermissionCatalog.CareInstructions.Create,
    ];

    private readonly WebApplicationFactory<global::Program> _factory;
    private readonly FakeAiTextGenerator _ai = new();

    public AiClinicalDraftIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerator>();
            services.AddSingleton<IAiTextGenerator>(_ai);
        }));

    [Fact]
    public async Task ExamNote_Draft_Splits_Note_Without_Identity_And_Accept_Merges_Into_Examination()
    {
        var ctx = await SeedAsync(ExamPermissions);
        _ai.Respond("""
            {"visitReason":"[hasta] kusma","anamnesis":"3 gündür kusuyor","findings":"Karın palpasyonunda hassasiyet",
             "temperatureC":38.9,"heartRateBpm":120,"bilinmeyen":"x","respiratoryRatePerMin":"yirmi"}
            """);
        var note = $"{ctx.PetName} 3 gündür kusuyor, sahibi Taslak Bey 0532 111 22 33 aradı. Ateş 38.9, nabız 120.";
        await ctx.AddTermAsync("Otitis externa");

        var draft = await JsonAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/examinations/{ctx.ExaminationId}/ai/note-draft", new { text = note }),
            HttpStatusCode.OK);

        // Modele kimlik gitmedi: hayvan adı [hasta], sahip adı [kişi], telefon [numara].
        var sent = _ai.Requests.Should().ContainSingle().Which.UserContent;
        sent.Should().Contain("[hasta] 3 gündür").And.Contain("[kişi]").And.Contain("[numara]")
            .And.NotContain(ctx.PetName).And.NotContain("Taslak").And.NotContain(OriginalFindings, "AI-001 yalnız notu böler");
        // Dikte düzeltmesi için kliniğin sözlüğü (sistem terimleri) modele gider.
        sent.Should().Contain("\"vocabulary\":[").And.Contain("Otitis externa");
        _ai.Requests.Single().SystemPrompt.Should().Contain("Not sesle yazılmış (dikte) olabilir");

        draft.GetProperty("feature").GetString().Should().Be("ExamNoteStructure");
        draft.GetProperty("targetType").GetString().Should().Be("Examination");
        var fields = draft.GetProperty("content").GetProperty("fields");
        fields.GetProperty("visitReason").GetString().Should().Be($"{ctx.PetName} kusma", "yer tutucu hayvan adıyla değişir");
        fields.GetProperty("temperatureC").GetDecimal().Should().Be(38.9m);
        fields.TryGetProperty("bilinmeyen", out _).Should().BeFalse();
        fields.TryGetProperty("respiratoryRatePerMin", out _).Should().BeFalse("türü yanlış değer atılır");
        draft.GetProperty("content").GetProperty("replaceFields").GetArrayLength().Should().Be(0);

        // Üretim muayeneye dokunmaz; not denetim yüküne yazılmaz.
        (await ctx.ExaminationAsync()).Findings.Should().Be(OriginalFindings);
        (await ctx.AuditPayloadsAsync("Ai.ExamNoteStructure.Generate", $"ExaminationId={ctx.ExaminationId}"))
            .Should().ContainSingle().Which.Should().NotContain("kusuyor");

        // Kabul: şikâyet "yerine yaz", diğerleri sona eklenir. Yalnız seçim değişti → düzenlenmiş sayılmaz.
        var draftId = draft.GetProperty("id").GetGuid();
        var edited = JsonSerializer.SerializeToNode(draft.GetProperty("content"))!.AsObject();
        edited["replaceFields"] = new System.Text.Json.Nodes.JsonArray("visitReason");
        var accepted = await JsonAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { editedContent = edited }),
            HttpStatusCode.OK);
        accepted.GetProperty("status").GetString().Should().Be("Accepted");
        accepted.GetProperty("appliedRecordId").GetGuid().Should().Be(ctx.ExaminationId);

        var exam = await ctx.ExaminationAsync();
        exam.VisitReason.Should().Be($"{ctx.PetName} kusma");
        exam.Anamnesis.Should().Be("3 gündür kusuyor");
        exam.Findings.Should().Be($"{OriginalFindings}\nKarın palpasyonunda hassasiyet");
        exam.Notes.Should().Be(OriginalNotes);
        exam.TemperatureC.Should().Be(38.9m);
        exam.HeartRateBpm.Should().Be(120);

        // Kayıtta iz: muayene detayında kabul edilen taslak.
        var detail = await JsonAsync(await ctx.Http.GetAsync($"/api/v1/examinations/{ctx.ExaminationId}"), HttpStatusCode.OK);
        var trace = detail.GetProperty("aiProvenance").EnumerateArray().Should().ContainSingle().Which;
        trace.GetProperty("draftId").GetGuid().Should().Be(draftId);
        trace.GetProperty("feature").GetString().Should().Be("ExamNoteStructure");
        trace.GetProperty("edited").GetBoolean().Should().BeFalse();
        trace.GetProperty("acceptedByUserId").GetGuid().Should().Be(ctx.UserId);
    }

    [Fact]
    public async Task ExamNote_Edited_Fields_Count_As_Edit_And_Invalid_Content_Is_Rejected_Without_Writing()
    {
        var ctx = await SeedAsync(ExamPermissions);
        _ai.Respond("""{"findings":"Kulakta kızarıklık"}""");
        var draftId = (await JsonAsync(
                await ctx.Http.PostAsJsonAsync($"/api/v1/examinations/{ctx.ExaminationId}/ai/note-draft", new { text = "kulak kırmızı" }),
                HttpStatusCode.OK))
            .GetProperty("id").GetGuid();

        await ProblemAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { editedContent = new { fields = new { kilo = 3 } } }),
            HttpStatusCode.BadRequest,
            "AiDrafts.Validation");
        (await ctx.ExaminationAsync()).Findings.Should().Be(OriginalFindings);

        var accepted = await JsonAsync(
            await ctx.Http.PostAsJsonAsync(
                $"/api/v1/ai/drafts/{draftId}/accept",
                new { editedContent = new { fields = new { findings = "Sağ kulakta kızarıklık" }, replaceFields = Array.Empty<string>() } }),
            HttpStatusCode.OK);
        accepted.GetProperty("status").GetString().Should().Be("AcceptedWithEdits");
        (await ctx.ExaminationAsync()).Findings.Should().Be($"{OriginalFindings}\nSağ kulakta kızarıklık");
    }

    [Fact]
    public async Task ExamNote_On_Finalized_Examination_Fails_Before_Calling_Model()
    {
        var ctx = await SeedAsync(ExamPermissions, finalized: true);

        await ProblemAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/examinations/{ctx.ExaminationId}/ai/note-draft", new { text = "not" }),
            HttpStatusCode.Conflict,
            "Examinations.Finalized");
        _ai.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExamNote_Invalid_Model_Output_Creates_No_Draft()
    {
        var ctx = await SeedAsync(ExamPermissions);
        _ai.Respond("""{"bilinmeyen":"x"}""");

        await ProblemAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/examinations/{ctx.ExaminationId}/ai/note-draft", new { text = "not" }),
            HttpStatusCode.BadGateway,
            "Ai.InvalidOutput");
        (await ctx.DraftCountAsync()).Should().Be(0);
        (await ctx.UsageRecordsAsync()).Should().ContainSingle().Which.Outcome.Should().Be(AiUsageOutcome.InvalidOutput);
    }

    [Fact]
    public async Task OwnerExplanation_Accept_Creates_Separate_Record_With_Trace_And_Leaves_Examination()
    {
        var ctx = await SeedAsync(ExamPermissions, finalized: true);
        _ai.Respond("""{"text":"[hasta] için muayenede kulak iltihabı görüldü."}""");

        var draft = await JsonAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/examinations/{ctx.ExaminationId}/ai/owner-explanation-draft", new { text = "Damlayı vurgula" }),
            HttpStatusCode.OK);

        var sent = _ai.Requests.Should().ContainSingle().Which.UserContent;
        sent.Should().Contain(OriginalFindings, "açıklama muayene kaydından yazılır").And.Contain("Damlayı vurgula")
            .And.NotContain(ctx.PetName);
        draft.GetProperty("feature").GetString().Should().Be("OwnerExplanation");
        draft.GetProperty("content").GetProperty("text").GetString().Should().Be($"{ctx.PetName} için muayenede kulak iltihabı görüldü.");

        var draftId = draft.GetProperty("id").GetGuid();
        var accepted = await JsonAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { editedContent = new { text = "Düzeltilmiş açıklama" } }),
            HttpStatusCode.OK);
        accepted.GetProperty("status").GetString().Should().Be("AcceptedWithEdits");
        var explanationId = accepted.GetProperty("appliedRecordId").GetGuid();

        var list = await JsonAsync(
            await ctx.Http.GetAsync($"/api/v1/examinations/{ctx.ExaminationId}/owner-explanations"), HttpStatusCode.OK);
        var item = list.EnumerateArray().Should().ContainSingle().Which;
        item.GetProperty("id").GetGuid().Should().Be(explanationId);
        item.GetProperty("text").GetString().Should().Be("Düzeltilmiş açıklama");
        item.GetProperty("sourceAiDraftId").GetGuid().Should().Be(draftId);
        item.GetProperty("createdByUserId").GetGuid().Should().Be(ctx.UserId);
        var trace = item.GetProperty("aiProvenance").EnumerateArray().Should().ContainSingle().Which;
        trace.GetProperty("feature").GetString().Should().Be("OwnerExplanation");
        trace.GetProperty("edited").GetBoolean().Should().BeTrue();

        (await ctx.ExaminationAsync()).Notes.Should().Be(OriginalNotes, "açıklama muayene kaydına yazılmaz");
    }

    [Fact]
    public async Task CareInstruction_From_Hospitalization_Creates_Linked_Instruction_With_Trace()
    {
        var ctx = await SeedAsync(CarePermissions, withHospitalization: true);
        _ai.Respond("""{"text":"İlaçlar:\n- Kayıttaki ilaç\nKontrol:\n- [hasta] 3 gün sonra kontrole gelsin."}""");

        var draft = await JsonAsync(
            await ctx.Http.PostAsync($"/api/v1/hospitalizations/{ctx.HospitalizationId}/ai/care-instruction-draft", null),
            HttpStatusCode.OK);
        draft.GetProperty("feature").GetString().Should().Be("DischargeInstructions");
        draft.GetProperty("targetType").GetString().Should().Be("Hospitalization");
        draft.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("type").GetString())
            .Should().Contain(["Examination", "Hospitalization", "Pet"]);
        _ai.Requests.Should().ContainSingle().Which.UserContent.Should().Contain("Gözlem için yatış").And.Contain(OriginalFindings);

        var accepted = await JsonAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draft.GetProperty("id").GetGuid()}/accept", new { }),
            HttpStatusCode.OK);
        accepted.GetProperty("status").GetString().Should().Be("Accepted");

        var instruction = await JsonAsync(
            await ctx.Http.GetAsync($"/api/v1/care-instructions/{accepted.GetProperty("appliedRecordId").GetGuid()}"), HttpStatusCode.OK);
        instruction.GetProperty("hospitalizationId").GetGuid().Should().Be(ctx.HospitalizationId!.Value);
        instruction.GetProperty("examinationId").GetGuid().Should().Be(ctx.ExaminationId, "yatışın muayenesi miras alınır");
        instruction.GetProperty("text").GetString().Should().EndWith($"{ctx.PetName} 3 gün sonra kontrole gelsin.");
        instruction.GetProperty("aiProvenance").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("feature").GetString().Should().Be("DischargeInstructions");
    }

    [Fact]
    public async Task CareInstruction_Draft_Requires_CareInstruction_Create_Permission()
    {
        var ctx = await SeedAsync([PermissionCatalog.Ai.Use, PermissionCatalog.Examinations.Read]);

        var response = await ctx.Http.PostAsync($"/api/v1/examinations/{ctx.ExaminationId}/ai/care-instruction-draft", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _ai.Requests.Should().BeEmpty();
    }

    // ----- yardımcılar -----

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
        => (await JsonAsync(response, expected)).GetProperty("code").GetString().Should().Be(code);

    private async Task<ClinicalDraftTestContext> SeedAsync(string[] permissions, bool finalized = false, bool withHospitalization = false)
    {
        _ai.Requests.Clear();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId, clinicId, userId, examinationId;
        Guid? hospitalizationId = null;
        string email, petName;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"AiK-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            email = $"ai-klinik-{Guid.NewGuid():N}@example.com";
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));

            var client = new Client(tenant.Id, OwnerName, DateTime.UtcNow, "905320000000");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenant.Id, client.Id, $"Tarcin{Guid.NewGuid():N}"[..14], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();

            var exam = new Examination(
                tenant.Id, clinic.Id, pet.Id, null, DateTime.UtcNow.AddHours(-1), "Kontrol", OriginalFindings, null, OriginalNotes, DateTime.UtcNow);
            if (finalized)
                exam.Finalize(user.Id, DateTime.UtcNow).IsSuccess.Should().BeTrue();
            db.Add(exam);

            if (withHospitalization)
            {
                var hospitalization = new Hospitalization(
                    tenant.Id, clinic.Id, pet.Id, exam.Id, DateTime.UtcNow.AddHours(-1), null, "Gözlem için yatış", null, DateTime.UtcNow);
                db.Add(hospitalization);
                hospitalizationId = hospitalization.Id;
            }

            var settings = ClinicAiSettings.Create(tenant.Id, clinic.Id, DateTime.UtcNow);
            settings.SetEnabled(true, null, DateTime.UtcNow);
            db.Add(settings);
            await db.SaveChangesAsync();
            (tenantId, clinicId, userId, examinationId, petName) = (tenant.Id, clinic.Id, user.Id, exam.Id, pet.Name);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions, clinicId);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new ClinicalDraftTestContext(_factory, http, clinicId, userId, examinationId, hospitalizationId, petName);
    }

    private sealed record ClinicalDraftTestContext(
        WebApplicationFactory<global::Program> Factory,
        HttpClient Http,
        Guid ClinicId,
        Guid UserId,
        Guid ExaminationId,
        Guid? HospitalizationId,
        string PetName)
    {
        public async Task<Examination> ExaminationAsync()
        {
            await using var scope = Factory.Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Examinations.AsNoTracking().SingleAsync(e => e.Id == ExaminationId);
        }

        /// <summary>Kiracıya özel sözlük terimi (dikte düzeltmesi için modele giden <c>vocabulary</c>).</summary>
        public async Task AddTermAsync(string name)
        {
            var tenantId = (await ExaminationAsync()).TenantId;
            await using var scope = Factory.Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(Backend.Veteriner.Domain.Catalog.ClinicalTerm.CreateForTenant(
                tenantId, Backend.Veteriner.Domain.Catalog.ClinicalTermKind.Diagnosis, name, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        public async Task<int> DraftCountAsync()
        {
            await using var scope = Factory.Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AiDrafts.AsNoTracking().CountAsync(d => d.ClinicId == ClinicId);
        }

        public async Task<IReadOnlyList<AiUsageRecord>> UsageRecordsAsync()
        {
            await using var scope = Factory.Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AiUsageRecords.AsNoTracking().Where(r => r.ClinicId == ClinicId).ToListAsync();
        }

        public async Task<IReadOnlyList<string>> AuditPayloadsAsync(string action, string target)
        {
            await using var scope = Factory.Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == action && a.TargetId == target)
                .Select(a => a.RequestPayload ?? string.Empty)
                .ToListAsync();
        }
    }

    /// <summary>Sahte sağlayıcı: istekleri kaydeder, ayarlanan yanıtı döner. Sınıf içi testler sırayla çalışır.</summary>
    private sealed class FakeAiTextGenerator : IAiTextGenerator
    {
        private AiGenerationResult _next = new(AiGenerationStatus.Completed, """{"text":"x"}""", 100, 10);

        public bool IsConfigured => true;

        public List<AiGenerationRequest> Requests { get; } = [];

        public void Respond(string text)
            => _next = new AiGenerationResult(AiGenerationStatus.Completed, text, 100, 10);

        public Task<AiGenerationResult> GenerateJsonAsync(AiGenerationRequest request, CancellationToken ct)
        {
            lock (Requests)
                Requests.Add(request);
            return Task.FromResult(_next);
        }
    }
}
