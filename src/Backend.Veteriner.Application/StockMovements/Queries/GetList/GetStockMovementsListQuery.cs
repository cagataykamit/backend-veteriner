using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.StockMovements.Queries.GetList;

public sealed record GetStockMovementsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? ProductId = null,
    Guid? ProductCategoryId = null,
    StockMovementType? MovementType = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null)
    : IRequest<Result<PagedResult<StockMovementDto>>>;
