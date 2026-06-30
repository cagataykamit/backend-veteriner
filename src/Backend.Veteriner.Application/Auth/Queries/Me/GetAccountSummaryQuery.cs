using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Me;

public sealed record GetAccountSummaryQuery : IRequest<Result<AccountSummaryDto>>;
