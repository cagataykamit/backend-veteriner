using Backend.Veteriner.Domain.ExternalApi;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.ExternalApi;

/// <summary>TIER-009: anahtar adı ve kapsam kuralları, yalnız özet ve ön ek saklanması, geri alınmaz iptal, seyrek son kullanım yazımı.</summary>
public sealed class ApiKeyTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly byte[] Hash = new byte[ApiKey.SecretHashLength];
    private const string Prefix = "vtk_abcdefgh";

    private static ApiKey New(params string[] scopes)
        => ApiKey.Create(Guid.NewGuid(), "Muhasebe", scopes, Prefix, Hash, UserId, Now).Value!;

    [Fact]
    public void Create_Should_Store_Canonical_Scopes_And_Trimmed_Name()
    {
        var key = ApiKey.Create(
            Guid.NewGuid(), "  Muhasebe aktarımı  ",
            [ApiKeyScopes.PetsRead, ApiKeyScopes.ClientsRead, ApiKeyScopes.PetsRead], Prefix, Hash, UserId, Now).Value!;

        key.Name.Should().Be("Muhasebe aktarımı");
        key.ScopeList.Should().Equal(ApiKeyScopes.ClientsRead, ApiKeyScopes.PetsRead);
        key.Prefix.Should().Be(Prefix);
        key.IsRevoked.Should().BeFalse();
        key.LastUsedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Create_Should_Require_Name(string? name)
        => ApiKey.Create(Guid.NewGuid(), name, [ApiKeyScopes.ClientsRead], Prefix, Hash, UserId, Now).Error.Code
            .Should().Be(ApiKey.ValidationCode);

    [Fact]
    public void Create_Should_Require_At_Least_One_Known_Read_Scope()
    {
        ApiKey.Create(Guid.NewGuid(), "A", [], Prefix, Hash, UserId, Now).IsSuccess.Should().BeFalse();
        ApiKey.Create(Guid.NewGuid(), "A", ["clients:write"], Prefix, Hash, UserId, Now).Error.Message.Should().Contain("clients:write");
        ApiKey.Create(Guid.NewGuid(), "A", ["*"], Prefix, Hash, UserId, Now).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_Reject_Wrong_Hash_Or_Prefix_Length()
    {
        ApiKey.Create(Guid.NewGuid(), "A", [ApiKeyScopes.ClientsRead], Prefix, new byte[16], UserId, Now).IsSuccess.Should().BeFalse();
        ApiKey.Create(Guid.NewGuid(), "A", [ApiKeyScopes.ClientsRead], "vtk_a", Hash, UserId, Now).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Revoke_Should_Be_Final()
    {
        var key = New(ApiKeyScopes.ClientsRead);

        key.Revoke(UserId, Now.AddHours(1)).IsSuccess.Should().BeTrue();
        key.IsRevoked.Should().BeTrue();
        key.RevokedAtUtc.Should().Be(Now.AddHours(1));
        key.RevokedByUserId.Should().Be(UserId);
        key.Revoke(UserId, Now.AddHours(2)).Error.Code.Should().Be("ApiKeys.AlreadyRevoked");
        key.RevokedAtUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void MarkUsed_Should_Write_At_Most_Once_Per_Precision_Window()
    {
        var key = New(ApiKeyScopes.ClientsRead);

        key.MarkUsed(Now).Should().BeTrue();
        key.MarkUsed(Now.AddSeconds(30)).Should().BeFalse();
        key.LastUsedAtUtc.Should().Be(Now);
        key.MarkUsed(Now + ApiKey.LastUsedPrecision).Should().BeTrue();
        key.LastUsedAtUtc.Should().Be(Now + ApiKey.LastUsedPrecision);
    }
}
