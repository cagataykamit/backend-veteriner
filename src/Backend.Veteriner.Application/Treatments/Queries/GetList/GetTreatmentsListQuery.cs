using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Treatments.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Treatments.Queries.GetList;

public sealed record GetTreatmentsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null)
    : IRequest<Result<PagedResult<TreatmentListItemDto>>>;
