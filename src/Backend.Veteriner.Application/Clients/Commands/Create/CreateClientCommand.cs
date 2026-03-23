using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.Create;

public sealed record CreateClientCommand(string FullName, string? Email = null, string? Phone = null)
    : IRequest<Result<ClientCreatedDto>>;
