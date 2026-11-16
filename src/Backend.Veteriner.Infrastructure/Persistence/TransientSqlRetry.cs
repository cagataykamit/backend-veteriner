using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Arka plan işlerinde geçici SQL Server hatalarının (kilitlenme kurbanı 1205, kilit ya da komut zaman aşımı, bağlantı
/// kopması) kısa bekleme ve sınırlı yeniden denemeyle karşılanmasının tek yeri. Denemeler tükenirse istisna çağırana
/// gider; işin kendi hata yolu (yeniden kuyruk, başarısız durum) aynen çalışır.
/// <para>
/// İşlem her denemede baştan çalışmalı ve tekrarı zararsız olmalıdır: yeni DI kapsamı (ya da temizlenmiş değişiklik
/// izleyicisi), kendi veritabanı işlemi geri alınmış bir birim ya da koşullu tek komut. API istekleri bunu kullanmaz:
/// kullanıcı isteğinin işlem hattı (<c>TransactionBehavior</c>) dıştan yeniden denemeyle çakışır.
/// </para>
/// </summary>
public static class TransientSqlRetry
{
    /// <summary>Denemeler arası bekleme; uzunluğu ek deneme sayısıdır (toplam ~4 sn).</summary>
    public static readonly IReadOnlyList<TimeSpan> Delays =
    [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
    ];

    /// <summary>
    /// Geçici sayılan SQL Server hata numaraları: 1205 kilitlenme kurbanı, 1222 kilit isteği zaman aşımı, -2 komut zaman
    /// aşımı, 233/64/10053/10054/10060 bağlantı kopması, 40197/40501/40613/49918–49920 sunucu meşgul ya da yeniden başlıyor.
    /// </summary>
    private static readonly HashSet<int> TransientNumbers =
        [1205, 1222, -2, 233, 64, 10053, 10054, 10060, 40197, 40501, 40613, 49918, 49919, 49920];

    /// <summary>İstisna ya da iç istisnalarından biri geçici SQL Server hatası mı (EF'in sarmaladığı hatalar dahil).</summary>
    public static bool IsTransient(Exception? exception) => TransientNumber(exception) is not null;

    /// <summary>Geçici hatayı <see cref="Delays"/> aralıklarla yeniden dener; başka hata ve iptal hemen çağırana gider.</summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ILogger? logger,
        string operationName,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(ct);
            }
            catch (Exception ex) when (attempt < Delays.Count && !ct.IsCancellationRequested && TransientNumber(ex) is { } number)
            {
                // İstisna mesajı kişisel veri içerebilir: yalnız işlem adı ve hata numarası yazılır.
                logger?.LogWarning(
                    "Transient SQL error; retrying. Operation={Operation} SqlError={SqlError} Attempt={Attempt}",
                    operationName, number, attempt + 1);
                await Task.Delay(Delays[attempt], ct);
            }
        }
    }

    /// <inheritdoc cref="ExecuteAsync{T}"/>
    public static Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        ILogger? logger,
        string operationName,
        CancellationToken ct)
        => ExecuteAsync(
            async c =>
            {
                await operation(c);
                return true;
            },
            logger,
            operationName,
            ct);

    private static int? TransientNumber(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is not SqlException sql)
                continue;

            foreach (SqlError error in sql.Errors)
            {
                if (TransientNumbers.Contains(error.Number))
                    return error.Number;
            }
        }

        return null;
    }
}
