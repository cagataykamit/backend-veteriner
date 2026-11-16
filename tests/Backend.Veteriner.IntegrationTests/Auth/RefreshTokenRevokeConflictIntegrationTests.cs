using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

/// <summary>
/// <c>RefreshTokenRevokeConflictInterceptor</c>: aynı satırı iki istek (iki ayrı DbContext) yarıştırır. Çıkış okuduktan sonra
/// yenileme jetonu döndürüp önce kaydederse çıkışın kaydı "zaten kapatılmış" sayılır; tersinde döndürme çakışma alır (401 yolu).
/// </summary>
public sealed class RefreshTokenRevokeConflictIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RefreshTokenRevokeConflictIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Revoke_Saved_After_Concurrent_Rotation_Should_Succeed_As_Already_Revoked()
    {
        var id = await SeedTokenAsync();
        await using var logoutScope = _factory.Services.CreateSystemScope();
        var logoutDb = logoutScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revoking = await logoutDb.RefreshTokens.SingleAsync(t => t.Id == id);
        revoking.Revoke(DateTime.UtcNow, "logout");

        await RotateAsync(id);
        var save = () => logoutDb.SaveChangesAsync();

        await save.Should().NotThrowAsync("çakışan kapatma 'zaten kapatılmış' sayılır");
        (await ReadAsync(id)).RevokeReason.Should().Be("rotated", "kazanan döndürmedir; çıkış onu ezmez");
    }

    [Fact]
    public async Task Rotation_Saved_After_Concurrent_Revoke_Should_Still_Conflict()
    {
        var id = await SeedTokenAsync();
        await using var refreshScope = _factory.Services.CreateSystemScope();
        var refreshDb = refreshScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rotating = await refreshDb.RefreshTokens.SingleAsync(t => t.Id == id);
        rotating.ReplaceWith(new string('b', 86), DateTime.UtcNow);

        await using (var logoutScope = _factory.Services.CreateSystemScope())
        {
            var logoutDb = logoutScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await logoutDb.RefreshTokens.SingleAsync(t => t.Id == id)).Revoke(DateTime.UtcNow, "logout");
            await logoutDb.SaveChangesAsync();
        }

        var save = () => refreshDb.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>("kapatılmış jeton döndürülemez; yenileme 401 alır");
        (await ReadAsync(id)).RevokeReason.Should().Be("logout");
    }

    private async Task<Guid> SeedTokenAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == "admin@example.com").Select(u => u.Id).SingleAsync();
        var token = new RefreshToken(userId, Guid.NewGuid().ToString("N").PadRight(86, 'a'), DateTime.UtcNow.AddDays(1), null, null, DateTime.UtcNow);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token.Id;
    }

    private async Task RotateAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.RefreshTokens.SingleAsync(t => t.Id == id)).ReplaceWith(Guid.NewGuid().ToString("N").PadRight(86, 'c'), DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<RefreshToken> ReadAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == id);
    }
}
