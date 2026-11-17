namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Denetle-sonra-yaz kurallarını (saat çakışması, adet sınırı, eksikse oluştur) eşzamanlı isteklere karşı sıraya sokar: aynı
/// kaynak adıyla gelen ikinci istek, ilk isteğin transaction'ı bitene kadar bekler ve sonra güncel veriyi okur. Benzersiz indeksle
/// ifade edilemeyen kurallar içindir. Komut <see cref="ITransactionalRequest"/> olmalı; açık transaction yoksa istisna atılır.
/// </summary>
public interface ITransactionLock
{
    /// <param name="resource">Kilit adı; en çok 255 karakter (ör. <c>appointments:{tenantId:N}</c>).</param>
    Task AcquireAsync(string resource, CancellationToken ct);
}
