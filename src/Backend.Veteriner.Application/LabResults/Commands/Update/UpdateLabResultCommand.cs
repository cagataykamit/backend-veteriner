using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LabResults.Commands.Update;

/// <summary>
/// Tam güncelleme; <see cref="RowVersion"/> zorunludur. <c>Requested</c> kayda sonuç yazılırsa kayıt <c>Resulted</c> olur.
/// <see cref="VisitId"/> verilmezse muayene değişmediyse mevcut bağ korunur, değiştiyse yeni muayeneden miras alınır.
/// </summary>
public sealed record UpdateLabResultCommand(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime? ResultDateUtc,
    string TestName,
    string? ResultText,
    string? Interpretation,
    string? Notes,
    string? RowVersion,
    Guid? VisitId = null)
    : IRequest<Result<LabResultWriteResultDto>>;
