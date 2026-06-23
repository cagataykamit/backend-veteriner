using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Repositories.Dashboard;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.IntegrationTests.Dashboard;

public sealed class DashboardTodayAppointmentStatusCountsReaderTests : IAsyncLifetime
{
    /// <summary>İzin verilen TEK prefix. Development (VetinityCommandDb) ve diğer test DB'leri guard tarafından reddedilir.</summary>
    private const string CommandDatabasePrefix = "VetinityCommandDb_TodayCounts_";

    private static readonly DateTime DayStart = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayEnd = new(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

    private string _connectionString = string.Empty;
    private AppDbContext _db = null!;

    /// <summary>
    /// Test başına izole, run-specific LocalDB oluşturur ve migrate eder.
    /// DB açılmadan ÖNCE guard çalışır; migrate aşamasında hata olursa DB hemen drop edilir.
    /// </summary>
    public async Task InitializeAsync()
    {
        var commandDbName = $"{CommandDatabasePrefix}{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\mssqllocaldb;Database={commandDbName};Trusted_Connection=True;MultipleActiveResultSets=true";

        // Güvenlik kapısı: yalnız VetinityCommandDb_TodayCounts_ prefix'i kabul edilir.
        // Reddedilen örnekler: development command DB, legacy isimler, paylaşılan integration scope DB'leri, Development-Production suffix'leri, boş ad.
        IntegrationTestDatabaseGuard.EnsureSafeDatabase(connectionString, allowedPrefix: CommandDatabasePrefix);

        _connectionString = connectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var db = new AppDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
        }
        catch
        {
            await db.DisposeAsync();
            await IntegrationTestDatabaseReset.EnsureDroppedAsync(connectionString);
            throw;
        }

        _db = db;
    }

    /// <summary>
    /// Test başarılı, başarısız veya exception ile bitse de çalışır.
    /// Önce DbContext (ve connection) dispose edilir, ardından DB güvenli force-drop ile silinir;
    /// böylece C:\Users\&lt;user&gt; altında kalıcı .mdf/.ldf dosyası kalmaz.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();

