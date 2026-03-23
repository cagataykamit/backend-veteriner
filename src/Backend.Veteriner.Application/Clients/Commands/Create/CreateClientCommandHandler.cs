using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.Create;

public sealed class CreateClientCommandHandler : IRequestHandler<CreateClientCommand, Result<ClientCreatedDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clientsRead;
    private readonly IRepository<Client> _clientsWrite;

    public CreateClientCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clientsRead,
        IRepository<Client> clientsWrite)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _clientsRead = clientsRead;
        _clientsWrite = clientsWrite;
    }

    public async Task<Result<ClientCreatedDto>> Handle(CreateClientCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientCreatedDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<ClientCreatedDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<ClientCreatedDto>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için müşteri oluşturulamaz.");

        var emailNorm = Client.NormalizeEmailForStorage(request.Email);
        string? phoneNorm = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
            TurkishMobilePhone.TryNormalize(request.Phone, out phoneNorm);

        if (!string.IsNullOrEmpty(emailNorm) && !string.IsNullOrEmpty(phoneNorm))
        {
            var duplicate = await _clientsRead.FirstOrDefaultAsync(
                new ClientByTenantNormalizedEmailAndPhoneSpec(tenantId, emailNorm, phoneNorm), ct);
            if (duplicate is not null)
            {
                return Result<ClientCreatedDto>.Failure(
                    "Clients.DuplicateClient",
                    "Bu kiracı altında aynı e-posta ve telefon ile kayıtlı bir müşteri zaten var.");
            }
        }

        var client = new Client(tenantId, request.FullName, request.Phone, request.Email);
        await _clientsWrite.AddAsync(client, ct);
        await _clientsWrite.SaveChangesAsync(ct);

        var dto = new ClientCreatedDto(client.Id, client.TenantId, client.FullName, client.Email, client.Phone);
        return Result<ClientCreatedDto>.Success(dto);
    }
}
