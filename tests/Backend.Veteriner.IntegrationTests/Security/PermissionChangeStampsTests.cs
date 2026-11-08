using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Infrastructure.Caching;
using Backend.Veteriner.Infrastructure.Security;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Security;

/// <summary>SEC-002: izin değişim damgası kuralı (iat &lt; damga → eski token).</summary>
public sealed class PermissionChangeStampsTests
{
    private static readonly DateTime Start = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private PermissionChangeStamps Create()
        => new(_cache, _time, Options.Create(new JwtOptions { Key = new string('k', 40) }));

    private static long Unix(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds();

    [Fact]
    public void Token_Issued_Before_Process_Start_Is_Stale()
    {
        var stamps = Create();

        stamps.IsStale(Guid.NewGuid(), Unix(Start.AddSeconds(-1))).Should().BeTrue();
        stamps.IsStale(Guid.NewGuid(), Unix(Start)).Should().BeFalse();
    }

    [Fact]
    public void Mark_Makes_Earlier_Token_Stale_And_Keeps_Same_Second_Token_Valid()
    {
        var stamps = Create();
        var userId = Guid.NewGuid();
        var issuedBefore = Unix(Start.AddSeconds(5));

        _time.SetUtcNow(Start.AddSeconds(10));
        stamps.Mark(userId);

        stamps.IsStale(userId, issuedBefore).Should().BeTrue();
        // Damgayla aynı saniyede yenilenen token kabul edilir; aksi halde yenileme döngüye girer.
        stamps.IsStale(userId, Unix(Start.AddSeconds(10))).Should().BeFalse();
        stamps.IsStale(userId, Unix(Start.AddSeconds(11))).Should().BeFalse();
    }

    [Fact]
    public void Mark_Does_Not_Affect_Other_Users()
    {
        var stamps = Create();
        var issued = Unix(Start.AddSeconds(5));

        _time.SetUtcNow(Start.AddSeconds(10));
        stamps.Mark(Guid.NewGuid());

        stamps.IsStale(Guid.NewGuid(), issued).Should().BeFalse();
    }

    [Fact]
    public void Default_Access_Token_Lifetime_Is_30_Minutes()
        => new JwtOptions().ExpMinutes.Should().Be(30);
}
