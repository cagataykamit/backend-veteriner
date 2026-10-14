using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Sales.Queries.GetList;

/// <summary>Klinik kapsamı zorunlu: <see cref="ClinicId"/> veya aktif klinik bağlamı. <see cref="ToUtc"/> hariçtir; iptaller varsayılan gizli.</summary>
public sealed record GetSalesListQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? ClinicId = null,
    Guid? ClientId = null,
    Guid? ProductId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    bool IncludeVoided = false)
    : IRequest<Result<PagedResult<SaleDto>>>;
