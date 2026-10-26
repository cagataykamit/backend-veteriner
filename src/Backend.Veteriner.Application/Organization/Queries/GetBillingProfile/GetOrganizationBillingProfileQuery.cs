using Backend.Veteriner.Application.Organization.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Organization.Queries.GetBillingProfile;

public sealed record GetOrganizationBillingProfileQuery : IRequest<Result<OrganizationBillingProfileDto>>;
