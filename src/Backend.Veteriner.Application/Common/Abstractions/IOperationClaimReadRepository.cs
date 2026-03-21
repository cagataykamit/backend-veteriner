namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// OperationClaim (rol) varlık kontrolü için read-only sözleşme.
/// Assign gibi işlemlerde rolün mevcut olup olmadığını doğrulamak için kullanılır.
/// </summary>
public interface IOperationClaimReadRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
}
