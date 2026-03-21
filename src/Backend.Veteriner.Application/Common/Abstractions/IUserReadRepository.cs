using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// User aggregate read sözleşmesi.
/// Auth akışları (Login/Me/Refresh) Ardalis'in spec metotlarını bu repo üzerinden kullanır.
/// Admin listeleme gibi paging+filter senaryoları için özel metotlar eklenir.
/// </summary>
public interface IUserReadRepository : IReadRepository<User>
{
    /// <summary>
    /// Admin kullanıcı listeleme için sayfalı sorgu.
    /// Ardalis reposu IQueryable expose etmediği için use-case bazlı metotla çözülür.
    /// </summary>
    Task<PagedResult<AdminUserListItemDto>> GetAdminPagedAsync(PageRequest req, CancellationToken ct);
}
