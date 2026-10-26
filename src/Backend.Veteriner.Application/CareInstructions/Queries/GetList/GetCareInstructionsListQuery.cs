using Backend.Veteriner.Application.CareInstructions.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.CareInstructions.Queries.GetList;

public sealed record GetCareInstructionsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    Guid? HospitalizationId = null,
    Guid? ExaminationId = null,
    Guid? VisitId = null)
    : IRequest<Result<PagedResult<CareInstructionDto>>>;
