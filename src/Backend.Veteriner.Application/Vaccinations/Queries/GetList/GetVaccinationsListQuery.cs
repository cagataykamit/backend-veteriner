using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetList;

public sealed record GetVaccinationsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    VaccinationStatus? Status = null,
    DateTime? DueFromUtc = null,
    DateTime? DueToUtc = null,
    DateTime? AppliedFromUtc = null,
    DateTime? AppliedToUtc = null)
    : IRequest<Result<PagedResult<VaccinationListItemDto>>>;
