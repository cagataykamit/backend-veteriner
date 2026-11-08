using System.IO.Compression;
using Backend.Veteriner.Application.DataExports;
using Backend.Veteriner.Infrastructure.Persistence;

namespace Backend.Veteriner.Infrastructure.DataExports;

/// <summary>
/// Kiracı paketini ZIP olarak yazar: <see cref="DataExportTables.All"/> sırasıyla her tablo bir CSV, sonunda satır sayılarıyla
/// <see cref="DataExportFormat.ReadmeFileName"/>. Satırlar akışla okunur (izlenmez); bellek tablo boyutundan bağımsızdır.
/// Kiracı kapsamındaki <see cref="AppDbContext"/> ile çalışır.
/// </summary>
public sealed class DataExportPackageWriter
{
    private readonly AppDbContext _db;

    public DataExportPackageWriter(AppDbContext db) => _db = db;

    /// <param name="afterEachFile">Her dosyadan sonra çağrılır (işçi kirayı yeniler; kira kaybında istisnayla durdurur).</param>
    public async Task WriteAsync(
        Guid tenantId,
        DateTime generatedAtUtc,
        Stream destination,
        Func<CancellationToken, Task> afterEachFile,
        CancellationToken ct)
    {
        var summaries = new List<DataExportFileSummary>(DataExportTables.All.Count);
        await using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var table in DataExportTables.All)
        {
            var entry = zip.CreateEntry(table.FileName, CompressionLevel.Optimal);
            await using (var writer = new StreamWriter(await entry.OpenAsync(ct), DataExportFormat.Utf8Bom))
            {
                summaries.Add(await table.WriteAsync(_db, tenantId, writer, ct));
            }

            await afterEachFile(ct);
        }

        var readme = zip.CreateEntry(DataExportFormat.ReadmeFileName, CompressionLevel.Optimal);
        await using (var writer = new StreamWriter(await readme.OpenAsync(ct), DataExportFormat.Utf8Bom))
        {
            await writer.WriteAsync(DataExportFormat.Readme(generatedAtUtc, summaries));
        }
    }
}
