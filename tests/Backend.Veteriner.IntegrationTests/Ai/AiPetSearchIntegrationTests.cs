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
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Ai;

/// <summary>
/// Doğal dille hasta araması (AI_PATIENT_SEARCH_API_CONTRACT.md): HTTP + gerçek SQL Server (LocalDB), sağlayıcı sahte. Modele
/// giden girdi (kayıt yok, kimlik temizliği), filtre doğrulaması, ölçütlerin VE birleşimi, izin, klinik ve kiracı yalıtımı.
/// Her test kendi kiracısını (ücretli Premium: AI kredisi var) ve kliniğini kurar; kiracı geneli hayvan araması başka testten etkilenmez.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AiPetSearchIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string OwnerName = "Deniz Aydemiroğlu";

    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Ai.Use,
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Treatments.Read,
        PermissionCatalog.Prescriptions.Read,
        PermissionCatalog.Vaccinations.Read,
    ];

    private readonly WebApplicationFactory<global::Program> _factory;
    private readonly FakeAiTextGenerator _ai = new();

    public AiPetSearchIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerator>();
            services.AddSingleton<IAiTextGenerator>(_ai);
        }));

    [Fact]
    public async Task Interpret_Sends_Only_The_Sentence_And_Catalogs_And_Returns_A_Validated_Filter()
    {
        var ctx = await SeedAsync(AllPermissions);
        var pet = await ctx.AddPetAsync("CAT");
        await ctx.AddExaminationAsync(pet, DateTime.UtcNow.AddDays(-5), "Kusma");
        await ctx.AddTermAsync("Safra kusması");
        _ai.Respond("""
            {"species":["CAT","DRAGON"],"from":"2026-08-19","clinicalTerms":["kusma","kus%ma"],
             "unsupported":["ad ile arama"]}
            """, inputTokens: 1_500, outputTokens: 80);

        var body = await JsonAsync(
            await ctx.Http.PostAsJsonAsync("/api/v1/ai/pet-search/interpret",
                new { query = "Son 3 ayda kusmayla gelen kediler, sahibi 05321112233" }),
            HttpStatusCode.OK);

        var filter = body.GetProperty("filter");
        filter.GetProperty("species").EnumerateArray().Select(e => e.GetString()).Should().Equal("CAT");
        filter.GetProperty("clinicalTerms").EnumerateArray().Select(e => e.GetString()).Should().Equal("kusma");
        filter.GetProperty("from").GetString().Should().Be("2026-08-19");
        body.GetProperty("isEmpty").GetBoolean().Should().BeFalse();
        body.GetProperty("unsupported").EnumerateArray().Select(e => e.GetString()).Should().Equal("ad ile arama");
        body.GetProperty("dropped").GetArrayLength().Should().Be(2);
        body.GetProperty("model").GetString().Should().Be("claude-haiku-5-5");

        var sent = _ai.Requests.Should().ContainSingle().Which;
        sent.UserContent.Should().Contain("kusmayla gelen kediler").And.Contain("\"code\":\"CAT\",\"name\":\"Kedi\"")
            .And.Contain("Safra kusması", "kliniğin sözlüğü modele ipucu olarak gider");
        sent.UserContent.Should().NotContain("05321112233", "uzun numara modele gitmez")
            .And.NotContain(ctx.PetName(pet)).And.NotContain("Aydemiroğlu", "model hiçbir kayıt görmez");
        sent.Schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();

        var record = (await ctx.UsageRecordsAsync()).Should().ContainSingle().Which;
        record.Feature.Should().Be(AiFeature.PetSearch);
        record.PromptVersion.Should().Be("pet-search/1");
        record.SubjectId.Should().BeNull();
        record.SourceRefs.Should().BeEmpty();
        record.Outcome.Should().Be(AiUsageOutcome.Succeeded);
    }

    [Fact]
    public async Task Interpret_Requires_Ai_And_Reports_Provider_Errors()
    {
        var ctx = await SeedAsync(AllPermissions);
        await ProblemAsync(await ctx.Http.PostAsJsonAsync("/api/v1/ai/pet-search/interpret", new { query = "  " }),
            HttpStatusCode.BadRequest, "AiSearch.Validation");

        _ai.Respond("filtre değil");
        await ProblemAsync(await ctx.Http.PostAsJsonAsync("/api/v1/ai/pet-search/interpret", new { query = "kediler" }),
            HttpStatusCode.BadGateway, "Ai.InvalidOutput");

        var disabled = await SeedAsync(AllPermissions, aiEnabled: false);
        await ProblemAsync(await disabled.Http.PostAsJsonAsync("/api/v1/ai/pet-search/interpret", new { query = "kediler" }),
            HttpStatusCode.Forbidden, "Ai.ClinicNotEnabled");

        var noAi = await SeedAsync(AllPermissions.Except([PermissionCatalog.Ai.Use]).ToArray());
        (await noAi.Http.PostAsJsonAsync("/api/v1/ai/pet-search/interpret", new { query = "kediler" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await noAi.Http.PostAsJsonAsync("/api/v1/ai/pet-search/results", new { filter = new { species = new[] { "CAT" } } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Results_Combine_Criteria_And_Stay_In_The_Active_Clinic()
    {
        var ctx = await SeedAsync(AllPermissions);
        var match = await ctx.AddPetAsync("CAT");
        var otherClinicCat = await ctx.AddPetAsync("CAT");
        var dog = await ctx.AddPetAsync("DOG");
        var oldVisit = await ctx.AddPetAsync("CAT");
        var diarrhoea = await ctx.AddPetAsync("CAT");
        var examId = await ctx.AddExaminationAsync(match, DateTime.UtcNow.AddDays(-10), "Kusma, iştahsızlık");
        await ctx.AddExaminationAsync(otherClinicCat, DateTime.UtcNow.AddDays(-10), "Kusma", ctx.OtherClinicId);
        await ctx.AddExaminationAsync(dog, DateTime.UtcNow.AddDays(-10), "Kusma");
        await ctx.AddExaminationAsync(oldVisit, DateTime.UtcNow.AddDays(-200), "Kusma");
        await ctx.AddExaminationAsync(diarrhoea, DateTime.UtcNow.AddDays(-10), "İshal");
        var before = _ai.Requests.Count;

        var body = await SearchAsync(ctx, new
        {
            species = new[] { "CAT" },
            clinicalTerms = new[] { "kusma" },
            from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)),
        });

        var item = body.GetProperty("items").EnumerateArray().Should().ContainSingle().Which;
        item.GetProperty("petId").GetGuid().Should().Be(match);
        item.GetProperty("clientName").GetString().Should().Be(OwnerName);
        item.GetProperty("speciesCode").GetString().Should().Be("CAT");
        var source = item.GetProperty("matches").EnumerateArray().Should().ContainSingle().Which;
        source.GetProperty("type").GetString().Should().Be("Examination");
        source.GetProperty("id").GetGuid().Should().Be(examId);
        source.GetProperty("text").GetString().Should().Be("Kusma, iştahsızlık");
        body.GetProperty("totalCount").GetInt32().Should().Be(1);
        _ai.Requests.Count.Should().Be(before, "arama AI çağırmaz");
        (await ctx.UsageRecordsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Overdue_Vaccine_Uses_The_Due_Rule_And_Skips_Deceased_Pets()
    {
        var ctx = await SeedAsync(AllPermissions);
        var overdue = await ctx.AddPetAsync("DOG");
        var upcoming = await ctx.AddPetAsync("DOG");
        var applied = await ctx.AddPetAsync("DOG");
        var deceased = await ctx.AddPetAsync("DOG", deceased: true);
        var otherVaccine = await ctx.AddPetAsync("DOG");
        await ctx.AddVaccinationAsync(overdue, "Karma (DHPPi+L)", VaccinationStatus.Scheduled, null, DateTime.UtcNow.AddDays(-10));
        await ctx.AddVaccinationAsync(upcoming, "Karma (DHPPi+L)", VaccinationStatus.Scheduled, null, DateTime.UtcNow.AddDays(10));
        await ctx.AddVaccinationAsync(applied, "Karma (DHPPi+L)", VaccinationStatus.Applied, DateTime.UtcNow.AddDays(-10), null);
        await ctx.AddVaccinationAsync(deceased, "Karma (DHPPi+L)", VaccinationStatus.Scheduled, null, DateTime.UtcNow.AddDays(-10));
        await ctx.AddVaccinationAsync(otherVaccine, "Kuduz", VaccinationStatus.Scheduled, null, DateTime.UtcNow.AddDays(-10));

        var body = await SearchAsync(ctx, new { species = new[] { "DOG" }, vaccineTerms = new[] { "karma" }, vaccineState = "Overdue" });

        var item = body.GetProperty("items").EnumerateArray().Should().ContainSingle().Which;
        item.GetProperty("petId").GetGuid().Should().Be(overdue);
        item.GetProperty("matches")[0].GetProperty("isOverdue").GetBoolean().Should().BeTrue();

        var any = await SearchAsync(ctx, new { vaccineTerms = new[] { "karma" } });
        any.GetProperty("filter").GetProperty("vaccineState").GetString().Should().Be("Any");
        any.GetProperty("totalCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Medication_Searches_Prescriptions_Or_Treatments_The_User_Can_Read()
    {
        var all = await SeedAsync(AllPermissions);
        var prescribed = await all.AddPetAsync("CAT");
        var treated = await all.AddPetAsync("CAT");
        await all.AddPrescriptionAsync(prescribed, "Amoksisilin 250 mg");
        await all.AddTreatmentAsync(treated, "Amoksisilin enjeksiyonu");

        var both = await SearchAsync(all, new { medicationTerms = new[] { "amoksisilin" } });
        both.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("petId").GetGuid())
            .Should().BeEquivalentTo(new[] { prescribed, treated });
        both.GetProperty("omittedTypes").GetArrayLength().Should().Be(0);

        var noTreatments = await all.WithPermissionsAsync(AllPermissions.Except([PermissionCatalog.Treatments.Read]).ToArray());
        var prescriptionsOnly = await SearchAsync(noTreatments, new { medicationTerms = new[] { "amoksisilin" } });
        prescriptionsOnly.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("petId").GetGuid()).Should().Equal(prescribed);
        prescriptionsOnly.GetProperty("omittedTypes").EnumerateArray().Select(e => e.GetString()).Should().Equal("Treatment");
    }

    [Fact]
    public async Task Criterion_Without_Read_Permission_Is_Forbidden_Instead_Of_Widening_Results()
    {
        var ctx = await SeedAsync(AllPermissions.Except([PermissionCatalog.Vaccinations.Read]).ToArray());

        await ProblemAsync(
            await ctx.Http.PostAsJsonAsync("/api/v1/ai/pet-search/results",
                new { filter = new { species = new[] { "DOG" }, vaccineState = "Overdue" } }),
            HttpStatusCode.Forbidden, "AiSearch.Forbidden");

        var noMedication = await ctx.WithPermissionsAsync(
            AllPermissions.Except([PermissionCatalog.Prescriptions.Read, PermissionCatalog.Treatments.Read]).ToArray());
        await ProblemAsync(
            await noMedication.Http.PostAsJsonAsync("/api/v1/ai/pet-search/results", new { filter = new { medicationTerms = new[] { "amoksisilin" } } }),
            HttpStatusCode.Forbidden, "AiSearch.Forbidden");

        await ProblemAsync(
            await ctx.Http.PostAsJsonAsync("/api/v1/ai/pet-search/results", new { filter = new { species = new[] { "DRAGON" } } }),
            HttpStatusCode.BadRequest, "AiSearch.Validation");
    }

    [Fact]
    public async Task Pet_Criteria_Search_Only_The_Own_Tenant()
    {
        var mine = await SeedAsync(AllPermissions);
        var other = await SeedAsync(AllPermissions);
        var myCat = await mine.AddPetAsync("CAT", gender: PetGender.Female, birthDate: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-3));
        await mine.AddPetAsync("CAT", gender: PetGender.Male, birthDate: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-3));
        await mine.AddPetAsync("CAT", gender: PetGender.Female, birthDate: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-12));
        var foreignCat = await other.AddPetAsync("CAT", gender: PetGender.Female, birthDate: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-3));
        await other.AddExaminationAsync(foreignCat, DateTime.UtcNow.AddDays(-1), "Kusma");

        var body = await SearchAsync(mine, new { species = new[] { "CAT" }, sex = "Female", ageMaxYears = 5 });
        var item = body.GetProperty("items").EnumerateArray().Should().ContainSingle().Which;
        item.GetProperty("petId").GetGuid().Should().Be(myCat);
        item.GetProperty("ageYears").GetInt32().Should().Be(3);
        item.GetProperty("matches").GetArrayLength().Should().Be(0);

        (await SearchAsync(mine, new { clinicalTerms = new[] { "kusma" } })).GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    private static async Task<JsonElement> SearchAsync(PetSearchTestContext ctx, object filter)
        => await JsonAsync(await ctx.Http.PostAsJsonAsync("/api/v1/ai/pet-search/results", new { filter, page = 1, pageSize = 50 }), HttpStatusCode.OK);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
        => (await JsonAsync(response, expected)).GetProperty("code").GetString().Should().Be(code);

    private async Task<PetSearchTestContext> SeedAsync(string[] permissions, bool aiEnabled = true)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId, clinicId, otherClinicId, clientId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"AiAra-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
            db.Tenants.Add(tenant);
            // Paket hediyesi yalnız ücretli abonelikte (denemede kota 0, ADR-044 madde 5).
            var subscription = TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Premium, DateTime.UtcNow, 14);
            subscription.ActivatePaidPlan(SubscriptionPlanCode.Premium, DateTime.UtcNow);
            db.TenantSubscriptions.Add(subscription);
            var clinic = new Clinic(tenant.Id, $"Ara-{Guid.NewGuid():N}"[..14], "Izmir");
            var otherClinic = new Clinic(tenant.Id, $"Ara2-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.AddRange(clinic, otherClinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"ai-ara-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));

            var client = new Client(tenant.Id, OwnerName, DateTime.UtcNow, "905321112233");
            db.Clients.Add(client);
            if (aiEnabled)
            {
                var settings = ClinicAiSettings.Create(tenant.Id, clinic.Id, DateTime.UtcNow);
                settings.SetEnabled(true, null, DateTime.UtcNow);
                db.Add(settings);
            }

            await db.SaveChangesAsync();
            (tenantId, clinicId, otherClinicId, clientId) = (tenant.Id, clinic.Id, otherClinic.Id, client.Id);
        }

        var ctx = new PetSearchTestContext(_factory, email, tenantId, clinicId, otherClinicId, clientId, new HttpClient(), new Dictionary<Guid, string>());
        return await ctx.WithPermissionsAsync(permissions);
    }

    private sealed record PetSearchTestContext(
        WebApplicationFactory<global::Program> Factory,
        string Email,
        Guid TenantId,
        Guid ClinicId,
        Guid OtherClinicId,
        Guid ClientId,
        HttpClient Http,
        Dictionary<Guid, string> PetNames)
    {
        private IServiceProvider Services => Factory.Services;

        public string PetName(Guid petId) => PetNames[petId];

        /// <summary>Aynı kullanıcı ve klinik, başka izinlerle yeni oturum.</summary>
        public async Task<PetSearchTestContext> WithPermissionsAsync(string[] permissions)
        {
            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(Services, Email, permissions, ClinicId);
            var http = Factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return this with { Http = http };
        }

        private async Task<Guid> SaveAsync(object entity, Guid id)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(entity);
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> AddPetAsync(string speciesCode, PetGender? gender = null, DateOnly? birthDate = null, bool deceased = false)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.Where(s => s.Code == speciesCode).Select(s => s.Id).SingleAsync();
            var name = $"Mavi{Guid.NewGuid():N}"[..12];
            var pet = new Pet(TenantId, ClientId, name, speciesId, birthDate: birthDate, gender: gender);
            if (deceased)
                pet.SetStatus(PetStatus.Deceased, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow));
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            PetNames[pet.Id] = name;
            return pet.Id;
        }

        public Task<Guid> AddTermAsync(string name)
        {
            var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Complaint, name, DateTime.UtcNow);
            return SaveAsync(term, term.Id);
        }

        public Task<Guid> AddExaminationAsync(Guid petId, DateTime at, string visitReason, Guid? clinicId = null)
        {
            var exam = new Examination(TenantId, clinicId ?? ClinicId, petId, null, at, visitReason, "Genel durum iyi.", null, null, DateTime.UtcNow);
            return SaveAsync(exam, exam.Id);
        }

        public async Task<Guid> AddVaccinationAsync(Guid petId, string name, VaccinationStatus status, DateTime? appliedAt, DateTime? dueAt)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var definitionId = await db.VaccineDefinitions.Select(v => v.Id).FirstAsync();
            var vaccination = new Vaccination(TenantId, petId, ClinicId, null, definitionId, name, status, appliedAt, dueAt, null, DateTime.UtcNow);
            db.Vaccinations.Add(vaccination);
            await db.SaveChangesAsync();
            return vaccination.Id;
        }

        public async Task<Guid> AddPrescriptionAsync(Guid petId, string itemName)
        {
            var prescription = new Prescription(TenantId, ClinicId, petId, null, null, DateTime.UtcNow.AddDays(-3), "Reçete", "Kullanım talimatı", null, null, DateTime.UtcNow);
            prescription.ReplaceItems([new PrescriptionItemDraft(itemName, null, "Günde 2 kez", 1m)]);
            return await SaveAsync(prescription, prescription.Id);
        }

        public Task<Guid> AddTreatmentAsync(Guid petId, string title)
        {
            var treatment = new Treatment(TenantId, ClinicId, petId, null, DateTime.UtcNow.AddDays(-2), title, "Kas içi uygulandı.", null, null, DateTime.UtcNow);
            return SaveAsync(treatment, treatment.Id);
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
        private AiGenerationResult _next = new(AiGenerationStatus.Completed, "{}", 100, 10);

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
