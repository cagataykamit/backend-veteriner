using System.Data;
using Backend.Veteriner.Application.Imports;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Backend.Veteriner.Infrastructure.Imports;

/// <summary>
/// Hazırlık satırlarını <see cref="SqlBulkCopy"/> ile yazar. Tablo ve sütun adları, sağlayıcı dönüşümleri (enum → sayı) EF
/// modelinden alınır (eşleme tek yerde: <c>ImportBatchRowConfiguration</c>); bağlantı ve açık transaction
/// <see cref="AppDbContext"/>'inkidir. Bellek için parça parça yazılır.
/// </summary>
public sealed class ImportBatchRowBulkWriter : IImportBatchRowBulkWriter
{
    /// <summary>SQL Server kilit yükseltme eşiğinin (deyim başına ~5.000 kilit) altında: tablo kilidi başka kiracıların satır okumasını bekletmesin.</summary>
    private const int ChunkSize = 2_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;

    public ImportBatchRowBulkWriter(AppDbContext db) => _db = db;

    public async Task InsertAsync(IReadOnlyCollection<ImportBatchRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        var entity = _db.Model.FindEntityType(typeof(ImportBatchRow))
                     ?? throw new InvalidOperationException("ImportBatchRow modelde yok.");
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var columns = entity.GetProperties()
            .Select(p => (Property: p, Column: p.GetColumnName(table)!, Converter: p.GetTypeMapping().Converter))
            .ToList();

        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqlConnection)_db.Database.GetDbConnection();
            var transaction = (SqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
            using var copy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
            {
                DestinationTableName = entity.GetSchema() is { } schema
                    ? $"[{schema}].[{entity.GetTableName()}]"
                    : $"[{entity.GetTableName()}]",
                BulkCopyTimeout = (int)Timeout.TotalSeconds,
                BatchSize = ChunkSize,
            };

            using var data = new DataTable();
            foreach (var (property, column, converter) in columns)
            {
                var type = converter?.ProviderClrType ?? property.ClrType;
                data.Columns.Add(column, Nullable.GetUnderlyingType(type) ?? type);
                copy.ColumnMappings.Add(column, column);
            }

            foreach (var chunk in rows.Chunk(ChunkSize))
            {
                data.Clear();
                foreach (var row in chunk)
                {
                    data.Rows.Add(columns
                        .Select(c => c.Property.PropertyInfo!.GetValue(row) is { } value
                            ? c.Converter?.ConvertToProvider(value) ?? value
                            : DBNull.Value)
                        .ToArray());
                }

                await copy.WriteToServerAsync(data, ct);
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }
}
