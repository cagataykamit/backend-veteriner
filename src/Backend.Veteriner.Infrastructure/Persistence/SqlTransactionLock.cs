using System.Data;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// <see cref="ITransactionLock"/>: SQL Server <c>sp_getapplock</c> (Exclusive, sahibi transaction). Kilit commit ya da geri almayla
/// kendiliğinden bırakılır. READ_COMMITTED_SNAPSHOT'ta da geçerlidir: satır sürümü okuması beklemez, uygulama kilidi bekletir.
/// </summary>
public sealed class SqlTransactionLock : ITransactionLock
{
    /// <summary>Bekleme tavanı; komut zaman aşımından önce anlaşılır bir hata verir.</summary>
    private const int TimeoutMilliseconds = 15_000;

    private readonly AppDbContext _db;

    public SqlTransactionLock(AppDbContext db) => _db = db;

    public async Task AcquireAsync(string resource, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"'{resource}' kilidi açık transaction ister; komut ITransactionalRequest olmalı.");

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await _db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout",
            [
                result,
                new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource },
                new SqlParameter("@timeout", SqlDbType.Int) { Value = TimeoutMilliseconds },
            ],
            ct);

        // 0: hemen, 1: bekledikten sonra verildi; eksi değerler zaman aşımı, iptal ya da kilitlenme kurbanı.
        if (result.Value is not int code || code < 0)
            throw new TimeoutException($"'{resource}' kilidi alınamadı (sp_getapplock sonucu {result.Value}).");
    }
}
