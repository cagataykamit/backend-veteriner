using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Visits.Queries.GetOpenItemVisits;

/// <summary>Bugün "Açık işler" özetindeki ziyaret sayısının arkasındaki ziyaretler (docs/OPEN_ITEMS_API_CONTRACT.md madde 10).</summary>
/// <param name="ClinicId">Klinik bağlamı yoksa zorunlu.</param>
public sealed record GetOpenItemVisitsQuery(Guid? ClinicId, OpenItemVisitKind Kind) : IRequest<Result<OpenItemVisitsDto>>;
