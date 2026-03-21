using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// UserOperationClaim veri erişimi.
/// Not: Projede UnitOfWork olmadığı için implementasyonlar SaveChanges çağırır.
/// </summary>
public interface IUserOperationClaimRepository
{
    Task<bool> ExistsAsync(Guid userId, Guid operationClaimId, CancellationToken ct);

    Task AddAsync(UserOperationClaim entity, CancellationToken ct);
    Task RemoveAsync(Guid userId, Guid operationClaimId, CancellationToken ct);

    Task<UserOperationClaim?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<UserOperationClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<UserOperationClaimDetailDto>> GetDetailsByUserIdAsync(Guid userId, CancellationToken ct);
}
