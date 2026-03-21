namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Uygulama seviyesinde tek commit noktası.
/// Handler sonunda transaction boundary olarak kullanılır.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}