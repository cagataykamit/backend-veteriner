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

        e1.UpdateClinicalContent(
            e1.ExaminedAtUtc,
            "Winner visit",
            e1.Findings,
            e1.Assessment,
            e1.Notes,
            e1.Anamnesis,
            e1.Plan,
            e1.WeightKg,
            e1.TemperatureC,
            e1.HeartRateBpm,
            e1.RespiratoryRatePerMin,
            e1.VitalsMeasuredAtUtc).IsSuccess.Should().BeTrue();

        e2.UpdateClinicalContent(
            e2.ExaminedAtUtc,
            "Loser visit",
            e2.Findings,
            e2.Assessment,
            e2.Notes,
            e2.Anamnesis,
            e2.Plan,
            e2.WeightKg,
            e2.TemperatureC,
            e2.HeartRateBpm,
            e2.RespiratoryRatePerMin,
            e2.VitalsMeasuredAtUtc).IsSuccess.Should().BeTrue();

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
