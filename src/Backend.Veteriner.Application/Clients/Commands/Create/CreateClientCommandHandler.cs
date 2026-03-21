using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.Create;

public sealed class CreateClientCommandHandler : IRequestHandler<CreateClientCommand, Result<Guid>>
{
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clientsRead;
    private readonly IRepository<Client> _clientsWrite;

    public CreateClientCommandHandler(
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clientsRead,
        IRepository<Client> clientsWrite)
    {
        _tenants = tenants;
        _clientsRead = clientsRead;
        _clientsWrite = clientsWrite;
    }

    public async Task<Result<Guid>> Handle(CreateClientCommand request, CancellationToken ct)
    {
        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için müşteri oluşturulamaz.");

        var nameKey = request.FullName.Trim().ToLowerInvariant();
        var phoneKey = string.IsNullOrWhiteSpace(request.Phone) ? "" : request.Phone.Trim();

        var duplicate = await _clientsRead.FirstOrDefaultAsync(
            new ClientByTenantFullNameAndPhoneSpec(request.TenantId, nameKey, phoneKey), ct);
        if (duplicate is not null)
            return Result<Guid>.Failure(
                "Clients.DuplicateClient",
                "Bu kiracı altında aynı ad ve telefon bilgisiyle kayıtlı bir müşteri zaten var.");

        var client = new Client(request.TenantId, request.FullName, request.Phone);
        await _clientsWrite.AddAsync(client, ct);
        await _clientsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(client.Id);
    }
}
