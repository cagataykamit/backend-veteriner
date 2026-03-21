using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Domain.Authorization;

namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IPermissionRepository
{
    // CRUD
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct);
    Task<Permission?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Permission?> GetByCodeAsync(string code, CancellationToken ct);
    Task AddAsync(Permission permission, CancellationToken ct);
    Task UpdateAsync(Permission permission, CancellationToken ct);
    Task DeleteAsync(Permission permission, CancellationToken ct);

    // List (EF ba��ml�l���n� Application'dan kald�r�r)
    Task<IReadOnlyList<PermissionDto>> GetListAsync(string? search, CancellationToken ct);
}
