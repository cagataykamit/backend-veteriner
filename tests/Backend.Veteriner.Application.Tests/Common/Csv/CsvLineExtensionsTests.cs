using System.Text;
using Backend.Veteriner.Application.Common.Csv;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common.Csv;

/// <summary>CSV satır sonu işletim sisteminden bağımsız CRLF (Linux CI'da LF yazılması CSV testlerini düşürüyordu).</summary>
public sealed class CsvLineExtensionsTests
{
    [Fact]
    public void AppendCsvLine_Should_Write_Crlf_On_Every_Platform()
        => new StringBuilder().AppendCsvLine("a;b").AppendCsvLine().ToString().Should().Be("a;b\r\n\r\n");
}
