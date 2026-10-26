using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Queries.Me;

public sealed record GetAccountSummaryQuery : IRequest<Result<AccountSummaryDto>>;
