using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
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
/// AI hasta geçmişi özeti (AI_HISTORY_SUMMARY_API_CONTRACT.md): HTTP + gerçek SQL Server (LocalDB), sağlayıcı sahte
/// (gerçek API çağrısı yok). Modele giden girdi (izin süzmesi, kimlik temizliği), kaynak doğrulaması, kullanım kaydı,
/// sınırlar, klinik açma/kapama ve platform yönetimi. Her test kendi kliniğini/kullanıcısını/hayvanını kurar.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AiHistorySummaryIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string OwnerName = "Zeynep Kaplanoğlu";
    private const string OwnerPhone = "905321112233";
    private static readonly DateTime T0 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Ai.Use,
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Treatments.Read,
        PermissionCatalog.Prescriptions.Read,
        PermissionCatalog.LabResults.Read,
        PermissionCatalog.Vaccinations.Read,
        PermissionCatalog.Hospitalizations.Read,
    ];

    private readonly WebApplicationFactory<global::Program> _factory;
    private readonly FakeAiTextGenerator _ai = new();

    public AiHistorySummaryIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerator>();
            services.AddSingleton<IAiTextGenerator>(_ai);
        }));

    [Fact]
    public async Task Summary_Sends_Redacted_Records_And_Returns_Draft_Linked_To_Real_Records()
    {
        var ctx = await SeedAsync(AllPermissions, aiEnabled: true);
        var examId = await ctx.AddExaminationAsync(T0, $"Sahibi {OwnerName} ({OwnerPhone}) aradı; kusma, iştahsızlık.");
        var treatmentId = await ctx.AddTreatmentAsync(T0.AddDays(1));
        await ctx.AddLabResultAsync(T0.AddDays(2), "Hemogram");
        _ai.Respond("""
            {"sections":[
              {"kind":"RecentVisits","items":[{"text":"2026-09-01 kusma ve iştahsızlıkla muayene.","refs":["E1"]}]},
              {"kind":"ActiveIssues","items":[{"text":"Serum tedavisi.","refs":["T1","X9"]},{"text":"Uydurma.","refs":["Z1"]}]}
            ]}
            """, inputTokens: 1_200, outputTokens: 300);

        var response = await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null);
        var body = await JsonAsync(response, HttpStatusCode.OK);

        body.GetProperty("status").GetString().Should().Be("Generated");
        body.GetProperty("isDraft").GetBoolean().Should().BeTrue();
        body.GetProperty("model").GetString().Should().Be("claude-haiku-5-5");
        body.GetProperty("droppedItemCount").GetInt32().Should().Be(1);
        var coverage = body.GetProperty("coverage");
        coverage.GetProperty("recordCount").GetInt32().Should().Be(3);
        coverage.GetProperty("omittedTypes").GetArrayLength().Should().Be(0);
        coverage.GetProperty("truncated").GetBoolean().Should().BeFalse();

        var sections = body.GetProperty("sections").EnumerateArray().ToList();
        sections.Select(s => s.GetProperty("kind").GetString()).Should().Equal("ActiveIssues", "RecentVisits");
        var visitSource = sections[1].GetProperty("items")[0].GetProperty("sources")[0];
        visitSource.GetProperty("type").GetString().Should().Be("Examination");
        visitSource.GetProperty("id").GetGuid().Should().Be(examId);
        var issueSources = sections[0].GetProperty("items")[0].GetProperty("sources");
        issueSources.GetArrayLength().Should().Be(1);
        issueSources[0].GetProperty("id").GetGuid().Should().Be(treatmentId);

        var sent = _ai.Requests.Should().ContainSingle().Which;
        sent.Model.Should().Be("claude-haiku-5-5");
        sent.MaxOutputTokens.Should().Be(2_000);
        sent.UserContent.Should().Contain("kusma, iştahsızlık").And.Contain("Hemogram").And.Contain("\"ref\":\"E1\"");
        sent.UserContent.Should().NotContain("Zeynep").And.NotContain("Kaplanoğlu").And.NotContain(OwnerPhone);
        sent.UserContent.Should().NotContain(ctx.PetName, "hayvan adı da gönderilmez");

        var usage = await ctx.UsageRecordsAsync();
        var record = usage.Should().ContainSingle().Which;
        record.Outcome.Should().Be(AiUsageOutcome.Succeeded);
        record.Feature.Should().Be(AiFeature.PetHistorySummary);
        record.SubjectId.Should().Be(ctx.PetId);
        (record.InputTokens, record.OutputTokens).Should().Be((1_200, 300));
        record.CostUsd.Should().Be(0.00027m);
    }

    [Fact]
    public async Task Summary_Never_Reads_Types_The_User_Cannot_Read()
    {
        var ctx = await SeedAsync(AllPermissions.Except([PermissionCatalog.LabResults.Read]).ToArray(), aiEnabled: true);
        await ctx.AddExaminationAsync(T0, "Kontrol muayenesi.");
        await ctx.AddLabResultAsync(T0.AddDays(1), "GizliTestAdi");
        _ai.Respond("""{"sections":[]}""");

        var body = await JsonAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null), HttpStatusCode.OK);

        body.GetProperty("coverage").GetProperty("omittedTypes").EnumerateArray().Select(e => e.GetString()).Should().Equal("LabResult");
        body.GetProperty("coverage").GetProperty("recordCount").GetInt32().Should().Be(1);
        _ai.Requests.Should().NotContain(r => r.UserContent.Contains("GizliTestAdi"));
    }

    [Fact]
    public async Task Summary_Without_Records_Does_Not_Call_The_Model_Or_Record_Usage()
    {
        var ctx = await SeedAsync(AllPermissions, aiEnabled: true);
        var before = _ai.Requests.Count;

        var body = await JsonAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null), HttpStatusCode.OK);

        body.GetProperty("status").GetString().Should().Be("NoRecords");
        body.GetProperty("sections").GetArrayLength().Should().Be(0);
        _ai.Requests.Count.Should().Be(before);
        (await ctx.UsageRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Feature_Is_Off_Without_Key_Or_When_Clinic_Is_Not_Enabled()
    {
        var disabled = await SeedAsync(AllPermissions, aiEnabled: false);
        await disabled.AddExaminationAsync(T0, "Kontrol.");

        var status = await JsonAsync(await disabled.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK);
        status.GetProperty("isAvailable").GetBoolean().Should().BeFalse();
        status.GetProperty("unavailableCode").GetString().Should().Be("Ai.ClinicNotEnabled");
        await ProblemAsync(await disabled.Http.PostAsync($"/api/v1/pets/{disabled.PetId}/ai/history-summary", null),
            HttpStatusCode.Forbidden, "Ai.ClinicNotEnabled");

        var enabled = await SeedAsync(AllPermissions, aiEnabled: true);
        await enabled.AddExaminationAsync(T0, "Kontrol.");
        (await JsonAsync(await enabled.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK))
            .GetProperty("isAvailable").GetBoolean().Should().BeTrue();

        _ai.IsConfigured = false;
        try
        {
            var noKey = await JsonAsync(await enabled.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK);
            noKey.GetProperty("isConfigured").GetBoolean().Should().BeFalse();
            noKey.GetProperty("unavailableCode").GetString().Should().Be("Ai.NotConfigured");
            await ProblemAsync(await enabled.Http.PostAsync($"/api/v1/pets/{enabled.PetId}/ai/history-summary", null),
                HttpStatusCode.ServiceUnavailable, "Ai.NotConfigured");
        }
        finally
        {
            _ai.IsConfigured = true;
        }

        (await enabled.UsageRecordsAsync()).Should().BeEmpty();
        (await disabled.UsageRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task User_Rate_Limit_And_Clinic_Monthly_Quota_Return_429()
    {
        var ctx = await SeedAsync(AllPermissions, aiEnabled: true);
        await ctx.AddExaminationAsync(T0, "Kontrol.");
        _ai.Respond("""{"sections":[]}""");
        for (var i = 0; i < 3; i++)
            await JsonAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null), HttpStatusCode.OK);

        await ProblemAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null),
            HttpStatusCode.TooManyRequests, "Ai.RateLimited");
        (await ctx.UsageRecordsAsync()).Should().HaveCount(3);

        var full = await SeedAsync(AllPermissions, aiEnabled: true);
        await full.AddExaminationAsync(T0, "Kontrol.");
        await full.AddUsageAsync(costUsd: 2m);

        await ProblemAsync(await full.Http.PostAsync($"/api/v1/pets/{full.PetId}/ai/history-summary", null),
            HttpStatusCode.TooManyRequests, "Ai.ClinicQuotaExceeded");
        var status = await JsonAsync(await full.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK);
        status.GetProperty("unavailableCode").GetString().Should().Be("Ai.ClinicQuotaExceeded");
        status.GetProperty("clinicMonthUsedPercent").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task Provider_Failure_And_Invalid_Output_Are_Recorded_And_Do_Not_Return_A_Summary()
    {
        var ctx = await SeedAsync(AllPermissions, aiEnabled: true);
        await ctx.AddExaminationAsync(T0, "Kontrol.");

        _ai.Fail(AiFailureKind.Unavailable);
        await ProblemAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null),
            HttpStatusCode.ServiceUnavailable, "Ai.ProviderUnavailable");

        _ai.Respond("özet değil", inputTokens: 900, outputTokens: 40);
        await ProblemAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null),
            HttpStatusCode.BadGateway, "Ai.InvalidOutput");

        var outcomes = (await ctx.UsageRecordsAsync()).OrderBy(r => r.CreatedAtUtc).Select(r => r.Outcome);
        outcomes.Should().Equal(AiUsageOutcome.ProviderError, AiUsageOutcome.InvalidOutput);
    }

    [Fact]
    public async Task Endpoints_Require_Ai_Use_And_An_Active_Clinic()
    {
        var noAi = await SeedAsync(AllPermissions.Except([PermissionCatalog.Ai.Use]).ToArray(), aiEnabled: true);
        (await noAi.Http.PostAsync($"/api/v1/pets/{noAi.PetId}/ai/history-summary", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await noAi.Http.GetAsync("/api/v1/ai/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var noClinic = await SeedAsync(AllPermissions, aiEnabled: true, withClinicClaim: false);
        await ProblemAsync(await noClinic.Http.PostAsync($"/api/v1/pets/{noClinic.PetId}/ai/history-summary", null),
            HttpStatusCode.BadRequest, "Clinics.ClinicSelectionRequired");

        var missingPet = await SeedAsync(AllPermissions, aiEnabled: true);
        await ProblemAsync(await missingPet.Http.PostAsync($"/api/v1/pets/{Guid.NewGuid()}/ai/history-summary", null),
            HttpStatusCode.NotFound, "Pets.NotFound");
    }

    [Fact]
    public async Task Platform_Admin_Enables_Clinic_And_Sees_Monthly_Usage()
    {
        var ctx = await SeedAsync(AllPermissions, aiEnabled: false);
        await ctx.AddExaminationAsync(T0, "Kontrol.");
        var admin = await PlatformAdminClientAsync();
        var url = $"/api/v1/admin/ai/clinics/{ctx.ClinicId}";

        (await ctx.Http.PutAsJsonAsync(url, new { isEnabled = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var enabled = await JsonAsync(await admin.PutAsJsonAsync(url, new { isEnabled = true }), HttpStatusCode.OK);
        enabled.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        enabled.GetProperty("tenantId").GetGuid().Should().Be(ctx.TenantId);
        await ProblemAsync(await admin.PutAsJsonAsync($"/api/v1/admin/ai/clinics/{Guid.NewGuid()}", new { isEnabled = true }),
            HttpStatusCode.NotFound, "Ai.ClinicNotFound");

        _ai.Respond("""{"sections":[]}""", inputTokens: 1_000, outputTokens: 100);
        await JsonAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null), HttpStatusCode.OK);

        var usage = await JsonAsync(await admin.GetAsync("/api/v1/admin/ai/usage"), HttpStatusCode.OK);
        usage.GetProperty("isConfigured").GetBoolean().Should().BeTrue();
        usage.GetProperty("limits").GetProperty("systemMonthlyCostLimitUsd").GetDecimal().Should().Be(10m);
        var row = usage.GetProperty("clinics").EnumerateArray().Single(c => c.GetProperty("clinicId").GetGuid() == ctx.ClinicId);
        row.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        row.GetProperty("requests").GetInt32().Should().Be(1);
        row.GetProperty("tokens").GetInt64().Should().Be(1_100);
        (await ctx.Http.GetAsync("/api/v1/admin/ai/usage")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await JsonAsync(await admin.PutAsJsonAsync(url, new { isEnabled = false }), HttpStatusCode.OK);
        await ProblemAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null),
            HttpStatusCode.Forbidden, "Ai.ClinicNotEnabled");
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
        => (await JsonAsync(response, expected)).GetProperty("code").GetString().Should().Be(code);

    private async Task<HttpClient> PlatformAdminClientAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _, _) = jwt.Create(
            Guid.NewGuid(),
            $"ai-destek-{Guid.NewGuid():N}@example.com",
            Array.Empty<string>(),
            [new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString)]);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<AiTestContext> SeedAsync(string[] permissions, bool aiEnabled, bool withClinicClaim = true)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId, clinicId, petId;
        string email, petName;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Ai-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"ai-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));

            var client = new Client(tenant.Id, OwnerName, DateTime.UtcNow, OwnerPhone);
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            petName = $"Pamuk{Guid.NewGuid():N}"[..14];
            var pet = new Pet(tenant.Id, client.Id, petName, speciesId);
            pet.ApplyAlerts(["Allergy"], "Penisilin alerjisi");
            db.Pets.Add(pet);

            if (aiEnabled)
            {
                var settings = ClinicAiSettings.Create(tenant.Id, clinic.Id, DateTime.UtcNow);
                settings.SetEnabled(true, null, DateTime.UtcNow);
                db.Add(settings);
            }

            await db.SaveChangesAsync();
            (tenantId, clinicId, petId) = (tenant.Id, clinic.Id, pet.Id);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, permissions, withClinicClaim ? clinicId : null);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new AiTestContext(_factory.Services, http, tenantId, clinicId, petId, petName);
    }

    private sealed record AiTestContext(IServiceProvider Services, HttpClient Http, Guid TenantId, Guid ClinicId, Guid PetId, string PetName)
    {
        private async Task<Guid> SaveAsync(object entity, Guid id)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(entity);
            await db.SaveChangesAsync();
            return id;
        }

        public Task<Guid> AddExaminationAsync(DateTime at, string findings)
        {
            var exam = new Examination(TenantId, ClinicId, PetId, null, at, "Kusma", findings, null, null, DateTime.UtcNow);
            return SaveAsync(exam, exam.Id);
        }

        public Task<Guid> AddTreatmentAsync(DateTime at)
        {
            var treatment = new Treatment(TenantId, ClinicId, PetId, null, at, "Serum", "IV sıvı tedavisi", null, null, DateTime.UtcNow);
            return SaveAsync(treatment, treatment.Id);
        }

        public Task<Guid> AddLabResultAsync(DateTime at, string testName)
        {
            var lab = new LabResult(TenantId, ClinicId, PetId, null, null, null, at, testName, "Normal", null, null, DateTime.UtcNow);
            return SaveAsync(lab, lab.Id);
        }

        public Task<Guid> AddUsageAsync(decimal costUsd)
        {
            var record = AiUsageRecord.Create(
                TenantId, ClinicId, Guid.NewGuid(), AiFeature.PetHistorySummary, PetId, "claude-haiku-5-5",
                1_000, 100, costUsd, 500, AiUsageOutcome.Succeeded, DateTime.UtcNow);
            return SaveAsync(record, record.Id);
        }

        public async Task<IReadOnlyList<AiUsageRecord>> UsageRecordsAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AiUsageRecords.AsNoTracking().Where(r => r.ClinicId == ClinicId).ToListAsync();
        }
    }

    /// <summary>Sahte sağlayıcı: istekleri kaydeder, ayarlanan yanıtı döner. Sınıf içi testler sırayla çalışır.</summary>
    private sealed class FakeAiTextGenerator : IAiTextGenerator
    {
        private AiGenerationResult _next = new(AiGenerationStatus.Completed, """{"sections":[]}""", 100, 10);

        public bool IsConfigured { get; set; } = true;

        public List<AiGenerationRequest> Requests { get; } = [];

        public void Respond(string text, int inputTokens = 100, int outputTokens = 10)
            => _next = new AiGenerationResult(AiGenerationStatus.Completed, text, inputTokens, outputTokens);

        public void Fail(AiFailureKind kind) => _next = AiGenerationResult.Failed(kind);

        public Task<AiGenerationResult> GenerateJsonAsync(AiGenerationRequest request, CancellationToken ct)
        {
            lock (Requests)
                Requests.Add(request);
            return Task.FromResult(_next);
        }
    }
}
