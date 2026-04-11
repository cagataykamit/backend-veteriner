using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.Billing;

public sealed record ProcessBillingWebhookCommand(
    BillingProvider Provider,
    string RawBody,
    IReadOnlyDictionary<string, string> Headers)
    : IRequest<Result<BillingWebhookAckDto>>, ITransactionalRequest, IIgnoreTenantWriteSubscriptionGuard;
