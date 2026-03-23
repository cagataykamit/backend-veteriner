using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetById;

public sealed class GetClientByIdQueryHandler : IRequestHandler<GetClientByIdQuery, Result<ClientDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Client> _clients;

    public GetClientByIdQueryHandler(ITenantContext tenantContext, IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clients = clients;
    }

    public async Task<Result<ClientDetailDto>> Handle(GetClientByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result<ClientDetailDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var dto = new ClientDetailDto(client.Id, client.TenantId, client.FullName, client.Email, client.Phone);
        return Result<ClientDetailDto>.Success(dto);
    }
}
