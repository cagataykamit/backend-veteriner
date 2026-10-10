using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Charges.Queries.GetList;

/// <summary>Klinik kapsamı zorunlu: <see cref="ClinicId"/> veya aktif klinik bağlamı.</summary>
public sealed record GetChargesListQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? ClinicId = null,
    Guid? ClientId = null,
    Guid? PetId = null,
    Guid? VisitId = null,
    Guid? ExaminationId = null)
    : IRequest<Result<PagedResult<ChargeDto>>>;
