using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using MediatR;

namespace Backend.Veteriner.Application.Users.Queries.GetAll;

/// <summary>
/// Admin kullanıcı listeleme query handler.
/// Not: Ardalis.Specification tabanlı IReadRepository IQueryable expose etmez.
/// Bu nedenle admin listeleme gibi paging+filter senaryoları IUserReadRepository üzerindeki özel metotla yürütülür.
/// </summary>
public sealed class AdminGetUsersQueryHandler
    : IRequestHandler<AdminGetUsersQuery, PagedResult<AdminUserListItemDto>>
{
    private readonly IUserReadRepository _users;

    public AdminGetUsersQueryHandler(IUserReadRepository users)
        => _users = users;

    public Task<PagedResult<AdminUserListItemDto>> Handle(AdminGetUsersQuery request, CancellationToken ct)
        => _users.GetAdminPagedAsync(request.PageRequest, ct);
}
