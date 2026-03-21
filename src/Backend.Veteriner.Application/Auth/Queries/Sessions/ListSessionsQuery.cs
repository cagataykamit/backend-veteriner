using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Sessions;

public sealed record ListSessionsQuery : IRequest<Result<IReadOnlyList<SessionDto>>>;
