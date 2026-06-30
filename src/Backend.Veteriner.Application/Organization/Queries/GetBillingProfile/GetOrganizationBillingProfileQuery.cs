using Backend.Veteriner.Application.Organization.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Organization.Queries.GetBillingProfile;

public sealed record GetOrganizationBillingProfileQuery : IRequest<Result<OrganizationBillingProfileDto>>;
