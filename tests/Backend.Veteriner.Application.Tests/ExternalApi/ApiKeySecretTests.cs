using Backend.Veteriner.Application.ExternalApi;
using Backend.Veteriner.Domain.ExternalApi;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.ExternalApi;

/// <summary>TIER-009: anahtar biçimi, yalnız özet saklanması ve doğrulama. Test anahtarları her koşuda üretilir; sabit anahtar yok.</summary>
public sealed class ApiKeySecretTests
{
    [Fact]
    public void Generate_Should_Produce_Parsable_Key_With_Prefix_And_Sha256_Hash()
    {
        var generated = ApiKeySecret.Generate();

        generated.PlainText.Should().HaveLength(ApiKeySecret.KeyLength).And.StartWith(generated.Prefix + "_");
        generated.Prefix.Should().HaveLength(ApiKey.PrefixLength).And.StartWith(ApiKeySecret.Marker);
        generated.Hash.Should().HaveCount(ApiKey.SecretHashLength).And.Equal(ApiKeySecret.Hash(generated.PlainText));
        ApiKeySecret.TryGetPrefix(generated.PlainText, out var prefix).Should().BeTrue();
        prefix.Should().Be(generated.Prefix);
    }

    [Fact]
    public void Generate_Should_Not_Repeat()
    {
        var keys = Enumerable.Range(0, 50).Select(_ => ApiKeySecret.Generate()).ToList();

        keys.Select(k => k.PlainText).Should().OnlyHaveUniqueItems();
        keys.Select(k => k.Prefix).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Verify_Should_Accept_Only_The_Exact_Key()
    {
        var generated = ApiKeySecret.Generate();
        var tampered = generated.PlainText[..^1] + (generated.PlainText[^1] == 'a' ? 'b' : 'a');

        ApiKeySecret.Verify(generated.PlainText, generated.Hash).Should().BeTrue();
        ApiKeySecret.Verify(tampered, generated.Hash).Should().BeFalse();
        ApiKeySecret.Verify(generated.PlainText, null).Should().BeFalse("ön ek bulunmadığında da karşılaştırma yapılır ama kabul edilmez");
        ApiKeySecret.Verify(generated.PlainText, new byte[ApiKey.SecretHashLength]).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("vtk_abcdefgh")]
    public void TryGetPrefix_Should_Reject_Malformed(string? presented)
        => ApiKeySecret.TryGetPrefix(presented, out _).Should().BeFalse();

    [Fact]
    public void TryGetPrefix_Should_Reject_Wrong_Marker_Separator_Or_Characters()
    {
        var key = ApiKeySecret.Generate().PlainText;

        ApiKeySecret.TryGetPrefix("vtx_" + key[4..], out _).Should().BeFalse();
        ApiKeySecret.TryGetPrefix(key[..ApiKey.PrefixLength] + "-" + key[(ApiKey.PrefixLength + 1)..], out _).Should().BeFalse();
        ApiKeySecret.TryGetPrefix(key[..^1] + "%", out _).Should().BeFalse();
        ApiKeySecret.TryGetPrefix(key + "a", out _).Should().BeFalse();
        ApiKeySecret.TryGetPrefix(key.ToUpperInvariant(), out _).Should().BeFalse();
    }
}
