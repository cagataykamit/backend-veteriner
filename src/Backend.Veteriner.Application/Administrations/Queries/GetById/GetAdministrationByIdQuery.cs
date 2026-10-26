using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Administrations.Queries.GetById;

public sealed record GetAdministrationByIdQuery(Guid Id) : IRequest<Result<AdministrationDto>>;
