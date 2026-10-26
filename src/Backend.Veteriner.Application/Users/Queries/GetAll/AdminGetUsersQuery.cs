using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Mediator;

namespace Backend.Veteriner.Application.Users.Queries.GetAll;

/// <summary>
/// Admin kullanıcı liste (paged).
/// </summary>
public sealed record AdminGetUsersQuery(PageRequest PageRequest)
    : IRequest<PagedResult<AdminUserListItemDto>>;
