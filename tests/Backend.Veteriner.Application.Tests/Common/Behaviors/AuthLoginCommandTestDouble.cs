using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Auth.TestDoubles;

public sealed record AuthLoginCommand : IRequest<Result>;
