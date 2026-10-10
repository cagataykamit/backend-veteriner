namespace Backend.Veteriner.Application.Charges.Contracts.Dtos;

/// <summary>Ücret satırı. <see cref="PetId"/>, <see cref="VisitId"/>, <see cref="ExaminationId"/> null olabilir.</summary>
public sealed record ChargeDto(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    Guid? PetId,
    string? PetName,
    Guid? VisitId,
    Guid? ExaminationId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    string Currency,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    decimal AllocatedAmount,
    decimal OutstandingAmount);

/// <summary>Ücret ekleme sonucu; <see cref="Created"/> false ise aynı <c>requestId</c> ile tekrar istek ilk kaydı döndürmüştür.</summary>
public sealed record ChargeCreateResultDto(bool Created, ChargeDto Charge);
