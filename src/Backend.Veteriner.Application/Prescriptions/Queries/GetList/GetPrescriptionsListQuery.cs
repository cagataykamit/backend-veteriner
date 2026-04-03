using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Prescriptions.Queries.GetList;

public sealed record GetPrescriptionsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null)
    : IRequest<Result<PagedResult<PrescriptionListItemDto>>>;
