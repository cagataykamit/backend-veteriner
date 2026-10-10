using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Clients;

/// <summary>
/// POST /clients/quick-register: gerçek SQL Server (LocalDB, izole test veritabanı). Atomiklik (hayvan başarısızsa
/// müşteri kalmaz, outbox olayları da), yinelenen müşteri, yetki ve kiracı izolasyonu.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class QuickRegisterIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] BothPermissions = [PermissionCatalog.Clients.Create, PermissionCatalog.Pets.Create];

    private readonly CustomWebApplicationFactory _factory;
    private Guid? _speciesId;

    public QuickRegisterIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task QuickRegister_Should_Create_Client_And_Pet_With_Normalized_Phone_And_Outbox_Events_And_No_Visit()
    {
        var http = await CreateClientAsync(BothPermissions);
        var name = UniqueName();

        var response = await http.PostAsJsonAsync("/api/v1/clients/quick-register", Body(name, "0555 111 22 33"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var result = (await response.Content.ReadFromJsonAsync<QuickRegisterClientResultDto>())!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == result.ClientId);
        client.FullName.Should().Be(name);
        client.PhoneNormalized.Should().Be("905551112233");
        var pet = await db.Pets.AsNoTracking().SingleAsync(p => p.Id == result.PetId);
        pet.ClientId.Should().Be(client.Id);
        pet.TenantId.Should().Be(client.TenantId);
        pet.Name.Should().Be("Pamuk");

        (await db.OutboxMessages.AsNoTracking().CountAsync(m =>
            m.Type == ClientIntegrationEventTypes.Created && m.Payload.Contains(result.ClientId.ToString())))
            .Should().Be(1);
        (await db.OutboxMessages.AsNoTracking().CountAsync(m =>
            m.Type == PetIntegrationEventTypes.Created && m.Payload.Contains(result.PetId.ToString())))
            .Should().Be(1);
        (await db.Visits.CountAsync(v => v.PetId == result.PetId)).Should().Be(0, "hızlı kayıt Visit açmaz");
    }

    [Theory]
    [InlineData("5551112233")]
    [InlineData("+90 555 111 22 33")]
    [InlineData("905551112233")]
    public async Task QuickRegister_Should_Normalize_Accepted_Phone_Formats(string phone)
    {
        var http = await CreateClientAsync(BothPermissions);

        var response = await http.PostAsJsonAsync("/api/v1/clients/quick-register", Body(UniqueName(), phone));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<QuickRegisterClientResultDto>())!.ClientId;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Clients.AsNoTracking().SingleAsync(c => c.Id == id)).PhoneNormalized.Should().Be("905551112233");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("02121234567")]
    public async Task QuickRegister_Should_Reject_Missing_Or_Invalid_Phone_Without_Creating_Anything(string phone)
    {
        var http = await CreateClientAsync(BothPermissions);
        var name = UniqueName();

        var response = await http.PostAsJsonAsync("/api/v1/clients/quick-register", Body(name, phone));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ClientCountAsync(name)).Should().Be(0);
    }

    [Fact]
    public async Task QuickRegister_Should_Roll_Back_Client_And_Outbox_When_Pet_Step_Returns_Business_Failure()
    {
        var http = await CreateClientAsync(BothPermissions);
        var name = UniqueName();

        var response = await http.PostAsJsonAsync(
            "/api/v1/clients/quick-register", Body(name, "0555 111 22 33", speciesId: Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Pets.SpeciesNotFound");
        (await ClientCountAsync(name)).Should().Be(0, "hayvan başarısızsa müşteri kalmamalı");
        (await OutboxCountContainingAsync(name)).Should().Be(0, "geri alınan müşterinin outbox olayı kalmamalı");
    }

    [Fact]
    public async Task QuickRegister_Should_Roll_Back_Client_When_Pet_Step_Throws_Validation_Exception()
    {
        var http = await CreateClientAsync(BothPermissions);
        var name = UniqueName();
        var futureBirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));

        var response = await http.PostAsJsonAsync(
            "/api/v1/clients/quick-register", Body(name, "0555 111 22 33", birthDate: futureBirthDate));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ClientCountAsync(name)).Should().Be(0, "müşteri kaydedildikten sonra hayvan doğrulaması patlarsa geri alınmalı");
        (await OutboxCountContainingAsync(name)).Should().Be(0);
    }

    [Fact]
    public async Task QuickRegister_Should_Return_409_For_Same_Name_And_Phone_And_Create_Nothing_More()
    {
        var http = await CreateClientAsync(BothPermissions);
        var name = UniqueName();
        (await http.PostAsJsonAsync("/api/v1/clients/quick-register", Body(name, "0555 111 22 33")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await http.PostAsJsonAsync(
            "/api/v1/clients/quick-register", Body(name.ToUpperInvariant(), "5551112233", petName: "Karamel"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(second)).Should().Be("Clients.DuplicateClient");
        (await ClientCountAsync(name)).Should().Be(1);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Pets.CountAsync(p => p.Name == "Karamel")).Should().Be(0);
    }

    [Fact]
    public async Task QuickRegister_Should_Allow_Same_Phone_With_Different_Name()
    {
        var http = await CreateClientAsync(BothPermissions);
        const string phone = "0555 222 33 44";
        (await http.PostAsJsonAsync("/api/v1/clients/quick-register", Body(UniqueName(), phone)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await http.PostAsJsonAsync("/api/v1/clients/quick-register", Body(UniqueName(), phone));

        second.StatusCode.Should().Be(HttpStatusCode.Created, "mevcut kural yalnızca ad+telefon çiftini mükerrer sayar");
    }

    [Theory]
    [InlineData("Clients.Create")]
    [InlineData("Pets.Create")]
    public async Task QuickRegister_Should_Require_Both_Clients_Create_And_Pets_Create(string onlyPermission)
    {
        var http = await CreateClientAsync([onlyPermission]);
        var name = UniqueName();

        var response = await http.PostAsJsonAsync("/api/v1/clients/quick-register", Body(name, "0555 111 22 33"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientCountAsync(name)).Should().Be(0);
    }

    [Fact]
    public async Task QuickRegister_Should_Isolate_Tenants_Duplicate_Rule_And_Ownership()
    {
        var http = await CreateClientAsync(BothPermissions);
        var name = UniqueName();
        Guid defaultTenantId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var foreignTenant = new Tenant($"Foreign-{Guid.NewGuid():N}"[..20]);
            db.Tenants.Add(foreignTenant);
            await db.SaveChangesAsync();
            db.Clients.Add(new Client(foreignTenant.Id, name, "0555 111 22 33"));
            await db.SaveChangesAsync();
            defaultTenantId = (await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == DataSeeder.DefaultTenantName)).Id;
        }

        var response = await http.PostAsJsonAsync("/api/v1/clients/quick-register", Body(name, "0555 111 22 33"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, "başka kiracıdaki aynı ad+telefon mükerrer sayılmaz");
        var id = (await response.Content.ReadFromJsonAsync<QuickRegisterClientResultDto>())!.ClientId;
        await using var verify = _factory.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.Clients.AsNoTracking().SingleAsync(c => c.Id == id)).TenantId.Should().Be(defaultTenantId);
    }

    // ---- Yardımcılar ----

    private static string UniqueName() => $"Qr Musteri {Guid.NewGuid():N}"[..28];

    private object Body(
        string fullName,
        string phone,
        string petName = "Pamuk",
        Guid? speciesId = null,
        DateOnly? birthDate = null)
        => new
        {
            fullName,
            phone,
            petName,
            speciesId = speciesId ?? GetSpeciesId(),
            birthDate,
        };

    private Guid GetSpeciesId()
    {
        if (_speciesId is { } cached)
            return cached;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _speciesId = db.Species.AsNoTracking().OrderBy(s => s.DisplayOrder).Select(s => s.Id).First();
        return _speciesId.Value;
    }

    private async Task<HttpClient> CreateClientAsync(IReadOnlyCollection<string> permissions)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, _) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<int> ClientCountAsync(string fullName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Clients.AsNoTracking().CountAsync(c => c.FullName == fullName);
    }

    private async Task<int> OutboxCountContainingAsync(string text)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OutboxMessages.AsNoTracking().CountAsync(m => m.Payload.Contains(text));
    }
}
