using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Commands.Transition;

/// <summary>
/// İleri yönlü bakım durumu geçişi: <see cref="VisitCareStatus.InProgress"/> (başlat) veya
/// <see cref="VisitCareStatus.Completed"/> (tamamla). Geri alma yalnızca düzeltme komutuyla yapılır.
/// </summary>
public sealed record TransitionVisitCommand(Guid VisitId, VisitCareStatus Target)
    : IRequest<Result<VisitDto>>;
