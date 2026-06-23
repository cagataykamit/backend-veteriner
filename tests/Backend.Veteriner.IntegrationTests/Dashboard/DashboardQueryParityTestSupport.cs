using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Queries.GetSummary;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Dashboard;

internal static class DashboardQueryParityTestSupport
{
    internal sealed record TenantWideScenario(
        Guid TenantId,
        Guid ClinicAId,
        Guid ClinicBId,
        Guid PetAId,
        Guid PetBId,
        Guid ClientAId,
        Guid ClientBId);

    internal static DateTime TodayWithinOperationalWindow()
    {
        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(DateTime.UtcNow);
        var when = dayStart.AddHours(2);
        return when < dayEnd ? when : dayStart.AddMinutes(30);
    }

    internal static DateTime HoursFromUtcNow(double hours) => DateTime.UtcNow.AddHours(hours);

    internal static DateTime DayOffsetFromUtcNow(int dayOffset, double hoursFromDayStart = 10)
    {
        var anchor = DateTime.UtcNow.AddDays(dayOffset);
        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(anchor);
        var when = dayStart.AddHours(hoursFromDayStart);
        return when < dayEnd ? when : dayStart.AddMinutes(30);
    }

    internal static async Task ResetCommandAppointmentsAsync(IServiceProvider rootServices)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await commandDb.Appointments.ExecuteDeleteAsync();
    }

    internal static async Task ResetAppointmentProjectionAsync(IServiceProvider rootServices)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await commandDb.Appointments.ExecuteDeleteAsync();
        await queryDb.AppointmentReadModels.ExecuteDeleteAsync();
        await queryDb.ClinicDailyAppointmentStatsReadModels.ExecuteDeleteAsync();
        await queryDb.ClinicPetActivityReadModels.ExecuteDeleteAsync();
        await queryDb.ClinicClientActivityReadModels.ExecuteDeleteAsync();
    }

    internal static async Task RebuildAsync(IServiceProvider rootServices)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionRebuildService>();
        await rebuild.RebuildAsync(500, CancellationToken.None);
    }

    internal static async Task<(DashboardSummaryDto Command, DashboardSummaryDto Query)> CompareSummaryPathsAsync(
        IServiceProvider rootServices,
        Guid tenantId,
        Guid? clinicId)
    {
        var commandResult = await InvokeSummaryHandlerAsync(rootServices, dashboardEnabled: false, tenantId, clinicId);
        var queryResult = await InvokeSummaryHandlerAsync(rootServices, dashboardEnabled: true, tenantId, clinicId);

        commandResult.IsSuccess.Should().BeTrue();
        queryResult.IsSuccess.Should().BeTrue();

        return (commandResult.Value!, queryResult.Value!);
    }

    internal static async Task<Backend.Veteriner.Domain.Shared.Result<DashboardSummaryDto>> InvokeSummaryHandlerAsync(
        IServiceProvider rootServices,
        bool dashboardEnabled,
        Guid tenantId,
        Guid? clinicId)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var handler = new GetDashboardSummaryQueryHandler(
            new FixedTenantContext(tenantId),
            clinicId is { } id ? new FixedClinicContext(id) : new NullClinicContext(),
            new DashboardParityClinicReadScopeResolver(),
            sp.GetRequiredService<IReadRepository<Appointment>>(),
            sp.GetRequiredService<IDashboardTodayAppointmentStatusCountsReader>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IDashboardClinicScopedReader>(),
            sp.GetRequiredService<IDashboardAppointmentReadModelReader>(),
            Options.Create(new QueryReadModelsOptions
            {
                AppointmentsEnabled = false,
                DashboardAppointmentsEnabled = dashboardEnabled
            }));

        return await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);
    }

    internal static async Task SeedAppointmentAsync(
        IServiceProvider rootServices,
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        DateTime when,
        AppointmentStatus status)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Appointments.Add(new Appointment(
            tenantId,
            clinicId,
            petId,
            when,
            30,
            AppointmentType.Consultation,
            status));
        await db.SaveChangesAsync();
    }

    internal static async Task<(Guid TenantId, Guid ClinicId, Guid PetId, Guid ClientId)> SeedSingleClinicScenarioAsync(
        IServiceProvider rootServices)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();

        var client = await db.Clients.FirstOrDefaultAsync(c =>
            c.TenantId == tenant.Id && c.FullName == "Dashboard Parity Client");
        if (client is null)
        {
            client = new Client(tenant.Id, "Dashboard Parity Client", "905551110088", "dash.parity@test.local");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        }

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.TenantId == tenant.Id && p.ClientId == client.Id);
        if (pet is null)
        {
            pet = new Pet(tenant.Id, client.Id, "DashParityPet", speciesId, "GoldenMix");
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
        }

        return (tenant.Id, clinic.Id, pet.Id, client.Id);
    }

    internal static async Task<TenantWideScenario> SeedTwoClinicScenarioAsync(IServiceProvider rootServices)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();

        var clinicA = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var clinicB = await db.Clinics.FirstOrDefaultAsync(c =>
            c.TenantId == tenant.Id && c.Name == "Dashboard Parity Clinic B");
        if (clinicB is null)
        {
            clinicB = new Clinic(tenant.Id, "Dashboard Parity Clinic B", "Ankara");
            db.Clinics.Add(clinicB);
            await db.SaveChangesAsync();
        }

        var clientA = await EnsureClientAsync(db, tenant.Id, "Dashboard Tenant Client A", "905551110101", "tenant.a@test.local");
        var clientB = await EnsureClientAsync(db, tenant.Id, "Dashboard Tenant Client B", "905551110102", "tenant.b@test.local");

        var petA = await EnsurePetAsync(db, tenant.Id, clientA.Id, "DashTenantPetA", speciesId);
        var petB = await EnsurePetAsync(db, tenant.Id, clientB.Id, "DashTenantPetB", speciesId);

        return new TenantWideScenario(
            tenant.Id,
            clinicA.Id,
            clinicB.Id,
            petA.Id,
            petB.Id,
            clientA.Id,
            clientB.Id);
    }

    internal static void AssertAppointmentDerivedParity(DashboardSummaryDto command, DashboardSummaryDto query)
    {
        query.TodayAppointmentsCount.Should().Be(command.TodayAppointmentsCount);
        query.CompletedTodayCount.Should().Be(command.CompletedTodayCount);
        query.CancelledTodayCount.Should().Be(command.CancelledTodayCount);
        query.UpcomingAppointmentsCount.Should().Be(command.UpcomingAppointmentsCount);
        query.UpcomingAppointments.Should().BeEquivalentTo(command.UpcomingAppointments, options => options.WithStrictOrdering());
        query.Last7DaysAppointments.Should().BeEquivalentTo(command.Last7DaysAppointments, options => options.WithStrictOrdering());
    }

    private static async Task<Client> EnsureClientAsync(
        AppDbContext db,
        Guid tenantId,
        string name,
        string phone,
        string email)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.FullName == name);
        if (client is not null)
            return client;

        client = new Client(tenantId, name, phone, email);
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    private static async Task<Pet> EnsurePetAsync(
        AppDbContext db,
        Guid tenantId,
        Guid clientId,
        string name,
        Guid speciesId)
    {
        var pet = await db.Pets.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Name == name);
        if (pet is not null)
            return pet;

        pet = new Pet(tenantId, clientId, name, speciesId, "Mixed");
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return pet;
    }
}

internal sealed class FixedTenantContext(Guid tenantId) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
}

internal sealed class FixedClinicContext(Guid clinicId) : IClinicContext
{
    public Guid? ClinicId { get; } = clinicId;
}

internal sealed class NullClinicContext : IClinicContext
{
    public Guid? ClinicId { get; } = null;
}

/// <summary>Parity testleri: aktif klinik varsa tek klinik; yoksa tenant-wide (Admin/Owner simülasyonu).</summary>
internal sealed class DashboardParityClinicReadScopeResolver : IClinicReadScopeResolver
{
    public Task<Result<ClinicReadScope>> ResolveAsync(Guid tenantId, Guid? requestClinicId, CancellationToken ct)
    {
        if (requestClinicId is { } id)
            return Task.FromResult(Result<ClinicReadScope>.Success(new ClinicReadScope(id, null)));

        return Task.FromResult(Result<ClinicReadScope>.Success(new ClinicReadScope(null, null)));
    }
}
