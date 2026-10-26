using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Queries.Sessions;

public sealed record ListSessionsQuery : IRequest<Result<IReadOnlyList<SessionDto>>>;
