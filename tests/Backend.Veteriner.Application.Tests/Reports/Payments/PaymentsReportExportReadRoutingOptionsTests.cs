using System.Text.Json;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>
/// CQRS-15J: <c>QueryReadModels:PaymentsReportExportReadEnabled</c> tüm ortamlarda production-safe
/// varsayılan (explicit false) ile gelmelidir.
/// </summary>
public sealed class PaymentsReportExportReadRoutingOptionsTests
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
    public void AppSettings_Should_KeepPaymentsReportExportReadDisabled(string fileName)
    {
        using var document = LoadAppSettings(fileName);
        var enabled = document.RootElement
            .GetProperty("QueryReadModels")
            .GetProperty("PaymentsReportExportReadEnabled")
            .GetBoolean();

        enabled.Should().BeFalse($"{fileName} QueryReadModels:PaymentsReportExportReadEnabled default false olmalı");
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
