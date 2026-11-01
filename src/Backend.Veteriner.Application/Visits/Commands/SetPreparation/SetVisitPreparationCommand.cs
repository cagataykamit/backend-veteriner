using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Visits.Commands.SetPreparation;

/// <summary>
/// Geliş hazırlığını yazar (CHECKIN-001/002/003, <c>Visits.Update</c>): geliş nedeni ve hazırlanan şablon/paket. Tam yazım: null alan
/// temizler. Tamamlanmış gelişte 409 <c>Visits.NotOpen</c>; aynı değerler idempotenttir.
/// </summary>
public sealed record SetVisitPreparationCommand(Guid VisitId, string? ArrivalReason, Guid? PreparedExaminationTemplateId)
    : IRequest<Result<VisitDto>>;
