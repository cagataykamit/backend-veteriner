using System.Text.Json;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Projections.Payments;

public sealed class PaymentFinanceRolloutOptionsTests
{
    private static readonly string[] AppSettingsFileNames =
    [
        "appsettings.json",
        "appsettings.Production.json",
        "appsettings.Staging.json",
        "appsettings.Development.json",
        "appsettings.IntegrationTests.json",
        "appsettings.LoadTest.json"
    ];

    public static TheoryData<string> AppSettingsFiles
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in AppSettingsFileNames)
                data.Add(file);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AppSettingsFiles))]
    public void AppSettings_Should_KeepPaymentProjectionDisabled(string fileName)
    {
        using var document = LoadAppSettings(fileName);
        var enabled = document.RootElement
            .GetProperty("PaymentProjection")
            .GetProperty("Enabled")
            .GetBoolean();

        enabled.Should().BeFalse($"{fileName} PaymentProjection:Enabled production-safe default false olmalı");
    }

    [Theory]
    [MemberData(nameof(AppSettingsFiles))]
    public void AppSettings_Should_KeepDashboardFinanceReadDisabled(string fileName)
    {
        using var document = LoadAppSettings(fileName);
        var enabled = document.RootElement
            .GetProperty("QueryReadModels")
            .GetProperty("DashboardFinanceReadEnabled")
            .GetBoolean();

        enabled.Should().BeFalse($"{fileName} QueryReadModels:DashboardFinanceReadEnabled default false olmalı");
    }

    private static JsonDocument LoadAppSettings(string fileName)
    {
        var path = Path.Combine(ResolveApiProjectDirectory(), fileName);
        File.Exists(path).Should().BeTrue($"appsettings bulunamadı: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
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
