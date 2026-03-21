using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetList;

public sealed record GetExaminationsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    Guid? AppointmentId = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null)
    : IRequest<Result<PagedResult<ExaminationListItemDto>>>;
