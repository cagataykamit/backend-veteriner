using System.Text.Json;
using Backend.Veteriner.Application.Common.Options;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Auth;

/// <summary>
/// <c>PermissionChange:RevokeSessionsOnPermissionChange</c>: Production'da açık (yetkisi değişenin oturumları düşer);
/// kod varsayılanı ve Development şablonu kapalı kalır, böylece yerel geliştirme ve testler etkilenmez.
/// </summary>
public sealed class PermissionChangeOptionsDefaultsTests
{
    [Fact]
    public void Production_Should_Revoke_Sessions_On_Permission_Change()
        => ReadFlag("appsettings.Production.json").Should().BeTrue();

    [Fact]
    public void Development_Template_Should_Not_Revoke_Sessions_On_Permission_Change()
        => ReadFlag("appsettings.Development.json.example").Should().BeFalse();

    [Fact]
    public void Code_Default_Should_Not_Revoke_Sessions_On_Permission_Change()
        => new PermissionChangeOptions().RevokeSessionsOnPermissionChange.Should().BeFalse();

    private static bool ReadFlag(string fileName)
    {
        var path = Path.Combine(ResolveApiProjectDirectory(), fileName);
        File.Exists(path).Should().BeTrue($"appsettings bulunamadı: {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("PermissionChange")
            .GetProperty("RevokeSessionsOnPermissionChange")
            .GetBoolean();
    }

    private static string ResolveApiProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Backend.Veteriner.Api");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Backend.Veteriner.Api dizini bulunamadı.");
    }
}
