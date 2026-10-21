using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetInvoicingIdentity;

public sealed class GetClientInvoicingIdentityQueryHandler
    : IRequestHandler<GetClientInvoicingIdentityQuery, Result<ClientInvoicingIdentityDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Client> _clients;

    public GetClientInvoicingIdentityQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _clients = clients;
    }

    public async Task<Result<ClientInvoicingIdentityDto>> Handle(GetClientInvoicingIdentityQuery request, CancellationToken ct)
    {
        // Tam kimlik numarası yalnız düzenleme yetkisiyle döner (uç politikasına ek savunma).
        if (!_permissions.HasPermission(PermissionCatalog.Clients.Update))
        {
            return Result<ClientInvoicingIdentityDto>.Failure(
                "Auth.PermissionDenied",
                "Fatura kimliğini görmek için Clients.Update yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientInvoicingIdentityDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Result<ClientInvoicingIdentityDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        return Result<ClientInvoicingIdentityDto>.Success(ClientInvoicingIdentityDto.From(client));
    }
}
