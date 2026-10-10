using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Veterinarians;

/// <summary>Geliş açarken sorumlu hekim seçimi için klinikteki aktif hekimler.</summary>
public sealed record GetClinicVeterinariansQuery(Guid ClinicId)
    : IRequest<Result<IReadOnlyList<ClinicVeterinarianDto>>>;

public sealed class GetClinicVeterinariansQueryHandler
    : IRequestHandler<GetClinicVeterinariansQuery, Result<IReadOnlyList<ClinicVeterinarianDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IClinicVeterinarianReader _veterinarians;

    public GetClinicVeterinariansQueryHandler(
        ITenantContext tenantContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IClinicVeterinarianReader veterinarians)
    {
        _tenantContext = tenantContext;
        _clinicScopeResolver = clinicScopeResolver;
        _veterinarians = veterinarians;
    }

    public async Task<Result<IReadOnlyList<ClinicVeterinarianDto>>> Handle(
        GetClinicVeterinariansQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ClinicVeterinarianDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, request.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<ClinicVeterinarianDto>>.Failure(scope.Error);

        return Result<IReadOnlyList<ClinicVeterinarianDto>>.Success(
            await _veterinarians.ListAsync(tenantId, request.ClinicId, ct));
    }
}
