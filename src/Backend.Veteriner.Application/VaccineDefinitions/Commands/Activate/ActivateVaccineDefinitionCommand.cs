using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.VaccineDefinitions.Commands.Activate;

public sealed record ActivateVaccineDefinitionCommand(Guid Id) : IRequest<Result>;

public sealed class ActivateVaccineDefinitionCommandHandler : IRequestHandler<ActivateVaccineDefinitionCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<VaccineDefinition> _definitionsRead;
    private readonly IRepository<VaccineDefinition> _definitionsWrite;
    private readonly TimeProvider _timeProvider;

    public ActivateVaccineDefinitionCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<VaccineDefinition> definitionsRead,
        IRepository<VaccineDefinition> definitionsWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _definitionsRead = definitionsRead;
        _definitionsWrite = definitionsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(ActivateVaccineDefinitionCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var entity = await _definitionsRead.FirstOrDefaultAsync(new VaccineDefinitionByIdSpec(request.Id), ct);
        if (entity is null)
            return Result.Failure("VaccineDefinitions.NotFound", "Aşı tanımı bulunamadı.");

        if (entity.TenantId is null || entity.IsCore)
        {
            return Result.Failure(
                "VaccineDefinitions.CoreDefinitionCannotBeModified",
                "Sistem aşı tanımları üzerinde bu işlem yapılamaz.");
        }

        if (entity.TenantId != tenantId)
            return Result.Failure("VaccineDefinitions.NotFound", "Aşı tanımı bulunamadı.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        entity.Activate(utcNow);
        await _definitionsWrite.UpdateAsync(entity, ct);
        await _definitionsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
