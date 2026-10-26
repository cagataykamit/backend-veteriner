using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LegalAcceptances.Queries.GetMyStatus;

/// <summary>Oturumdaki kullanıcının güncel sürümünü henüz kabul etmediği belgeler (ADR-029 madde 5–6).</summary>
public sealed record GetMyLegalAcceptanceStatusQuery : IRequest<Result<MyLegalAcceptanceStatusDto>>;
