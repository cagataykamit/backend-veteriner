using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.UpdateSettings;

/// <summary>
/// Tenant-scoped kurum ayarları güncelleme (Faz 5B). Şu an kapsam: <c>Name</c>.
/// Global admin <c>POST /api/v1/tenants</c> yüzeyine dokunmaz.
/// </summary>
public sealed record UpdateTenantSettingsCommand(Guid TenantId, string Name)
    : IRequest<Result<TenantDetailDto>>;
