using Backend.Veteriner.Application.Appointments.Queries.GetCalendar;
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
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Appointments;

[Collection("appointment-query-read")]
public sealed class AppointmentListCalendarQueryParityIntegrationTests : IClassFixture<AppointmentQueryReadModelWebApplicationFactory>
{
    private readonly AppointmentQueryReadModelWebApplicationFactory _factory;

    public AppointmentListCalendarQueryParityIntegrationTests(AppointmentQueryReadModelWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task List_Should_MatchCommandPath_AfterRebuild()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedScenarioAsync();

        var when1 = SlotAlignedUtcPlusDays(2);
        var when2 = SlotAlignedUtcPlusDays(4);
        await SeedAppointmentAsync(tenantId, clinicId, petId, when1, AppointmentStatus.Scheduled, "note-a");
        await SeedAppointmentAsync(tenantId, clinicId, petId, when2, AppointmentStatus.Completed);

        await RebuildAsync();

        var page = new PageRequest { Page = 1, PageSize = 20, Sort = "scheduledAtUtc", Order = "desc" };
        var query = new GetAppointmentsListQuery(page, clinicId);

        var commandResult = await InvokeListHandlerAsync(query, appointmentsEnabled: false);
        var queryResult = await InvokeListHandlerAsync(query, appointmentsEnabled: true);

        commandResult.IsSuccess.Should().BeTrue();
        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value!.TotalItems.Should().Be(commandResult.Value!.TotalItems);
        queryResult.Value.Items.Should().BeEquivalentTo(
            commandResult.Value.Items,
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task List_WhenProjectionRowMissing_Should_NotFallbackToCommandDb()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedScenarioAsync();
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await RebuildAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            await queryDb.AppointmentReadModels.ExecuteDeleteAsync();
        }

        var query = new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20 }, clinicId);
        var queryResult = await InvokeListHandlerAsync(query, appointmentsEnabled: true);

        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value!.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Calendar_Should_MatchCommandPath_AfterRebuild()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedScenarioAsync();
        var from = SlotAlignedUtcPlusDays(1);
        var to = SlotAlignedUtcPlusDays(10);
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(3), AppointmentStatus.Scheduled);
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(5), AppointmentStatus.Cancelled);

        await RebuildAsync();

        var query = new GetAppointmentsCalendarQuery(from, to, clinicId);
        var commandResult = await InvokeCalendarHandlerAsync(query, appointmentsEnabled: false);
        var queryResult = await InvokeCalendarHandlerAsync(query, appointmentsEnabled: true);

        commandResult.IsSuccess.Should().BeTrue();
        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value.Should().BeEquivalentTo(commandResult.Value, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task List_WithStatusFilter_Should_MatchCommandPath()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedScenarioAsync();
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(4), AppointmentStatus.Completed);
        await RebuildAsync();

        var query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20 },
            clinicId,
            Status: AppointmentStatus.Completed);

        var commandResult = await InvokeListHandlerAsync(query, false);
        var queryResult = await InvokeListHandlerAsync(query, true);

        queryResult.Value!.TotalItems.Should().Be(commandResult.Value!.TotalItems);
        queryResult.Value.Items.Select(x => x.Id).Should().BeEquivalentTo(commandResult.Value.Items.Select(x => x.Id));
    }

    [Theory]
    [InlineData("ParityPetUnique")]
    [InlineData("Parity Client")]
    [InlineData("905551110099")]
    [InlineData("note-search-xyz")]
    [InlineData("VanKedisiBreed")]
    [InlineData("parity.owner@vetinity.test")]
    public async Task List_Search_Should_MatchCommandPath(string searchTerm)
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, clientId) = await SeedSearchScenarioAsync();
        _ = clientId;

        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled, "note-search-xyz");
        await RebuildAsync();

        var query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20, Search = searchTerm },
            clinicId);

        await AssertListParityAsync(query);
    }

    [Fact]
    public async Task List_SearchNonMatching_Should_ReturnEmptyOnBothPaths()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedSearchScenarioAsync();
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await RebuildAsync();

        var query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20, Search = "zzznomatch999" },
            clinicId);

        var commandResult = await InvokeListHandlerAsync(query, false);
        var queryResult = await InvokeListHandlerAsync(query, true);

        commandResult.Value!.TotalItems.Should().Be(0);
        queryResult.Value!.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task List_SearchWithStatusAndDateFilter_Should_MatchCommandPath()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedSearchScenarioAsync();
        var when = SlotAlignedUtcPlusDays(3);
        await SeedAppointmentAsync(tenantId, clinicId, petId, when, AppointmentStatus.Scheduled, "note-search-xyz");
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(5), AppointmentStatus.Completed, "note-search-xyz");
        await RebuildAsync();

        var query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20, Search = "note-search-xyz" },
            clinicId,
            Status: AppointmentStatus.Scheduled,
            DateFromUtc: when.AddHours(-1),
            DateToUtc: when.AddHours(1));

        await AssertListParityAsync(query);
    }

    [Fact]
    public async Task List_NullEmailAndBreed_Should_NotThrow_AndMatchCommandPath()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedScenarioAsync(nullBreed: true, nullEmail: true);
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await RebuildAsync();

        var query = new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20 }, clinicId);
        await AssertListParityAsync(query);
    }

    [Fact]
    public async Task List_Pagination_Page1AndPage2_Should_MatchCommandPathWithoutDuplicates()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedScenarioAsync();
        for (var i = 0; i < 25; i++)
            await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(2 + i), AppointmentStatus.Scheduled);

        await RebuildAsync();

        var page1Query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 10, Sort = "scheduledAtUtc", Order = "desc" },
            clinicId);
        var page2Query = new GetAppointmentsListQuery(
            new PageRequest { Page = 2, PageSize = 10, Sort = "scheduledAtUtc", Order = "desc" },
            clinicId);

        await AssertListParityAsync(page1Query);
        await AssertListParityAsync(page2Query);

        var commandPage1 = (await InvokeListHandlerAsync(page1Query, false)).Value!;
        var commandPage2 = (await InvokeListHandlerAsync(page2Query, false)).Value!;
        commandPage1.TotalItems.Should().Be(25);
        commandPage1.Items.Select(x => x.Id).Intersect(commandPage2.Items.Select(x => x.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task List_SameScheduledAtUtc_Should_UseAppointmentIdTieBreakParity()
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedScenarioAsync();
        var when = SlotAlignedUtcPlusDays(3);
        await SeedAppointmentAsync(tenantId, clinicId, petId, when, AppointmentStatus.Scheduled);
        await SeedAppointmentAsync(tenantId, clinicId, petId, when, AppointmentStatus.Completed);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = await db.Appointments.Where(a => a.ScheduledAtUtc == when).Select(a => a.Id).ToListAsync();
            await db.Appointments
                .Where(a => ids.Contains(a.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ScheduledAtUtc, when));
        }

        await RebuildAsync();

        var query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20, Sort = "scheduledAtUtc", Order = "asc" },
            clinicId);

        await AssertListParityAsync(query);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task List_SortDirection_Should_MatchCommandPath(string order)
    {
        await ResetAsync();
        var (tenantId, clinicId, petId, _) = await SeedScenarioAsync();
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(2), AppointmentStatus.Scheduled);
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(4), AppointmentStatus.Completed);
        await SeedAppointmentAsync(tenantId, clinicId, petId, SlotAlignedUtcPlusDays(6), AppointmentStatus.Cancelled);
        await RebuildAsync();

        var query = new GetAppointmentsListQuery(
            new PageRequest { Page = 1, PageSize = 20, Sort = "scheduledAtUtc", Order = order },
            clinicId);

        await AssertListParityAsync(query);
    }

    private async Task AssertListParityAsync(GetAppointmentsListQuery query)
    {
        var commandResult = await InvokeListHandlerAsync(query, appointmentsEnabled: false);
        var queryResult = await InvokeListHandlerAsync(query, appointmentsEnabled: true);

        commandResult.IsSuccess.Should().BeTrue();
        queryResult.IsSuccess.Should().BeTrue();
        queryResult.Value!.TotalItems.Should().Be(commandResult.Value!.TotalItems);
        queryResult.Value.Items.Should().BeEquivalentTo(
            commandResult.Value.Items,
            options => options.WithStrictOrdering());
    }

    private async Task ResetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await commandDb.Appointments.ExecuteDeleteAsync();
        await queryDb.AppointmentReadModels.ExecuteDeleteAsync();
    }

    private async Task RebuildAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionRebuildService>();
        await rebuild.RebuildAsync(500, CancellationToken.None);
    }

    private async Task<(Guid TenantId, Guid ClinicId, Guid PetId, Guid ClientId)> SeedSearchScenarioAsync()
        => await SeedScenarioAsync(breed: "VanKedisiBreed", email: "parity.owner@vetinity.test", petName: "ParityPetUnique");

    private async Task<(Guid TenantId, Guid ClinicId, Guid PetId, Guid ClientId)> SeedScenarioAsync(
        string? breed = null,
        string? email = null,
        bool nullBreed = false,
        bool nullEmail = false,
        string petName = "ParityPet")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();

        var client = await db.Clients.FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.FullName == "Parity Client");
        if (client is null)
        {
            client = nullEmail
                ? new Client(tenant.Id, "Parity Client", "905551110099")
                : new Client(tenant.Id, "Parity Client", "905551110099", email ?? "parity.owner@vetinity.test");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        }
        else if (!nullEmail && email is not null)
        {
            client.UpdateDetails(client.FullName, email, client.Phone, client.Address);
            await db.SaveChangesAsync();
        }

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.TenantId == tenant.Id && p.ClientId == client.Id && p.Name == petName);
        if (pet is null)
        {
            pet = new Pet(
                tenant.Id,
                client.Id,
                petName,
                speciesId,
                nullBreed ? null : breed ?? "VanKedisiBreed");
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
        }
        else if (!nullBreed && breed is not null)
        {
            pet.UpdateDetails(petName, speciesId, breed, pet.BirthDate, pet.BreedId, pet.Gender, pet.ColorId, pet.Weight, pet.Notes);
            await db.SaveChangesAsync();
        }

        return (tenant.Id, clinic.Id, pet.Id, client.Id);
    }

    private async Task SeedAppointmentAsync(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        DateTime when,
        AppointmentStatus status,
        string? notes = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Appointments.Add(new Appointment(
            tenantId,
            clinicId,
            petId,
            when,
            30,
            AppointmentType.Consultation,
            status,
            notes));
        await db.SaveChangesAsync();
    }

    private async Task<Backend.Veteriner.Domain.Shared.Result<PagedResult<Backend.Veteriner.Application.Appointments.Contracts.Dtos.AppointmentListItemDto>>> InvokeListHandlerAsync(
        GetAppointmentsListQuery query,
        bool appointmentsEnabled)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var (tenantId, clinicId) = await ResolveDefaultScopeAsync(sp);
        var tenantContext = new FixedTenantContext(tenantId);
        var clinicContext = new FixedClinicContext(clinicId);

        var handler = new GetAppointmentsListQueryHandler(
            tenantContext,
            clinicContext,
            sp.GetRequiredService<IReadRepository<Appointment>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IReadRepository<Clinic>>(),
            sp.GetRequiredService<IAppointmentReadModelReader>(),
            sp.GetRequiredService<IPetReadModelLookupReader>(),
            Options.Create(new QueryReadModelsOptions { AppointmentsEnabled = appointmentsEnabled }));

        return await handler.Handle(query, CancellationToken.None);
    }

    private async Task<Backend.Veteriner.Domain.Shared.Result<IReadOnlyList<Backend.Veteriner.Application.Appointments.Contracts.Dtos.AppointmentCalendarItemDto>>> InvokeCalendarHandlerAsync(
        GetAppointmentsCalendarQuery query,
        bool appointmentsEnabled)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var (tenantId, clinicId) = await ResolveDefaultScopeAsync(sp);
        var tenantContext = new FixedTenantContext(tenantId);
        var clinicContext = new FixedClinicContext(clinicId);

        var handler = new GetAppointmentsCalendarQueryHandler(
            tenantContext,
            clinicContext,
            sp.GetRequiredService<IReadRepository<Appointment>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IAppointmentReadModelReader>(),
            Options.Create(new QueryReadModelsOptions { AppointmentsEnabled = appointmentsEnabled }));

        return await handler.Handle(query, CancellationToken.None);
    }

    private static async Task<(Guid TenantId, Guid ClinicId)> ResolveDefaultScopeAsync(IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = await db.Tenants.Where(t => t.Name == DataSeeder.DefaultTenantName).Select(t => t.Id).SingleAsync();
        var clinicId = await db.Clinics
            .Where(c => c.TenantId == tenantId && c.Name == DataSeeder.DefaultSeedClinicName)
            .Select(c => c.Id)
            .SingleAsync();
        return (tenantId, clinicId);
    }

    private static DateTime SlotAlignedUtcPlusDays(int days)
    {
        var date = DateTime.UtcNow.Date.AddDays(days);
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            date = date.AddDays(1);
        return date.AddHours(9);
    }
}

[CollectionDefinition("appointment-query-read", DisableParallelization = true)]
public sealed class AppointmentQueryReadCollection : ICollectionFixture<AppointmentQueryReadModelWebApplicationFactory>;

file sealed class FixedTenantContext(Guid tenantId) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
}

file sealed class FixedClinicContext(Guid clinicId) : IClinicContext
{
    public Guid? ClinicId { get; } = clinicId;
}
