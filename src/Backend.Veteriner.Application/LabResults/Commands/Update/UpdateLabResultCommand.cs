using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.LabResults.Commands.Update;

public sealed record UpdateLabResultCommand(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime ResultDateUtc,
    string TestName,
    string ResultText,
    string? Interpretation,
    string? Notes)
    : IRequest<Result>;
