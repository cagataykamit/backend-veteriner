using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Examinations;

/// <summary>
/// Examinations list shared search lookup pilot smoke (CQRS-12D-3).
/// Yalnız search pet-id çözümlemesi Query DB'ye taşınır; examination verisi Command DB'den okunur.
/// </summary>
[Collection("pet-projection")]
public sealed class ExaminationSharedSearchLookupSmokeIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public ExaminationSharedSearchLookupSmokeIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task FlagOff_Should_FindExamination_ByClientNameSearch_EvenWhenQueryDbEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seed = await SeedExaminationAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seed.ExaminationId);
    }

    [Fact]
    public async Task FlagOn_Should_FindExamination_ByClientNameSearch_WhenReadModelPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seed = await SeedExaminationAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        await SeedPetReadModelAsync(queryDb, seed);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seed.ExaminationId);
    }

    [Fact]
    public async Task FlagOn_Should_NotFallback_WhenReadModelEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seed = await SeedExaminationAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task FlagOn_Should_FindExamination_ByPetNameSearch_WhenReadModelPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seed = await SeedExaminationAsync(commandDb, clientFullName: "Mehmet Demir", petName: "Pamuk");
        await SeedPetReadModelAsync(queryDb, seed);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "pamuk",
            sharedSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
    }

    private static async Task ResetQueryPetsAsync(QueryDbContext queryDb)
    {
        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await queryDb.PetReadModels.ExecuteDeleteAsync();
    }

    private static async Task<ExaminationSearchSeed> SeedExaminationAsync(
        AppDbContext commandDb,
        string clientFullName,
        string petName)
    {
        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..20]);
        var clinic = new Clinic(tenant.Id, "Pilot Clinic", "Istanbul");
        commandDb.Tenants.Add(tenant);
        commandDb.Clinics.Add(clinic);
        await commandDb.SaveChangesAsync();

        var client = new Client(tenant.Id, clientFullName, "905551110088");
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var speciesName = await commandDb.Species.Where(s => s.Id == speciesId).Select(s => s.Name).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, petName, speciesId);
        commandDb.Pets.Add(pet);
        await commandDb.SaveChangesAsync();

        var examination = new Examination(
            tenant.Id,
            clinic.Id,
            pet.Id,
            appointmentId: null,
            DateTime.UtcNow.AddHours(-2),
            "Rutin kontrol",
            "Bulgu yok",
            null,
            null);
        commandDb.Examinations.Add(examination);
        await commandDb.SaveChangesAsync();

        return new ExaminationSearchSeed(
            tenant.Id,
            clinic.Id,
            client.Id,
            pet.Id,
            examination.Id,
            clientFullName,
            petName,
            speciesId,
            speciesName);
    }

    private static async Task SeedPetReadModelAsync(QueryDbContext queryDb, ExaminationSearchSeed seed)
    {
        var now = DateTime.UtcNow;
        queryDb.PetReadModels.Add(new PetReadModel
        {
            PetId = seed.PetId,
            TenantId = seed.TenantId,
            ClientId = seed.ClientId,
            ClientFullName = seed.ClientFullName,
            ClientFullNameNormalized = seed.ClientFullName.Trim().ToLowerInvariant(),
            Name = seed.PetName,
            NameNormalized = seed.PetName.Trim().ToLowerInvariant(),
            SpeciesId = seed.SpeciesId,
            SpeciesName = seed.SpeciesName,
            SpeciesNameNormalized = seed.SpeciesName.Trim().ToLowerInvariant(),
            LastEventId = Guid.NewGuid(),
            LastProjectedAtUtc = now,
            LastEventOccurredAtUtc = now
        });
        await queryDb.SaveChangesAsync();
    }

    private static async Task<Backend.Veteriner.Domain.Shared.Result<PagedResult<Backend.Veteriner.Application.Examinations.Contracts.Dtos.ExaminationListItemDto>>> InvokeHandlerAsync(
        IServiceProvider sp,
        Guid tenantId,
        Guid clinicId,
        string search,
        bool sharedSearchLookupEnabled)
    {
        var handler = new GetExaminationsListQueryHandler(
            new FixedTenantContext(tenantId),
            new FixedClinicContext(clinicId),
            new PassthroughClinicReadScopeResolver(),
            sp.GetRequiredService<IReadRepository<Examination>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IPetReadModelLookupReader>(),
            Options.Create(new QueryReadModelsOptions
            {
                SharedSearchLookupEnabled = sharedSearchLookupEnabled
            }));

        return await handler.Handle(
            new GetExaminationsListQuery(
                new PageRequest { Page = 1, PageSize = 50, Search = search },
                clinicId),
            CancellationToken.None);
    }

    private sealed record ExaminationSearchSeed(
        Guid TenantId,
        Guid ClinicId,
        Guid ClientId,
        Guid PetId,
        Guid ExaminationId,
        string ClientFullName,
        string PetName,
        Guid SpeciesId,
        string SpeciesName);

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private sealed class FixedClinicContext(Guid clinicId) : IClinicContext
    {
        public Guid? ClinicId { get; } = clinicId;
    }

    /// <summary>Smoke test: klinik id doğrudan examination spec'e geçer; auth/assignment katmanı bu testin kapsamı dışında.</summary>
    private sealed class PassthroughClinicReadScopeResolver : IClinicReadScopeResolver
    {
        public Task<Result<ClinicReadScope>> ResolveAsync(
            Guid tenantId,
            Guid? requestClinicId,
            CancellationToken ct)
            => Task.FromResult(Result<ClinicReadScope>.Success(new ClinicReadScope(requestClinicId, null)));
    }
}