        if (!string.IsNullOrEmpty(_connectionString))
            await IntegrationTestDatabaseReset.EnsureDroppedAsync(_connectionString);
    }

    [Fact]
    public async Task GetAsync_When_NoAppointments_ReturnsZeros()
    {
        var db = _db;
        var tenant = new Tenant("T");
        var clinic = new Clinic(tenant.Id, "C", "Istanbul");
        db.AddRange(tenant, clinic);
        await db.SaveChangesAsync();

        var reader = new DashboardTodayAppointmentStatusCountsReader(db);
        var counts = await reader.GetAsync(tenant.Id, clinic.Id, DayStart, DayEnd, null, CancellationToken.None);

        counts.Scheduled.Should().Be(0);
        counts.Completed.Should().Be(0);
        counts.Cancelled.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_Counts_Statuses_ForDayRange_And_Clinic()
    {
        var db = _db;
        var tenant = new Tenant("T");
        var clinic = new Clinic(tenant.Id, "C", "Istanbul");
        var client = new Client(tenant.Id, "Ali", "905551112233", "a@x.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "P", speciesId);
        var inDay = new DateTime(2026, 8, 10, 14, 0, 0, DateTimeKind.Utc);
        var beforeDay = new DateTime(2026, 8, 9, 23, 0, 0, DateTimeKind.Utc);
        db.AddRange(tenant, clinic, client, pet);
        db.AddRange(
            new Appointment(tenant.Id, clinic.Id, pet.Id, inDay, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled),
            new Appointment(tenant.Id, clinic.Id, pet.Id, inDay.AddHours(1), 30, AppointmentType.Checkup, AppointmentStatus.Completed),
            new Appointment(tenant.Id, clinic.Id, pet.Id, inDay.AddHours(2), 30, AppointmentType.Checkup, AppointmentStatus.Cancelled),
            new Appointment(tenant.Id, clinic.Id, pet.Id, beforeDay, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled));
        await db.SaveChangesAsync();

        var reader = new DashboardTodayAppointmentStatusCountsReader(db);
        var counts = await reader.GetAsync(tenant.Id, clinic.Id, DayStart, DayEnd, null, CancellationToken.None);

        counts.Scheduled.Should().Be(1);
        counts.Completed.Should().Be(1);
        counts.Cancelled.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_Excludes_OtherClinic()
    {
        var db = _db;
        var tenant = new Tenant("T");
        var clinicA = new Clinic(tenant.Id, "A", "Istanbul");
        var clinicB = new Clinic(tenant.Id, "B", "Ankara");
        var client = new Client(tenant.Id, "Veli", "905552223344", "v@x.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "P", speciesId);
        var inDay = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
        db.AddRange(tenant, clinicA, clinicB, client, pet);
        db.Add(new Appointment(tenant.Id, clinicB.Id, pet.Id, inDay, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled));
        await db.SaveChangesAsync();

        var reader = new DashboardTodayAppointmentStatusCountsReader(db);
        var atA = await reader.GetAsync(tenant.Id, clinicA.Id, DayStart, DayEnd, null, CancellationToken.None);
        var atB = await reader.GetAsync(tenant.Id, clinicB.Id, DayStart, DayEnd, null, CancellationToken.None);

        atA.Scheduled.Should().Be(0);
        atB.Scheduled.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_TenantIsolation()
    {
        var db = _db;
        var t1 = new Tenant("T1");
        var t2 = new Tenant("T2");
        var c1 = new Clinic(t1.Id, "C1", "Istanbul");
        var c2 = new Clinic(t2.Id, "C2", "Istanbul");
        var client2 = new Client(t2.Id, "X", "905553334455", "x@x.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet2 = new Pet(t2.Id, client2.Id, "P", speciesId);
        var inDay = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        db.AddRange(t1, t2, c1, c2, client2, pet2);
        db.Add(new Appointment(t2.Id, c2.Id, pet2.Id, inDay, 30, AppointmentType.Checkup, AppointmentStatus.Completed));
        await db.SaveChangesAsync();

        var reader = new DashboardTodayAppointmentStatusCountsReader(db);
        var counts = await reader.GetAsync(t1.Id, c1.Id, DayStart, DayEnd, null, CancellationToken.None);

        counts.Completed.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_When_ClinicIdNull_Counts_AllClinics_ForTenant_InRange()
    {
        var db = _db;
        var tenant = new Tenant("T");
        var clinicA = new Clinic(tenant.Id, "A", "Istanbul");
        var clinicB = new Clinic(tenant.Id, "B", "Ankara");
        var client = new Client(tenant.Id, "Y", "905554445566", "y@x.com");
        var speciesId = await GetAnySpeciesIdAsync(db);
        var pet = new Pet(tenant.Id, client.Id, "P", speciesId);
        var inDay = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        db.AddRange(tenant, clinicA, clinicB, client, pet);
        db.AddRange(
            new Appointment(tenant.Id, clinicA.Id, pet.Id, inDay, 30, AppointmentType.Checkup, AppointmentStatus.Scheduled),
            new Appointment(tenant.Id, clinicB.Id, pet.Id, inDay.AddHours(3), 30, AppointmentType.Checkup, AppointmentStatus.Scheduled));
        await db.SaveChangesAsync();

        var reader = new DashboardTodayAppointmentStatusCountsReader(db);
        var counts = await reader.GetAsync(tenant.Id, null, DayStart, DayEnd, null, CancellationToken.None);

        counts.Scheduled.Should().Be(2);
    }

    private static async Task<Guid> GetAnySpeciesIdAsync(AppDbContext db)
        => await db.Species
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .Select(x => x.Id)
            .FirstAsync();
}
