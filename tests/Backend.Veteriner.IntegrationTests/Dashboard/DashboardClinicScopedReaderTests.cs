using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.Dashboard;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.IntegrationTests.Dashboard;

public sealed class DashboardClinicScopedReaderTests
{
    [Fact]
    public async Task ListRecentClients_When_TakeNonPositive_ReturnsEmpty()
    {
        await using var db = await CreateDbContextAsync();
        var reader = new DashboardClinicScopedReader(db);

        var r0 = await reader.ListRecentClientsAtClinicAsync(Guid.NewGuid(), Guid.NewGuid(), 0, CancellationToken.None);
        var rn = await reader.ListRecentClientsAtClinicAsync(Guid.NewGuid(), Guid.NewGuid(), -1, CancellationToken.None);

        r0.Should().BeEmpty();
        rn.Should().BeEmpty();
    }

    [Fact]
    public async Task ListRecentClients_When_NoAppointments_ReturnsEmpty()
    {
        await using var db = await CreateDbContextAsync();
        var tenant = new Tenant("T");
        var clinic = new Clinic(tenant.Id, "C", "Istanbul");
        db.AddRange(tenant, clinic);
        await db.SaveChangesAsync();

        var reader = new DashboardClinicScopedReader(db);
        var rows = await reader.ListRecentClientsAtClinicAsync(tenant.Id, clinic.Id, 5, CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ListRecentClients_OneClient_MultipleAppointmentsSamePet_SingleRow_WithLatestScheduledAt()
    {
        await using var db = await CreateDbContextAsync();
        var tenant = new Tenant("T");
        var clinic = new Clinic(tenant.Id, "C", "Istanbul");
        var client = new Client(tenant.Id, "Ali", "905551112233", "ali@x.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "P", speciesId);
        var tOld = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);
        var tNew = new DateTime(2026, 2, 15, 11, 0, 0, DateTimeKind.Utc);
        var appt1 = new Appointment(tenant.Id, clinic.Id, pet.Id, tOld, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled);
        var appt2 = new Appointment(tenant.Id, clinic.Id, pet.Id, tNew, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled);
        db.AddRange(tenant, clinic, client, pet, appt1, appt2);
        await db.SaveChangesAsync();

        var reader = new DashboardClinicScopedReader(db);
        var rows = await reader.ListRecentClientsAtClinicAsync(tenant.Id, clinic.Id, 10, CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(client.Id);
        rows[0].FullName.Should().Be("Ali");
    }

    [Fact]
    public async Task ListRecentClients_OneClient_TwoPets_UsesMaxLastAtAcrossPets()
    {
        await using var db = await CreateDbContextAsync();
        var tenant = new Tenant("T");
        var clinic = new Clinic(tenant.Id, "C", "Istanbul");
        var client = new Client(tenant.Id, "Veli", "905559998877", "veli@x.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var petA = new Pet(tenant.Id, client.Id, "A", speciesId);
        var petB = new Pet(tenant.Id, client.Id, "B", speciesId);
        var tPetA = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        var tPetB = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);
        var apptA = new Appointment(tenant.Id, clinic.Id, petA.Id, tPetA, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled);
        var apptB = new Appointment(tenant.Id, clinic.Id, petB.Id, tPetB, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled);
        db.AddRange(tenant, clinic, client, petA, petB, apptA, apptB);
        await db.SaveChangesAsync();

        var reader = new DashboardClinicScopedReader(db);
        var rows = await reader.ListRecentClientsAtClinicAsync(tenant.Id, clinic.Id, 10, CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(client.Id);
    }

    [Fact]
    public async Task ListRecentClients_ExcludesOtherClinic()
    {
        await using var db = await CreateDbContextAsync();
        var tenant = new Tenant("T");
        var clinicA = new Clinic(tenant.Id, "A", "Istanbul");
        var clinicB = new Clinic(tenant.Id, "B", "Ankara");
        var client = new Client(tenant.Id, "Ayşe", "905551010101", "ayse@x.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "P", speciesId);
        var tUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var apptOnlyB = new Appointment(tenant.Id, clinicB.Id, pet.Id, tUtc, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled);
        db.AddRange(tenant, clinicA, clinicB, client, pet, apptOnlyB);
        await db.SaveChangesAsync();

        var reader = new DashboardClinicScopedReader(db);
        var atA = await reader.ListRecentClientsAtClinicAsync(tenant.Id, clinicA.Id, 10, CancellationToken.None);
        var atB = await reader.ListRecentClientsAtClinicAsync(tenant.Id, clinicB.Id, 10, CancellationToken.None);

        atA.Should().BeEmpty();
        atB.Should().ContainSingle().Which.Id.Should().Be(client.Id);
    }

    [Fact]
    public async Task ListRecentClients_OrderByLastAtDesc_ThenTake()
    {
        await using var db = await CreateDbContextAsync();
        var tenant = new Tenant("T");
        var clinic = new Clinic(tenant.Id, "C", "Istanbul");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var cOlder = new Client(tenant.Id, "Older", "905551010101", "o@x.com");
        var cNewer = new Client(tenant.Id, "Newer", "905552020202", "n@x.com");
        var pOld = new Pet(tenant.Id, cOlder.Id, "Po", speciesId);
        var pNew = new Pet(tenant.Id, cNewer.Id, "Pn", speciesId);
        var tOld = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var tNew = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        db.AddRange(tenant, clinic, cOlder, cNewer, pOld, pNew);
        db.AddRange(
            new Appointment(tenant.Id, clinic.Id, pOld.Id, tOld, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled),
            new Appointment(tenant.Id, clinic.Id, pNew.Id, tNew, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled));
        await db.SaveChangesAsync();

        var reader = new DashboardClinicScopedReader(db);
        var rows = await reader.ListRecentClientsAtClinicAsync(tenant.Id, clinic.Id, 1, CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(cNewer.Id);
    }

    [Fact]
    public async Task CountClientsAtClinic_MatchesDistinctClients_WithDuplicateAppointments()
    {
        await using var db = await CreateDbContextAsync();
        var tenant = new Tenant("T");
        var clinic = new Clinic(tenant.Id, "C", "Istanbul");
        var client = new Client(tenant.Id, "X", "905553030303", "x@x.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "P", speciesId);
        db.AddRange(tenant, clinic, client, pet);
        db.AddRange(
            new Appointment(tenant.Id, clinic.Id, pet.Id, DateTime.UtcNow.AddDays(1), 30, AppointmentType.Checkup, AppointmentStatus.Scheduled),
            new Appointment(tenant.Id, clinic.Id, pet.Id, DateTime.UtcNow.AddDays(2), 30, AppointmentType.Checkup, AppointmentStatus.Scheduled));
        await db.SaveChangesAsync();

        var reader = new DashboardClinicScopedReader(db);
        var count = await reader.CountClientsAtClinicAsync(tenant.Id, clinic.Id, CancellationToken.None);

        count.Should().Be(1);
    }

    [Fact]
    public async Task ListRecentClients_QueryTenant_Isolates_FromOtherTenant()
    {
        await using var db = await CreateDbContextAsync();
        var t1 = new Tenant("T1");
        var t2 = new Tenant("T2");
        var c1 = new Clinic(t1.Id, "C1", "Istanbul");
        var c2 = new Clinic(t2.Id, "C2", "Istanbul");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var client2 = new Client(t2.Id, "OtherTenant", "905554040404", "o@x.com");
        var pet2 = new Pet(t2.Id, client2.Id, "P2", speciesId);
        db.AddRange(t1, t2, c1, c2, client2, pet2);
        db.Add(new Appointment(t2.Id, c2.Id, pet2.Id, DateTime.UtcNow.AddDays(1), 30, AppointmentType.Checkup, AppointmentStatus.Scheduled));
        await db.SaveChangesAsync();

        var reader = new DashboardClinicScopedReader(db);
        var rows = await reader.ListRecentClientsAtClinicAsync(t1.Id, c1.Id, 10, CancellationToken.None);

        rows.Should().BeEmpty();
    }

    private static async Task<AppDbContext> CreateDbContextAsync()
    {
        var dbName = $"VeterinerDb_DashboardReader_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static async Task<Guid> GetAnySpeciesIdAsync(AppDbContext db)
        => await db.Species
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .Select(x => x.Id)
            .FirstAsync();
}
