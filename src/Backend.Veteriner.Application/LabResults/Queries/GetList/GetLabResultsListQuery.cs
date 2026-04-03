using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.LabResults.Queries.GetList;

public sealed record GetLabResultsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null)
    : IRequest<Result<PagedResult<LabResultListItemDto>>>;
