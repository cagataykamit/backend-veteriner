using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.StockBatches.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.StockBatches.Queries.GetList;

/// <summary>
/// Klinik kapsamı zorunlu: <see cref="ClinicId"/> veya aktif klinik bağlamı. Varsayılan yalnız kalanı olan partiler;
/// <see cref="Expired"/>: true yalnız süresi geçmiş, false yalnız geçmemiş. Sıralama FEFO.
/// </summary>
public sealed record GetStockBatchesListQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? ClinicId = null,
    Guid? ProductId = null,
    string? LotNumber = null,
    bool AvailableOnly = true,
    bool? Expired = null,
    DateOnly? ExpiresBeforeDate = null)
    : IRequest<Result<PagedResult<StockBatchDto>>>;
