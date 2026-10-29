using System.Globalization;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Registers;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Backend.Veteriner.Application.Reports.Pdf;

/// <summary>PDF başlık bilgisi: rapor adı, dönem metni (yoksa null) ve oluşturma anı (UTC; dosyada İstanbul saati).</summary>
internal sealed record ReportPdfHeader(string Title, string? Period, DateTime GeneratedAtUtc)
{
    public static string PeriodText(DateOnly from, DateOnly to)
        => from == to ? Day(from) : $"{Day(from)} – {Day(to)}";

    /// <summary>UTC aralığın İstanbul takvim günleri (klasik raporlar: <c>[fromUtc, toUtc]</c>).</summary>
    public static string PeriodText(DateTime fromUtc, DateTime toUtc)
        => PeriodText(OperationDayBounds.ToLocalDate(fromUtc), OperationDayBounds.ToLocalDate(toUtc));

    private static string Day(DateOnly day) => day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// Rapor PDF'i (REPORT-007): A4 yatay, başlık + dönem, her sayfada tekrarlanan sütun başlığı, altbilgide oluşturma zamanı ve
/// sayfa numarası. Sütunlar ve değer türleri CSV/XLSX ile aynı <see cref="RegisterColumn{TRow}"/> listesinden gelir; gruplu
/// sütunlarda başlık <c>Grup · Başlık</c>. Satır tavanı çağıranda (<see cref="Tenants.ExportRowLimit.ForFormat"/>).
/// </summary>
internal static class ReportPdfWriter
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Unit PageMargin = Unit.FromCentimeter(1.2);

    /// <summary>A4 yatay sayfa genişliği (29,7 cm); tablo kenar boşlukları dışındaki alana yayılır.</summary>
    private static readonly Unit LandscapePageWidth = Unit.FromCentimeter(29.7);
    private static readonly Color HeaderShading = new(0xEE, 0xEE, 0xEE);
    private static readonly Color MutedText = new(0x55, 0x55, 0x55);

    /// <summary>Sütun genişliği oranı için örneklenen satır sayısı ve karakter sınırları.</summary>
    private const int WidthSampleRows = 200;
    private const int MinColumnWeight = 4;
    private const int MaxColumnWeight = 40;

    public static byte[] Write<TRow>(ReportPdfHeader header, IReadOnlyList<RegisterColumn<TRow>> columns, IReadOnlyList<TRow> rows)
    {
        ReportPdfFonts.EnsureConfigured();

        var document = new Document();
        document.Info.Title = header.Title;
        document.Info.Author = "Vetinity";

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = ReportPdfFonts.FamilyName;
        normal.Font.Size = 8;

        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.Orientation = Orientation.Landscape;
        setup.TopMargin = setup.BottomMargin = setup.LeftMargin = setup.RightMargin = PageMargin;
        setup.FooterDistance = Unit.FromCentimeter(0.6);

        var title = section.AddParagraph(header.Title);
        title.Format.Font.Size = 13;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(2);

        var meta = section.AddParagraph(MetaLine(header, rows.Count));
        meta.Format.Font.Size = 8;
        meta.Format.Font.Color = MutedText;
        meta.Format.SpaceAfter = Unit.FromPoint(8);

        AddFooter(section, header.GeneratedAtUtc);
        AddTable(section, columns, rows);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static string MetaLine(ReportPdfHeader header, int rowCount)
        => header.Period is null ? $"{rowCount} kayıt" : $"Dönem: {header.Period} · {rowCount} kayıt";

    private static void AddFooter(Section section, DateTime generatedAtUtc)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = MutedText;
        footer.AddText($"Vetinity · Oluşturma: {OperationDayBounds.ToLocal(generatedAtUtc).ToString("dd.MM.yyyy HH:mm", Turkish)} · Sayfa ");
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();
    }

    private static void AddTable<TRow>(Section section, IReadOnlyList<RegisterColumn<TRow>> columns, IReadOnlyList<TRow> rows)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.25;
        table.Borders.Color = Colors.Gray;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(2);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(1.5);

        var usable = Unit.FromPoint(LandscapePageWidth.Point - (2 * PageMargin.Point));
        var weights = ColumnWeights(columns, rows);
        var totalWeight = weights.Sum();
        var numeric = new bool[columns.Count];

        for (var c = 0; c < columns.Count; c++)
        {
            table.AddColumn(Unit.FromPoint(usable.Point * weights[c] / totalWeight));
            numeric[c] = rows.Take(WidthSampleRows).Any(r => columns[c].Value(r) is MoneyAmount or decimal);
        }

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = HeaderShading;
        head.Format.Font.Bold = true;
        for (var c = 0; c < columns.Count; c++)
            head.Cells[c].AddParagraph(columns[c].CsvHeader);

        foreach (var row in rows)
        {
            var line = table.AddRow();
            for (var c = 0; c < columns.Count; c++)
            {
                var paragraph = line.Cells[c].AddParagraph(Format(columns[c], row));
                if (numeric[c])
                    paragraph.Format.Alignment = ParagraphAlignment.Right;
            }
        }
    }

    /// <summary>Genişlik oranı: başlık ve örnek satırlardaki en uzun metin, alt/üst sınırlı (uzun metin satır kırar).</summary>
    private static int[] ColumnWeights<TRow>(IReadOnlyList<RegisterColumn<TRow>> columns, IReadOnlyList<TRow> rows)
    {
        var sample = rows.Take(WidthSampleRows).ToList();
        return columns
            .Select(c => Math.Clamp(
                sample.Select(r => Format(c, r).Length).DefaultIfEmpty(0).Max() is var longest && longest > c.CsvHeader.Length
                    ? longest
                    : c.CsvHeader.Length,
                MinColumnWeight,
                MaxColumnWeight))
            .ToArray();
    }

    private static string Format<TRow>(RegisterColumn<TRow> column, TRow row)
        => column.Value(row) switch
        {
            null => string.Empty,
            DateTime utc => OperationDayBounds.ToLocalDate(utc).ToString("dd.MM.yyyy", Turkish),
            DateOnly day => day.ToString("dd.MM.yyyy", Turkish),
            LocalDateTime time => OperationDayBounds.ToLocal(time.Utc).ToString("dd.MM.yyyy HH:mm", Turkish),
            MoneyAmount money => money.Value.ToString("#,##0.00", Turkish),
            decimal number => number.ToString("#,##0.###", Turkish),
            var other => other.ToString() ?? string.Empty,
        };
}
