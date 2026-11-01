using Backend.Veteriner.Domain.Catalog;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Catalog;

public sealed class ClinicalSnippetTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_Should_Normalize_Shortcut_And_Keep_Body_Lines()
    {
        var result = ClinicalSnippet.Create(TenantId, " #Kusma ", "  Kusma anamnezi ", "Kaç kez?\r\nİçerik?\r\n", Now);

        result.IsSuccess.Should().BeTrue();
        var snippet = result.Value!;
        snippet.Shortcut.Should().Be("kusma");
        snippet.Title.Should().Be("Kusma anamnezi");
        snippet.NormalizedTitle.Should().Be("kusma anamnezi");
        snippet.Body.Should().Be("Kaç kez?\nİçerik?");
        snippet.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("#İshal", "ishal")]
    [InlineData("ÇÖĞÜŞ-1", "cogus-1")]
    public void NormalizeShortcut_Should_Fold_Turkish_Letters(string input, string expected)
        => ClinicalSnippet.NormalizeShortcut(input).Should().Be(expected);

    [Theory]
    [InlineData("k")]
    [InlineData("iki kelime")]
    [InlineData("nokta.li")]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345")]
    public void Create_Should_Reject_Invalid_Shortcut(string shortcut)
    {
        var result = ClinicalSnippet.Create(TenantId, shortcut, "Başlık", "Metin", Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ClinicalSnippets.Validation");
    }

    [Fact]
    public void Create_Should_Reject_Empty_Body_And_Too_Long_Body()
    {
        ClinicalSnippet.Create(TenantId, "kusma", "Başlık", "   ", Now).IsSuccess.Should().BeFalse();
        ClinicalSnippet.Create(TenantId, "kusma", "Başlık", new string('a', ClinicalSnippet.MaxBodyLength + 1), Now)
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Update_And_Deactivate_Should_Change_State()
    {
        var snippet = ClinicalSnippet.Create(TenantId, "kusma", "Kusma", "Metin", Now).Value!;

        snippet.Update("#ishal", "İshal", "Yeni metin", Now.AddMinutes(1)).IsSuccess.Should().BeTrue();
        snippet.Deactivate(Now.AddMinutes(2));

        snippet.Shortcut.Should().Be("ishal");
        snippet.Body.Should().Be("Yeni metin");
        snippet.IsActive.Should().BeFalse();
        snippet.UpdatedAtUtc.Should().Be(Now.AddMinutes(2));

        snippet.Activate(Now.AddMinutes(3));
        snippet.IsActive.Should().BeTrue();
    }
}
