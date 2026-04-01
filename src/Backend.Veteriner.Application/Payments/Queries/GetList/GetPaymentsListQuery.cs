using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Queries.GetList;

/// <summary>Ödeme listesi sorgusu. Metin araması için HTTP query: <c>search</c> (boş/whitespace yok sayılır).</summary>
public sealed record GetPaymentsListQuery(
    PaymentListPagingRequest Paging,
    Guid? ClinicId = null,
    Guid? ClientId = null,
    Guid? PetId = null,
    PaymentMethod? Method = null,
    DateTime? PaidFromUtc = null,
    DateTime? PaidToUtc = null,
    string? Search = null)
    : IRequest<Result<PagedResult<PaymentListItemDto>>>;
