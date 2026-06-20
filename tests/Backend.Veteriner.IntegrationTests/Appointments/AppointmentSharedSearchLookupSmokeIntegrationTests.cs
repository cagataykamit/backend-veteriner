using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
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

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// CQRS-12D-5 smoke: appointment list shared search lookup on Command DB path (AppointmentsEnabled=false).
/// </summary>
[Collection("pet-projection")]
public sealed class AppointmentSharedSearchLookupSmokeIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public AppointmentSharedSearchLookupSmokeIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task FlagOff_Should_FindAppointment_ByClientNameSearch_EvenWhenQueryDbEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seed = await SeedAppointmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seed.AppointmentId);
    }

    [Fact]
    public async Task FlagOn_Should_NotFallback_WhenReadModelEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        await SeedAppointmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        var seed = await SeedAppointmentAsync(commandDb, clientFullName: "Mehmet Demir", petName: "Boncuk");

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
    public async Task FlagOn_Should_FindAppointment_ByClientNameSearch_WhenReadModelPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seed = await SeedAppointmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        await SeedPetReadModelAsync(queryDb, seed);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seed.AppointmentId);
    }

    [Fact]
    public async Task FlagOn_Should_ReturnOnlyRowsForRequestedTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQueryPetsAsync(queryDb);
        var seedA = await SeedAppointmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        var seedB = await SeedAppointmentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Other");
        await SeedPetReadModelAsync(queryDb, seedA);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seedA.TenantId,
            seedA.ClinicId,
            search: "yılmaz",
            sharedSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seedA.AppointmentId);
        result.Value.Items.Should().NotContain(x => x.Id == seedB.AppointmentId);
    }

    private static async Task ResetQueryPetsAsync(QueryDbContext queryDb)
    {
        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await queryDb.PetReadModels.ExecuteDeleteAsync();
    }

    private static async Task<AppointmentSearchSeed> SeedAppointmentAsync(
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

        var appointment = new Appointment(
            tenant.Id,
            clinic.Id,
            pet.Id,
            DateTime.UtcNow.AddDays(2),
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled,
            "Kontrol");

        commandDb.Appointments.Add(appointment);
        await commandDb.SaveChangesAsync();

        return new AppointmentSearchSeed(
            tenant.Id,
            clinic.Id,
            client.Id,
            pet.Id,
            appointment.Id,
            clientFullName,
            petName,
            speciesId,
            speciesName);
    }

    private static async Task SeedPetReadModelAsync(QueryDbContext queryDb, AppointmentSearchSeed seed)
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

    private static async Task<Result<PagedResult<Backend.Veteriner.Application.Appointments.Contracts.Dtos.AppointmentListItemDto>>> InvokeHandlerAsync(
        IServiceProvider sp,
        Guid tenantId,
        Guid clinicId,
        string search,
        bool sharedSearchLookupEnabled)
    {
        var handler = new GetAppointmentsListQueryHandler(
            new FixedTenantContext(tenantId),
            new FixedClinicContext(clinicId),
            sp.GetRequiredService<IReadRepository<Appointment>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IReadRepository<Clinic>>(),
            sp.GetRequiredService<IAppointmentReadModelReader>(),
            sp.GetRequiredService<IPetReadModelLookupReader>(),
            Options.Create(new QueryReadModelsOptions
            {
                AppointmentsEnabled = false,
                SharedSearchLookupEnabled = sharedSearchLookupEnabled
            }));

        return await handler.Handle(
            new GetAppointmentsListQuery(
                new PageRequest { Page = 1, PageSize = 50, Search = search },
                clinicId),
            CancellationToken.None);
    }

    private sealed record AppointmentSearchSeed(
        Guid TenantId,
        Guid ClinicId,
        Guid ClientId,
        Guid PetId,
        Guid AppointmentId,
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
}
