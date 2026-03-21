using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Me;

public sealed record GetMeQuery(Guid UserId) : IRequest<MeDto>;
