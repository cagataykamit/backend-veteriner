using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Examinations;

/// <summary>
/// Kesinleştirme ve ek not kalıcılığı (gerçek SQL Server/LocalDB): varsayılan Draft, rowversion yarışı,
/// kesinleşmiş içeriğin korunması ve append-only ek not. Dedicated LocalDB test veritabanı.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ExaminationFinalizeIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ExaminationFinalizeIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    private async Task<Guid> SeedExaminationAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (_, _, assignedClinicId, _) = await IntegrationTestAuthHelper.SeedExaminationWriterUserAsync(
            _factory.Services,
            hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(_factory.Services, assignedClinicId);
        return seed.ExaminationId;
    }

    private async Task<Examination> ReadAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Examinations.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    [Fact]
    public async Task NewExamination_Should_BeDraft_With_NoFinalizeInfo()
    {
        var e = await ReadAsync(await SeedExaminationAsync());

        e.Status.Should().Be(ExaminationStatus.Draft);
        e.FinalizedAtUtc.Should().BeNull();
        e.FinalizedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Finalize_Should_Persist_Status_And_Bump_RowVersion_And_Block_Later_Content_Update()
    {
        var id = await SeedExaminationAsync();
        var before = await ReadAsync(id);
        var userId = Guid.NewGuid();
        var at = DateTime.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.Examinations.AsNoTracking().SingleAsync(x => x.Id == id);
            e.Finalize(userId, at).IsSuccess.Should().BeTrue();
            e.SetExpectedRowVersion(before.RowVersion);
            db.Update(e);
            await db.SaveChangesAsync();
        }

        var after = await ReadAsync(id);
        after.Status.Should().Be(ExaminationStatus.Finalized);
        after.FinalizedByUserId.Should().Be(userId);
        after.FinalizedAtUtc.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
        after.RowVersion.Should().NotEqual(before.RowVersion);
        after.VisitReason.Should().Be(before.VisitReason);

        after.UpdateClinicalContent(new ExaminationClinicalUpdate(after.ExaminedAtUtc, "Degistirilmeye calisildi"))
            .Error.Code.Should().Be("Examinations.Finalized");
    }

    [Fact]
    public async Task ConcurrentFinalizeAndUpdate_WithSameRowVersion_Should_AllowOneWinner()
    {
        var id = await SeedExaminationAsync();
        var seed = await ReadAsync(id);

        await using var scope1 = _factory.Services.CreateAsyncScope();
        await using var scope2 = _factory.Services.CreateAsyncScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var finalizing = await db1.Examinations.AsNoTracking().SingleAsync(x => x.Id == id);
        var updating = await db2.Examinations.AsNoTracking().SingleAsync(x => x.Id == id);

        finalizing.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        updating.UpdateClinicalContent(new ExaminationClinicalUpdate(updating.ExaminedAtUtc, "Eszamanli duzenleme"))
            .IsSuccess.Should().BeTrue();
        finalizing.SetExpectedRowVersion(seed.RowVersion);
        updating.SetExpectedRowVersion(seed.RowVersion);

        db2.Update(updating);
        await db2.SaveChangesAsync();

        db1.Update(finalizing);
        var act = () => db1.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        (await ReadAsync(id)).Status.Should().Be(ExaminationStatus.Draft);
    }

    [Fact]
    public async Task Addendum_Should_Persist_AppendOnly_InOrder_Without_Touching_Examination()
    {
        var id = await SeedExaminationAsync();
        var userId = Guid.NewGuid();
        var finalizedAt = DateTime.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.Examinations.SingleAsync(x => x.Id == id);
            e.Finalize(userId, finalizedAt).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var finalized = await ReadAsync(id);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.Examinations.AsNoTracking().SingleAsync(x => x.Id == id);
            db.ExaminationAddenda.Add(e.AddAddendum(userId, "Ikinci gerekce", "Ikinci", finalizedAt.AddMinutes(2)).Value!);
            db.ExaminationAddenda.Add(e.AddAddendum(userId, "Birinci gerekce", "Birinci", finalizedAt.AddMinutes(1)).Value!);
            await db.SaveChangesAsync();
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await verifyDb.ExaminationAddenda.AsNoTracking()
            .Where(a => a.ExaminationId == id)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync();

        rows.Select(a => a.Text).Should().Equal("Birinci", "Ikinci");
        rows.Should().OnlyContain(a => a.TenantId == finalized.TenantId && a.ClinicId == finalized.ClinicId && a.CreatedByUserId == userId);

        var after = await ReadAsync(id);
        after.RowVersion.Should().Equal(finalized.RowVersion);
        after.UpdatedAtUtc.Should().Be(finalized.UpdatedAtUtc);
    }
}
