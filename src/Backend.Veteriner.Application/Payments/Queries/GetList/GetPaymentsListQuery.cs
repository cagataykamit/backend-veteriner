using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Queries.GetList;

public sealed record GetPaymentsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? ClientId = null,
    Guid? PetId = null,
    PaymentMethod? Method = null,
    DateTime? PaidFromUtc = null,
    DateTime? PaidToUtc = null)
    : IRequest<Result<PagedResult<PaymentListItemDto>>>;
