namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Bu işaretleyici interface'i implement eden MediatR request'leri
/// TransactionBehavior tarafından DB transaction içinde çalıştırılır.
/// Genellikle command'lerde kullanılır, query'lerde kullanılmaz.
/// </summary>
public interface ITransactionalRequest
{
}

/// <summary>
/// <see cref="ITransactionalRequest"/> gibi tek DB transaction içinde çalışır; ek olarak yanıt
/// başarısız bir <c>Result</c> ise (iş kuralı hatası) transaction <b>geri alınır</b>.
/// Birden fazla komutu sırayla çalıştıran ve yarım kayıt bırakmaması gereken orkestrasyonlar için.
/// </summary>
public interface ITransactionalRollbackOnFailureRequest : ITransactionalRequest
{
}
