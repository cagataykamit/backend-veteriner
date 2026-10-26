using Backend.Veteriner.Application.StockBatches.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.StockBatches.Queries.GetProductBalance;

/// <summary>Ürün bazlı parti bakiyesi; klinik kapsamı zorunlu (<see cref="ClinicId"/> veya aktif klinik bağlamı).</summary>
public sealed record GetProductBatchBalanceQuery(Guid ProductId, Guid? ClinicId = null)
    : IRequest<Result<ProductBatchBalanceDto>>;
