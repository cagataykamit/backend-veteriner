namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Bu işaretleyici interface'i implement eden MediatR request'leri
/// TransactionBehavior tarafından DB transaction içinde çalıştırılır.
/// Genellikle command'lerde kullanılır, query'lerde kullanılmaz.
/// </summary>
public interface ITransactionalRequest
{
}