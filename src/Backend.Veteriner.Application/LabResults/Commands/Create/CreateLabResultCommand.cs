using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.LabResults.Commands.Create;

public sealed record CreateLabResultCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime ResultDateUtc,
    string TestName,
    string ResultText,
    string? Interpretation,
    string? Notes)
    : IRequest<Result<Guid>>;
