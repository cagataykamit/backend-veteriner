using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.VaccineDefinitions.Commands.Deactivate;

public sealed record DeactivateVaccineDefinitionCommand(Guid Id) : IRequest<Result>;

public sealed class DeactivateVaccineDefinitionCommandHandler : IRequestHandler<DeactivateVaccineDefinitionCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<VaccineDefinition> _definitionsRead;
    private readonly IRepository<VaccineDefinition> _definitionsWrite;

    public DeactivateVaccineDefinitionCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<VaccineDefinition> definitionsRead,
        IRepository<VaccineDefinition> definitionsWrite)
    {
        _tenantContext = tenantContext;
        _definitionsRead = definitionsRead;
        _definitionsWrite = definitionsWrite;
    }

    public async Task<Result> Handle(DeactivateVaccineDefinitionCommand request, CancellationToken ct)
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

        entity.Deactivate();
        await _definitionsWrite.UpdateAsync(entity, ct);
        await _definitionsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
