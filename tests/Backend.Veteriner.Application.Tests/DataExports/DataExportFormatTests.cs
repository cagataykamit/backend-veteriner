using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DataExports;
using Backend.Veteriner.Domain.DataExports;
using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.DataExports;

public sealed class DataExportFormatTests
{
    private sealed record Row(Guid Id, string? Name, decimal? Amount, bool Active, DateOnly? Day, DateTime? At, DataExportJobStatus Status);

    private static readonly IReadOnlyList<DataExportColumn<Row>> Columns =
    [
        DataExportColumn<Row>.Id("Id", "Kimlik.", r => r.Id),
        DataExportColumn<Row>.Text("Ad", "Ad.", r => r.Name),
        DataExportColumn<Row>.Number("Tutar", "Tutar.", r => r.Amount),
        DataExportColumn<Row>.Boolean("Etkin", "Etkin.", r => r.Active),
        DataExportColumn<Row>.Date("Gun", "Gün.", r => r.Day),
        DataExportColumn<Row>.Instant("Zaman", "Zaman.", r => r.At),
        DataExportColumn<Row>.Code("Durum", "Durum.", r => r.Status),
    ];

    [Fact]
    public void Header_Should_Expand_Instant_To_Utc_And_Istanbul_Columns()
        => DataExportFormat.HeaderLine(Columns).Should().Be("Id;Ad;Tutar;Etkin;Gun;ZamanUtc;ZamanIstanbul;Durum");

    [Fact]
    public void Row_Should_Use_Decimal_Point_Iso_Dates_And_Code_Names()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        var row = new Row(id, "Pamuk", 1250.50m, true, new DateOnly(2021, 3, 15), new DateTime(2026, 11, 8, 11, 30, 0, DateTimeKind.Utc), DataExportJobStatus.Ready);

        DataExportFormat.RowLine(Columns, row).Should().Be(
            "0f8fad5b-d9cb-469f-a165-70867728950e;Pamuk;1250.50;Evet;2021-03-15;2026-11-08T11:30:00Z;2026-11-08T14:30:00+03:00;Ready");
    }

    [Fact]
    public void Row_Should_Leave_Empty_Cells_For_Missing_Values()
    {
        var row = new Row(Guid.Empty, null, null, false, null, null, DataExportJobStatus.Queued);

        DataExportFormat.RowLine(Columns, row).Should().Be("00000000-0000-0000-0000-000000000000;;;Hayır;;;;Queued");
    }

    [Theory]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("tırnak \"x\"", "\"tırnak \"\"x\"\"\"")]
    [InlineData("satır\r\nsonu", "\"satır\r\nsonu\"")]
    [InlineData("=1+2", "=1+2")]
    public void Quote_Should_Only_Escape_Delimiter_Quote_And_Line_Breaks(string value, string expected)
        => DataExportFormat.Quote(value).Should().Be(expected);

    [Fact]
    public void Flags_Code_Should_Write_Empty_For_No_Flags_And_Comma_List_Otherwise()
    {
        var column = DataExportColumn<PetAlertFlags>.Code("Uyarilar", "Uyarılar.", f => f);

        DataExportFormat.Cells(column.Kind, column.Value(PetAlertFlags.None)).Should().Equal(string.Empty);
        DataExportFormat.Cells(column.Kind, column.Value(PetAlertFlags.Allergy | PetAlertFlags.Aggressive)).Should().Equal("Allergy, Aggressive");
        column.Description.Should().NotContain("None");
    }

    [Fact]
    public void Code_Column_Description_Should_List_Possible_Values()
        => Columns[^1].Description.Should().Be("Durum. Değerler: Queued, Running, Ready, Failed, Expired.");

    [Fact]
    public void Readme_Should_List_Version_Time_Files_Columns_And_Exclusions()
    {
        var generated = new DateTime(2026, 11, 8, 11, 30, 0, DateTimeKind.Utc);
        var readme = DataExportFormat.Readme(generated, [new DataExportFileSummary("ornek.csv", "Örnek", 2, DataExportFormat.Describe(Columns))]);

        readme.Should().Contain($"Biçim sürümü: {DataExportFormat.Version}");
        readme.Should().Contain("Oluşturma zamanı (UTC): 2026-11-08T11:30:00Z");
        readme.Should().Contain("Oluşturma zamanı (İstanbul): 2026-11-08T14:30:00+03:00");
        readme.Should().Contain("- ornek.csv: Örnek (2 kayıt)");
        readme.Should().Contain("  ZamanUtc: Zaman. (UTC)");
        readme.Should().Contain("  ZamanIstanbul: Zaman. (İstanbul saati)");
        readme.Should().Contain("Parola özetleri");
        readme.Should().Contain("\r\n");
    }
}

public sealed class DataExportAccessRulesTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    public void Tenant_Management_Roles_Should_Have_Data_Export(string role)
        => RolePermissionBindings.Map[role].Should().Contain(PermissionCatalog.DataExport.Manage);

    [Theory]
    [InlineData("ClinicAdmin")]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public void Other_Roles_Should_Not_Have_Data_Export(string role)
        => RolePermissionBindings.Map[role].Should().NotContain(PermissionCatalog.DataExport.Manage);

    [Fact]
    public void Permission_Should_Be_In_Catalog()
        => PermissionCatalog.All.Single(p => p.Code == PermissionCatalog.DataExport.Manage).Group.Should().Be("DataExport");

    [Fact]
    public void Request_Should_Bypass_Read_Only_Subscription_Guard_And_Be_Audited()
    {
        var command = new RequestDataExportCommand();

        command.Should().BeAssignableTo<IIgnoreTenantWriteSubscriptionGuard>();
        command.AuditAction.Should().Be("DataExport.Request");
        new DownloadDataExportQuery(Guid.Empty).AuditAction.Should().Be("DataExport.Download");
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(5, 0)]
    public void Remaining_Should_Follow_Window_Limit(int counted, int remaining)
    {
        DataExportJob.MaxRequestsPerWindow.Should().Be(3);
        DataExportRules.Remaining(counted).Should().Be(remaining);
    }
}
