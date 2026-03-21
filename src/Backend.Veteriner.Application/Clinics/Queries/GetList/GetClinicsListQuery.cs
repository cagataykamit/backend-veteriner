using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.GetList;

public sealed record GetClinicsListQuery(Guid TenantId, PageRequest PageRequest)
    : IRequest<Result<PagedResult<ClinicListItemDto>>>;
