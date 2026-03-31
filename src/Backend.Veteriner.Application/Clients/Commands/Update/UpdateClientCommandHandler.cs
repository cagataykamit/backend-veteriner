using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.Update;

public sealed class UpdateClientCommandHandler : IRequestHandler<UpdateClientCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clientsRead;
    private readonly IRepository<Client> _clientsWrite;

    public UpdateClientCommandHandler(
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

    public async Task<Result> Handle(UpdateClientCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için müşteri güncellenemez.");

        var client = await _clientsRead.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var emailNorm = Client.NormalizeEmailForStorage(request.Email);
        string? phoneNorm = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
            TurkishMobilePhone.TryNormalize(request.Phone, out phoneNorm);

        if (!string.IsNullOrEmpty(emailNorm) && !string.IsNullOrEmpty(phoneNorm))
        {
            var duplicate = await _clientsRead.FirstOrDefaultAsync(
                new ClientByTenantNormalizedEmailAndPhoneSpec(tenantId, emailNorm, phoneNorm), ct);
            if (duplicate is not null && duplicate.Id != request.Id)
            {
                return Result.Failure(
                    "Clients.DuplicateClient",
                    "Bu kiracı altında aynı e-posta ve telefon ile kayıtlı bir müşteri zaten var.");
            }
        }

        client.UpdateDetails(request.FullName, request.Email, request.Phone, request.Address);
        await _clientsWrite.UpdateAsync(client, ct);
        await _clientsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
