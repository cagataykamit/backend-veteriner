using Backend.Veteriner.Application.Clients.Queries.GetList;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.Queries.GetList;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Clients.ReadModels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Search;

/// <summary>SEARCH-001: sahip/hasta aramasının gerçek SQL Server üzerinde davranışı (Türkçe harf, telefon, mikroçip, tenant).</summary>
[Collection("pet-projection")]
public sealed class DailySearchIntegrationTests
{
    private const string Chip = "985121001234567";

    private readonly PetProjectionWebApplicationFactory _factory;

    public DailySearchIntegrationTests(PetProjectionWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("sule", "Şule Güneş")]
    [InlineData("SULE", "Şule Güneş")]
    [InlineData("şule", "Şule Güneş")]
    [InlineData("ŞULE", "Şule Güneş")]
    [InlineData("guNES sule", "Şule Güneş")]
    [InlineData("gunes,  şule.", "Şule Güneş")]
    [InlineData("ipek", "İpek Kırmızı")]
    [InlineData("İPEK", "İpek Kırmızı")]
    [InlineData("kirmizi", "İpek Kırmızı")]
    [InlineData("KIRMIZI", "İpek Kırmızı")]
    [InlineData("kırmızı", "İpek Kırmızı")]
    [InlineData("cagri ozturk", "Çağrı Öztürk")]
    [InlineData("ÇAĞRI ÖZTÜRK", "Çağrı Öztürk")]
    [InlineData("OZTURK", "Çağrı Öztürk")]
    [InlineData("0532 333 33 33", "Ayşe Yılmaz")]
    [InlineData("+90 532 333 33 33", "Ayşe Yılmaz")]
    [InlineData("905323333333", "Ayşe Yılmaz")]
    [InlineData("(0532) 333-33-33", "Ayşe Yılmaz")]
    [InlineData("532 333", "Ayşe Yılmaz")]
    public async Task Clients_Should_Find_ByTurkishLetterTokenAndPhoneFormat(string search, string expectedName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var seed = await SeedAsync(scope.ServiceProvider);

        var names = await ListClientNamesAsync(scope.ServiceProvider, seed.TenantId, search);

        names.Should().Equal(expectedName);
    }

    [Fact]
    public async Task Clients_Should_NotLeakAcrossTenants()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var seed = await SeedAsync(scope.ServiceProvider);
        var otherTenantId = (await SeedAsync(scope.ServiceProvider)).TenantId;

        var names = await ListClientNamesAsync(scope.ServiceProvider, seed.TenantId, "ayse yilmaz");
        var otherNames = await ListClientNamesAsync(scope.ServiceProvider, otherTenantId, "ayse yilmaz");

        names.Should().Equal("Ayşe Yılmaz");
        otherNames.Should().Equal("Ayşe Yılmaz");
    }

    [Theory]
    [InlineData("985121001234567", "Pamuk")]
    [InlineData("985 1210 0123 4567", "Pamuk")]
    [InlineData("1210 0123", "Pamuk")]
    [InlineData("isik", "Işık")]
    [InlineData("ISIK", "Işık")]
    [InlineData("ışık", "Işık")]
    [InlineData("sule", "Pamuk")]
    [InlineData("0532 111 11 11", "Pamuk")]
    public async Task Pets_Should_Find_ByChipTurkishLetterAndOwner(string search, string expectedPet)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var seed = await SeedAsync(scope.ServiceProvider);

        var names = await ListPetNamesAsync(scope.ServiceProvider, seed.TenantId, search);

        names.Should().Contain(expectedPet);
    }

    [Fact]
    public async Task Pets_Should_NotLeakAcrossTenants_ByChip()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var seed = await SeedAsync(scope.ServiceProvider);
        var other = await SeedAsync(scope.ServiceProvider);

        var names = await ListPetNamesAsync(scope.ServiceProvider, seed.TenantId, Chip);
        var otherNames = await ListPetNamesAsync(scope.ServiceProvider, other.TenantId, Chip);

        names.Should().Equal("Pamuk");
        otherNames.Should().Equal("Pamuk");
    }

    private static async Task<List<string>> ListClientNamesAsync(IServiceProvider sp, Guid tenantId, string search)
    {
        var handler = new GetClientsListQueryHandler(
            new FixedTenantContext(tenantId),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IClientReadModelReader>(),
            Options.Create(new QueryReadModelsOptions()));

        var result = await handler.Handle(
            new GetClientsListQuery(new PageRequest { Page = 1, PageSize = 50, Search = search }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return result.Value!.Items.Select(x => x.FullName).ToList();
    }

    private static async Task<List<string>> ListPetNamesAsync(IServiceProvider sp, Guid tenantId, string search)
    {
        var handler = new GetPetsListQueryHandler(
            new FixedTenantContext(tenantId),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IPetReadModelReader>(),
            Options.Create(new QueryReadModelsOptions()));

        var result = await handler.Handle(
            new GetPetsListQuery(new PageRequest { Page = 1, PageSize = 50, Search = search }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return result.Value!.Items.Select(x => x.Name).ToList();
    }

    /// <summary>Her çağrıda yeni tenant: sahipler + hastalar; mikroçip ve Türkçe adlar.</summary>
    private static async Task<(Guid TenantId, Guid ShuleId)> SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..20]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var shule = new Client(tenant.Id, "Şule Güneş", "905321111111");
        db.Clients.AddRange(
            shule,
            new Client(tenant.Id, "İpek Kırmızı", "905322222222"),
            new Client(tenant.Id, "Ayşe Yılmaz", "905323333333"),
            new Client(tenant.Id, "Çağrı Öztürk", "905324444444"));
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        db.Pets.AddRange(
            new Pet(tenant.Id, shule.Id, "Pamuk", speciesId, microchipNumber: Chip),
            new Pet(tenant.Id, shule.Id, "Işık", speciesId));
        await db.SaveChangesAsync();

        return (tenant.Id, shule.Id);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
