using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Examinations;

/// <summary>
/// SQL Server rowversion + EF concurrency token: iki ayrı <see cref="AppDbContext"/> ile aynı istemci sürümünden yazma.
/// Dedicated LocalDB integration test veritabanı (<see cref="IntegrationTestDatabaseGuard"/>).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ExaminationRowVersionConcurrencyIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ExaminationRowVersionConcurrencyIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task PartialUpdate_Should_PersistOnlySuppliedFields_And_ClearVitals_Explicitly()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (_, _, assignedClinicId, _) = await IntegrationTestAuthHelper.SeedExaminationWriterUserAsync(
            _factory.Services,
            hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(_factory.Services, assignedClinicId);

        async Task<Examination> UpdateAsync(ExaminationClinicalUpdate update)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Examinations.SingleAsync(x => x.Id == seed.ExaminationId);
            entity.UpdateClinicalContent(update).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
            return await Read();
        }

        async Task<Examination> Read()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Examinations.AsNoTracking().SingleAsync(x => x.Id == seed.ExaminationId);
        }

        var examinedAt = DateTime.UtcNow.AddHours(-2);

        var filled = await UpdateAsync(new ExaminationClinicalUpdate(
            examinedAt, "Kontrol", "Bulgu", "Degerlendirme", "Not", "Hikaye", "Plan",
            WeightKg: 4.25m, TemperatureC: 38.6m, HeartRateBpm: 120, RespiratoryRatePerMin: 30));
        filled.Plan.Should().Be("Plan");
        filled.WeightKg.Should().Be(4.25m);

        var partial = await UpdateAsync(new ExaminationClinicalUpdate(examinedAt, "Kontrol", Plan: "Yeni plan"));
        partial.Plan.Should().Be("Yeni plan");
        partial.Findings.Should().Be("Bulgu");
        partial.Assessment.Should().Be("Degerlendirme");
        partial.Notes.Should().Be("Not");
        partial.Anamnesis.Should().Be("Hikaye");
        partial.WeightKg.Should().Be(4.25m);
        partial.TemperatureC.Should().Be(38.6m);
        partial.HeartRateBpm.Should().Be(120);
        partial.RespiratoryRatePerMin.Should().Be(30);
        partial.VitalsMeasuredAtUtc.Should().NotBeNull();

        var cleared = await UpdateAsync(new ExaminationClinicalUpdate(
            examinedAt, "Kontrol", Notes: "", Findings: "", ClearVitals: true));
        cleared.Notes.Should().BeNull();
        cleared.Findings.Should().BeEmpty();
        cleared.Plan.Should().Be("Yeni plan");
        cleared.WeightKg.Should().BeNull();
        cleared.TemperatureC.Should().BeNull();
        cleared.HeartRateBpm.Should().BeNull();
        cleared.RespiratoryRatePerMin.Should().BeNull();
        cleared.VitalsMeasuredAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentUpdate_WithSameStaleRowVersion_Should_AllowOneWinner_And_RejectOther()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (_, _, assignedClinicId, _) = await IntegrationTestAuthHelper.SeedExaminationWriterUserAsync(
            _factory.Services,
            hasher);

        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(_factory.Services, assignedClinicId);
        var examinationId = seed.ExaminationId;

        byte[] staleRowVersion;
        await using (var readScope = _factory.Services.CreateAsyncScope())
        {
            var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
            staleRowVersion = await readDb.Examinations.AsNoTracking()
                .Where(e => e.Id == examinationId)
                .Select(e => e.RowVersion)
                .SingleAsync();
        }

        staleRowVersion.Should().NotBeNullOrEmpty();

        await using var scope1 = _factory.Services.CreateAsyncScope();
        await using var scope2 = _factory.Services.CreateAsyncScope();

        var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var e1 = await db1.Examinations.AsNoTracking().SingleAsync(x => x.Id == examinationId);
        var e2 = await db2.Examinations.AsNoTracking().SingleAsync(x => x.Id == examinationId);

        e1.UpdateClinicalContent(new ExaminationClinicalUpdate(
            e1.ExaminedAtUtc,
            "Winner visit")).IsSuccess.Should().BeTrue();

        e2.UpdateClinicalContent(new ExaminationClinicalUpdate(
            e2.ExaminedAtUtc,
            "Loser visit")).IsSuccess.Should().BeTrue();

        e1.SetExpectedRowVersion(staleRowVersion);
        e2.SetExpectedRowVersion(staleRowVersion);

        db1.Update(e1);
        var entry1 = db1.Entry(e1);
        entry1.Property(x => x.RowVersion).OriginalValue.Should().BeEquivalentTo(staleRowVersion);

        await db1.SaveChangesAsync();

        db2.Update(e2);
        var act = () => db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var final = await verifyDb.Examinations.AsNoTracking().SingleAsync(x => x.Id == examinationId);

        final.VisitReason.Should().Be("Winner visit");
        final.RowVersion.Should().NotEqual(staleRowVersion);
    }
}
