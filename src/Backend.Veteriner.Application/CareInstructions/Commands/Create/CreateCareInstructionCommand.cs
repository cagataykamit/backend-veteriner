using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.CareInstructions.Commands.Create;

/// <summary>
/// Hasta sahibine taburcu / evde bakım talimatı. Bağlar (<see cref="HospitalizationId"/>, <see cref="ExaminationId"/>, <see cref="VisitId"/>)
/// opsiyoneldir ve sonradan değişmez; verilmeyen muayene/geliş yatıştan, geliş ayrıca muayeneden miras alınır.
/// </summary>
public sealed record CreateCareInstructionCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? HospitalizationId,
    Guid? ExaminationId,
    Guid? VisitId,
    string Text)
    : IRequest<Result<Guid>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "CareInstruction.Create";
    public string? AuditTarget => $"PetId={PetId}";
}
