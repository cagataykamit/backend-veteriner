using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LabResults.Commands.Create;

/// <summary>
/// <see cref="ResultText"/> ve <see cref="ResultDateUtc"/> verilmezse kayıt sonuçsuz istem (<c>Requested</c>), ikisi de verilirse sonuçlanmış (<c>Resulted</c>) olur.
/// <see cref="VisitId"/> verilmezse muayenenin gelişi miras alınır.
/// </summary>
public sealed record CreateLabResultCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime? ResultDateUtc,
    string TestName,
    string? ResultText,
    string? Interpretation,
    string? Notes,
    Guid? VisitId = null)
    : IRequest<Result<Guid>>;
