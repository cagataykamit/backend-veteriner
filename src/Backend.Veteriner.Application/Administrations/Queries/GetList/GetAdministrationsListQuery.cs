using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Administrations.Queries.GetList;

/// <summary>Klinik kapsamı zorunlu: <see cref="ClinicId"/> veya aktif klinik bağlamı. İptaller varsayılan gizli.</summary>
public sealed record GetAdministrationsListQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? ClinicId = null,
    Guid? PetId = null,
    Guid? ExaminationId = null,
    Guid? TreatmentId = null,
    Guid? ProductId = null,
    bool IncludeVoided = false)
    : IRequest<Result<PagedResult<AdministrationDto>>>;
