using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetList;

public sealed record GetClientsListQuery(PageRequest PageRequest)
    : IRequest<Result<PagedResult<ClientListItemDto>>>;
