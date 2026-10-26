using Backend.Veteriner.Application.ChargeSuggestions.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ChargeSuggestions.Queries.GetList;

/// <summary>GET /api/v1/charge-suggestions: bekleyen kalemler. Klinik kapsamı zorunlu: <see cref="ClinicId"/> veya aktif klinik bağlamı.
/// <see cref="ClientId"/>: müşterinin <b>güncel</b> hayvanlarının kalemleri (kalemin müşterisi hayvanın güncel sahibidir).</summary>
public sealed record GetChargeSuggestionsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? ClinicId = null,
    Guid? PetId = null,
    Guid? ExaminationId = null,
    Guid? VisitId = null,
    Guid? ClientId = null)
    : IRequest<Result<PagedResult<ChargeSuggestionDto>>>;
