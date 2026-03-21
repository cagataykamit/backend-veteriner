using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Permissions.GetAll;

public sealed record GetAllPermissionsQuery(PageRequest Req)
    : IRequest<PagedResult<PermissionDto>>;
