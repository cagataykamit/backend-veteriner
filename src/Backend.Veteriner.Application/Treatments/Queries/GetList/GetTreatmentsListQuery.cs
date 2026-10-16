using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Treatments.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Treatments.Queries.GetList;

/// <param name="ActiveOnly">true: yalnız aktif tedaviler (<see cref="ActiveTreatmentRule"/>); sort verilmezse en yakın takip önce.</param>
public sealed record GetTreatmentsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null,
    bool ActiveOnly = false)
    : IRequest<Result<PagedResult<TreatmentListItemDto>>>;
