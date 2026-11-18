using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Commands.Update;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Ai;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Mediator;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Ai;

/// <summary>
/// AI taslak onay akışı (AI_DRAFTS_API_CONTRACT.md; AI-009/AI-010): taslak <see cref="AiInvoker"/> ile sahte sağlayıcıdan üretilir
/// (gerçek API çağrısı yok); kabul, mevcut <see cref="UpdateExaminationCommand"/> ile muayene notuna yazan test uygulayıcısıyla
/// uçtan uca, HTTP + gerçek SQL Server (LocalDB). Kabul edilmeyen taslak hiçbir kayda yazılmaz; denetimde klinik metin yok.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AiDraftIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string OriginalNote = "Exam notu ilk hali";
    private const string GeneratedNote = "AI taslak notu: iki gün sonra kontrol";
    private const string EditedNote = "Hekim düzeltmesi: üç gün sonra kontrol";

    private static readonly string[] DraftPermissions =
    [
        PermissionCatalog.Ai.Use,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Examinations.Update,
    ];

    private readonly WebApplicationFactory<global::Program> _factory;
    private readonly FakeAiTextGenerator _ai = new();

    public AiDraftIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerator>();
            services.AddSingleton<IAiTextGenerator>(_ai);
            services.AddScoped<IAiDraftApplier, TestExaminationNoteApplier>();
        }));

    [Fact]
    public async Task Draft_Is_Not_Written_Until_Accepted_Then_Applied_Through_Existing_Command()
    {
        var ctx = await SeedAsync(DraftPermissions);
        var draftId = await GenerateDraftAsync(ctx, GeneratedNote);

        // Taslak üretildi: muayeneye dokunulmadı; denetim kaydı taslağa bağlı, yalnız kimlik ve sürüm tutuyor.
        (await ctx.ExaminationNotesAsync()).Should().Be(OriginalNote);
        var usage = (await ctx.UsageRecordsAsync()).Should().ContainSingle().Which;
        usage.DraftId.Should().Be(draftId);
        usage.Feature.Should().Be(AiFeature.OwnerExplanation);
        usage.PromptVersion.Should().Be("test-note/1");
        usage.Outcome.Should().Be(AiUsageOutcome.Succeeded);
        usage.SourceRefs.Should().Equal(new AiSourceRef("Examination", ctx.ExaminationId), new AiSourceRef("Pet", ctx.PetId));

        var draft = await JsonAsync(await ctx.Http.GetAsync($"/api/v1/ai/drafts/{draftId}"), HttpStatusCode.OK);
        draft.GetProperty("status").GetString().Should().Be("Draft");
        draft.GetProperty("feature").GetString().Should().Be("OwnerExplanation");
        draft.GetProperty("targetType").GetString().Should().Be("Examination");
        draft.GetProperty("targetId").GetGuid().Should().Be(ctx.ExaminationId);
        draft.GetProperty("content").GetProperty("note").GetString().Should().Be(GeneratedNote);
        draft.GetProperty("sources").GetArrayLength().Should().Be(2);

        var pending = await JsonAsync(
            await ctx.Http.GetAsync($"/api/v1/ai/drafts?targetType=Examination&targetId={ctx.ExaminationId}"), HttpStatusCode.OK);
        pending.EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).Should().Equal(draftId);

        var accepted = await JsonAsync(await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { }), HttpStatusCode.OK);
        accepted.GetProperty("status").GetString().Should().Be("Accepted");
        accepted.GetProperty("appliedRecordId").GetGuid().Should().Be(ctx.ExaminationId);
        accepted.GetProperty("decidedByUserId").GetGuid().Should().Be(ctx.UserId);
        accepted.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Null);

        (await ctx.ExaminationNotesAsync()).Should().Be(GeneratedNote);
        var stored = await ctx.DraftAsync(draftId);
        stored.Status.Should().Be(AiDraftStatus.Accepted);
        stored.Content.Should().BeNull("kabulde içerik hedef kayıttadır, taslakta tutulmaz");

        // Karar verilmiş taslağa ikinci karar verilemez.
        await ProblemAsync(await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { }), HttpStatusCode.Conflict, "AiDrafts.NotPending");
        await ProblemAsync(await ctx.Http.PostAsync($"/api/v1/ai/drafts/{draftId}/discard", null), HttpStatusCode.Conflict, "AiDrafts.NotPending");
        (await JsonAsync(
                await ctx.Http.GetAsync($"/api/v1/ai/drafts?targetType=Examination&targetId={ctx.ExaminationId}"), HttpStatusCode.OK))
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Accept_With_Edits_Writes_Edited_Content_And_Keeps_Clinical_Text_Out_Of_Audit()
    {
        var ctx = await SeedAsync(DraftPermissions);
        var draftId = await GenerateDraftAsync(ctx, GeneratedNote);

        var accepted = await JsonAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { editedContent = new { note = EditedNote } }),
            HttpStatusCode.OK);

        accepted.GetProperty("status").GetString().Should().Be("AcceptedWithEdits");
        (await ctx.ExaminationNotesAsync()).Should().Be(EditedNote);

        var audit = await ctx.AuditPayloadsAsync("Ai.Draft.Accept", draftId);
        audit.Should().ContainSingle();
        audit[0].Should().NotContain(EditedNote).And.NotContain("Hekim düzeltmesi");
    }

    [Fact]
    public async Task Accept_With_Unchanged_Content_Counts_As_Not_Edited()
    {
        var ctx = await SeedAsync(DraftPermissions);
        var draftId = await GenerateDraftAsync(ctx, GeneratedNote);

        var accepted = await JsonAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { editedContent = new { note = GeneratedNote } }),
            HttpStatusCode.OK);

        accepted.GetProperty("status").GetString().Should().Be("Accepted");
    }

    [Fact]
    public async Task Discard_Writes_Nothing_And_Clears_Content()
    {
        var ctx = await SeedAsync(DraftPermissions);
        var draftId = await GenerateDraftAsync(ctx, GeneratedNote);

        var discarded = await JsonAsync(await ctx.Http.PostAsync($"/api/v1/ai/drafts/{draftId}/discard", null), HttpStatusCode.OK);

        discarded.GetProperty("status").GetString().Should().Be("Discarded");
        (await ctx.ExaminationNotesAsync()).Should().Be(OriginalNote);
        var stored = await ctx.DraftAsync(draftId);
        (stored.Status, stored.Content, stored.DecidedByUserId).Should().Be((AiDraftStatus.Discarded, (string?)null, ctx.UserId));
        await ProblemAsync(await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { }), HttpStatusCode.Conflict, "AiDrafts.NotPending");
    }

    [Fact]
    public async Task Failed_Apply_Rolls_Back_Everything_And_Draft_Stays_Pending()
    {
        var ctx = await SeedAsync(DraftPermissions);
        var draftId = await GenerateDraftAsync(ctx, GeneratedNote);

        // Uygulayıcı önce muayeneyi günceller, sonra başarısız döner: transaction geri alınır.
        await ProblemAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept",
                new { editedContent = new { note = TestExaminationNoteApplier.FailAfterWriteNote } }),
            HttpStatusCode.BadRequest, "Test.ApplyFailed");
        await ProblemAsync(
            await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { editedContent = new { other = "x" } }),
            HttpStatusCode.BadRequest, "AiDrafts.Validation");

        (await ctx.ExaminationNotesAsync()).Should().Be(OriginalNote);
        var stored = await ctx.DraftAsync(draftId);
        stored.Status.Should().Be(AiDraftStatus.Draft);
        JsonNode.Parse(stored.Content!)!["note"]!.GetValue<string>().Should().Be(GeneratedNote);

        await JsonAsync(await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { }), HttpStatusCode.OK);
        (await ctx.ExaminationNotesAsync()).Should().Be(GeneratedNote);
    }

    [Fact]
    public async Task Drafts_Require_Feature_Permission_Same_Clinic_And_Ai_Use()
    {
        var ctx = await SeedAsync(DraftPermissions);
        var draftId = await GenerateDraftAsync(ctx, GeneratedNote);

        var readOnly = await ctx.ClientForSameClinicAsync(_factory.Services, [PermissionCatalog.Ai.Use, PermissionCatalog.Examinations.Read]);
        await ProblemAsync(await readOnly.GetAsync($"/api/v1/ai/drafts/{draftId}"), HttpStatusCode.Forbidden, "AiDrafts.Forbidden");
        await ProblemAsync(await readOnly.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { }), HttpStatusCode.Forbidden, "AiDrafts.Forbidden");
        (await JsonAsync(await readOnly.GetAsync($"/api/v1/ai/drafts?targetType=Examination&targetId={ctx.ExaminationId}"), HttpStatusCode.OK))
            .GetArrayLength().Should().Be(0, "izinsiz özelliğin taslağı listede görünmez");

        var noAi = await ctx.ClientForSameClinicAsync(_factory.Services, [PermissionCatalog.Examinations.Update]);
        (await noAi.GetAsync($"/api/v1/ai/drafts/{draftId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var otherClinic = await SeedAsync(DraftPermissions);
        await ProblemAsync(await otherClinic.Http.GetAsync($"/api/v1/ai/drafts/{draftId}"), HttpStatusCode.NotFound, "AiDrafts.NotFound");
        await ProblemAsync(await otherClinic.Http.PostAsync($"/api/v1/ai/drafts/{draftId}/discard", null), HttpStatusCode.NotFound, "AiDrafts.NotFound");

        (await ctx.DraftAsync(draftId)).Status.Should().Be(AiDraftStatus.Draft);
    }

    [Fact]
    public async Task Expired_Draft_Cannot_Be_Decided_And_Expiry_Job_Clears_Content()
    {
        var ctx = await SeedAsync(DraftPermissions);
        var draftId = await ctx.InsertDraftAsync(createdAtUtc: DateTime.UtcNow.AddDays(-31), lifetime: TimeSpan.FromDays(30));

        var draft = await JsonAsync(await ctx.Http.GetAsync($"/api/v1/ai/drafts/{draftId}"), HttpStatusCode.OK);
        draft.GetProperty("status").GetString().Should().Be("Expired");
        draft.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Null);
        await ProblemAsync(await ctx.Http.PostAsJsonAsync($"/api/v1/ai/drafts/{draftId}/accept", new { }), HttpStatusCode.Gone, "AiDrafts.Expired");
        (await ctx.ExaminationNotesAsync()).Should().Be(OriginalNote);

        var job = _factory.Services.GetServices<IHostedService>().OfType<AiDraftExpiryHostedService>().Single();
        (await job.RunOnceAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);

        var stored = await ctx.DraftAsync(draftId);
        (stored.Status, stored.Content, stored.DecidedByUserId).Should().Be((AiDraftStatus.Expired, (string?)null, (Guid?)null));
        stored.DecidedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Feature_Without_Applier_Does_Not_Call_The_Model_Or_Create_A_Draft()
    {
        var ctx = await SeedAsync(DraftPermissions);
        var before = _ai.Requests.Count;

        var result = await InvokeForDraftAsync(ctx, AiFeature.DischargeInstructions);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AiDrafts.FeatureNotSupported");
        _ai.Requests.Count.Should().Be(before);
        (await ctx.UsageRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_Model_Output_Records_Usage_Without_Draft()
    {
        var ctx = await SeedAsync(DraftPermissions);
        _ai.Respond("""{"unexpected":true}""");

        var result = await InvokeForDraftAsync(ctx, AiFeature.OwnerExplanation);

        result.Error.Code.Should().Be("Ai.InvalidOutput");
        var usage = (await ctx.UsageRecordsAsync()).Should().ContainSingle().Which;
        (usage.Outcome, usage.DraftId).Should().Be((AiUsageOutcome.InvalidOutput, (Guid?)null));
        (await ctx.DraftCountAsync()).Should().Be(0);
    }

    private async Task<Guid> GenerateDraftAsync(DraftTestContext ctx, string note)
    {
        _ai.Respond(new JsonObject { ["note"] = note }.ToJsonString(), inputTokens: 500, outputTokens: 50);
        var result = await InvokeForDraftAsync(ctx, AiFeature.OwnerExplanation);
        result.IsSuccess.Should().BeTrue(result.IsSuccess ? null : result.Error.Code);
        return result.Value!.Id;
    }

    /// <summary>Özellik işleyicisinin (3. dalga) yapacağı çağrı: denetimden geçmiş istek → taslak.</summary>
    private async Task<Result<AiDraft>> InvokeForDraftAsync(DraftTestContext ctx, AiFeature feature)
    {
        await using var scope = _factory.Services.CreateTenantScope(ctx.TenantId);
        var invoker = scope.ServiceProvider.GetRequiredService<AiInvoker>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
        return await invoker.InvokeForDraftAsync(
            new AiInvocation(
                ctx.TenantId,
                ctx.ClinicId,
                ctx.UserId,
                feature,
                ctx.ExaminationId,
                [new AiSourceRef("Examination", ctx.ExaminationId), new AiSourceRef("Pet", ctx.PetId)],
                new AiUsageGrant(options.Model, options.PriceOf(options.Model)!, 1_000, 500),
                "test-note/1",
                "Sistem istemi",
                "Kullanıcı içeriği",
                new JsonObject { ["type"] = "object" }),
            new AiDraftTarget("Examination", ctx.ExaminationId),
            text => JsonNode.Parse(text ?? "null") is JsonObject o && o["note"] is JsonValue ? o : null,
            CancellationToken.None);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
        => (await JsonAsync(response, expected)).GetProperty("code").GetString().Should().Be(code);

    private async Task<DraftTestContext> SeedAsync(string[] permissions)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId, clinicId, petId, userId, examinationId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"AiD-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            email = $"ai-draft-{Guid.NewGuid():N}@example.com";
            userId = await AddClinicUserAsync(db, hasher, email, tenant.Id, clinic.Id);

            var client = new Client(tenant.Id, "Taslak Sahibi", DateTime.UtcNow, "905320000000");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenant.Id, client.Id, $"Tarcin{Guid.NewGuid():N}"[..14], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();

            var exam = new Examination(tenant.Id, clinic.Id, pet.Id, null, DateTime.UtcNow.AddHours(-1), "Kontrol", "Genel durum iyi", null, OriginalNote, DateTime.UtcNow);
            db.Add(exam);

            var settings = ClinicAiSettings.Create(tenant.Id, clinic.Id, DateTime.UtcNow);
            settings.SetEnabled(true, null, DateTime.UtcNow);
            db.Add(settings);
            await db.SaveChangesAsync();
            (tenantId, clinicId, petId, examinationId) = (tenant.Id, clinic.Id, pet.Id, exam.Id);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions, clinicId);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new DraftTestContext(_factory, http, tenantId, clinicId, userId, petId, examinationId);
    }

    private static async Task<Guid> AddClinicUserAsync(AppDbContext db, IPasswordHasher hasher, string email, Guid tenantId, Guid clinicId)
    {
        var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
        var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
        db.UserClinics.Add(new UserClinic(user.Id, clinicId, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return user.Id;
    }

    private sealed record DraftTestContext(
        WebApplicationFactory<global::Program> Factory,
        HttpClient Http,
        Guid TenantId,
        Guid ClinicId,
        Guid UserId,
        Guid PetId,
        Guid ExaminationId)
    {
        private IServiceProvider Services => Factory.Services;

        public async Task<HttpClient> ClientForSameClinicAsync(IServiceProvider services, string[] permissions)
        {
            var hasher = services.GetRequiredService<IPasswordHasher>();
            var email = $"ai-draft-2-{Guid.NewGuid():N}@example.com";
            await using (var scope = services.CreateSystemScope())
                await AddClinicUserAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), hasher, email, TenantId, ClinicId);

            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(services, email, permissions, ClinicId);
            var http = Factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        public async Task<Guid> InsertDraftAsync(DateTime createdAtUtc, TimeSpan lifetime)
        {
            var draft = AiDraft.Create(
                TenantId, ClinicId, AiFeature.OwnerExplanation, "Examination", ExaminationId,
                [new AiSourceRef("Examination", ExaminationId)], new JsonObject { ["note"] = GeneratedNote }.ToJsonString(),
                "claude-haiku-5-5", "test-note/1", UserId, createdAtUtc, lifetime);
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(draft);
            await db.SaveChangesAsync();
            return draft.Id;
        }

        public async Task<string?> ExaminationNotesAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Examinations.AsNoTracking().Where(e => e.Id == ExaminationId).Select(e => e.Notes).SingleAsync();
        }

        public async Task<AiDraft> DraftAsync(Guid draftId)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AiDrafts.AsNoTracking().SingleAsync(d => d.Id == draftId);
        }

        public async Task<int> DraftCountAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AiDrafts.AsNoTracking().CountAsync(d => d.ClinicId == ClinicId);
        }

        public async Task<IReadOnlyList<AiUsageRecord>> UsageRecordsAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AiUsageRecords.AsNoTracking().Where(r => r.ClinicId == ClinicId).ToListAsync();
        }

        public async Task<IReadOnlyList<string>> AuditPayloadsAsync(string action, Guid draftId)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var target = $"AiDraftId={draftId}";
            return await db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == action && a.TargetId == target)
                .Select(a => a.RequestPayload ?? string.Empty)
                .ToListAsync();
        }
    }

    /// <summary>
    /// Test uygulayıcısı (3. dalga özelliklerinin örneği): <c>{"note": "..."}</c> içeriğini mevcut <see cref="UpdateExaminationCommand"/>
    /// ile muayene notuna yazar. <see cref="FailAfterWriteNote"/> notu yazdıktan sonra başarısız döner (geri alma denemesi).
    /// </summary>
    private sealed class TestExaminationNoteApplier : IAiDraftApplier
    {
        public const string FailAfterWriteNote = "__yazdiktan-sonra-basarisiz__";

        private readonly IReadRepository<Examination> _examinations;
        private readonly ISender _sender;

        public TestExaminationNoteApplier(IReadRepository<Examination> examinations, ISender sender)
        {
            _examinations = examinations;
            _sender = sender;
        }

        public AiFeature Feature => AiFeature.OwnerExplanation;

        public string RequiredPermission => PermissionCatalog.Examinations.Update;

        public async Task<Result<Guid?>> ApplyAsync(AiDraftApplication application, CancellationToken ct)
        {
            if (application.Content["note"] is not JsonValue value || !value.TryGetValue<string>(out var note) || string.IsNullOrWhiteSpace(note))
                return Result<Guid?>.Failure(AiErrors.InvalidDraftContent("Not alanı zorunludur."));

            var exam = await _examinations.GetByIdAsync(application.TargetId, ct);
            if (exam is null || exam.ClinicId != application.ClinicId)
                return Result<Guid?>.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");

            var updated = await _sender.Send(
                new UpdateExaminationCommand(
                    exam.Id, null, null, null, exam.ExaminedAtUtc, exam.VisitReason, null, null, note,
                    ExaminationRowVersion.Encode(exam.RowVersion)),
                ct);
            if (!updated.IsSuccess)
                return Result<Guid?>.Failure(updated.Error);

            return note == FailAfterWriteNote
                ? Result<Guid?>.Failure("Test.ApplyFailed", "Uygulama sonradan başarısız.")
                : Result<Guid?>.Success(exam.Id);
        }
    }

    /// <summary>Sahte sağlayıcı: istekleri kaydeder, ayarlanan yanıtı döner. Sınıf içi testler sırayla çalışır.</summary>
    private sealed class FakeAiTextGenerator : IAiTextGenerator
    {
        private AiGenerationResult _next = new(AiGenerationStatus.Completed, """{"note":"x"}""", 100, 10);

        public bool IsConfigured => true;

        public List<AiGenerationRequest> Requests { get; } = [];

        public void Respond(string text, int inputTokens = 100, int outputTokens = 10)
            => _next = new AiGenerationResult(AiGenerationStatus.Completed, text, inputTokens, outputTokens);

        public Task<AiGenerationResult> GenerateJsonAsync(AiGenerationRequest request, CancellationToken ct)
        {
            lock (Requests)
                Requests.Add(request);
            return Task.FromResult(_next);
        }
    }
}
