using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.Update;

public sealed record UpdateClientCommand(
    Guid Id,
    string FullName,
    string? Email = null,
    string? Phone = null,
    string? Address = null)
    : IRequest<Result>;
