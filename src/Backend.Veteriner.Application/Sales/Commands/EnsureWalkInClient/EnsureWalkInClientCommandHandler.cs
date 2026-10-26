using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Sales.Commands.EnsureWalkInClient;

public sealed class EnsureWalkInClientCommandHandler
    : IRequestHandler<EnsureWalkInClientCommand, Result<WalkInClientDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clientsRead;
    private readonly IRepository<Client> _clientsWrite;
    private readonly IClientIntegrationEventOutbox _eventOutbox;

    public EnsureWalkInClientCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clientsRead,
        IRepository<Client> clientsWrite,
        IClientIntegrationEventOutbox eventOutbox)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _clientsRead = clientsRead;
        _clientsWrite = clientsWrite;
        _eventOutbox = eventOutbox;
    }

    public async ValueTask<Result<WalkInClientDto>> Handle(EnsureWalkInClientCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<WalkInClientDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<WalkInClientDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<WalkInClientDto>.Failure("Tenants.TenantInactive", "Pasif kiracı için müşteri oluşturulamaz.");

        var existing = await _clientsRead.FirstOrDefaultAsync(new WalkInClientSpec(tenantId), ct);
        if (existing is not null)
            return Result<WalkInClientDto>.Success(new WalkInClientDto(existing.Id, existing.FullName, false));

        var client = Client.CreateWalkIn(tenantId);
        try
        {
            await _clientsWrite.AddAsync(client, ct);

            // Outbox emission aynı SaveChanges/transaction sınırında kalıcı olur (buffer interceptor ile drain edilir).
            await _eventOutbox.EnqueueAsync(
                ClientIntegrationEventTypes.Created,
                new ClientCreatedIntegrationEvent(
                    Guid.NewGuid(),
                    DateTime.UtcNow,
                    ClientProjectionSnapshotFactory.Create(client)),
                ct);

            await _clientsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı ilk satış: filtreli benzersiz indeks kaybedene çarptı. İzlenen başarısız kayıt bu işlemde
            // yeniden yazılamayacağı için işlem geri alınır; tekrar denemek güvenlidir.
            if (await _clientsRead.FirstOrDefaultAsync(new WalkInClientSpec(tenantId), ct) is null)
                throw;

            return Result<WalkInClientDto>.Failure(
                "Sales.ConcurrencyConflict",
                "Peşin müşteri eşzamanlı olarak oluşturuldu; işlem tekrarlanmalı.");
        }

        return Result<WalkInClientDto>.Success(new WalkInClientDto(client.Id, client.FullName, true));
    }
}
