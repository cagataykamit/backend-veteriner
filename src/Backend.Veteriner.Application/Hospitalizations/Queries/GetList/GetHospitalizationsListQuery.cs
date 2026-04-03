using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Queries.GetList;

public sealed record GetHospitalizationsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    bool? ActiveOnly = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null)
    : IRequest<Result<PagedResult<HospitalizationListItemDto>>>;
