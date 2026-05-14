using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.VaccineDefinitions.Commands.Update;

public sealed record UpdateVaccineDefinitionCommand(
    Guid Id,
    Guid? SpeciesId,
    string Name,
    string Code,
    string? Description,
    int? DefaultNextDueDays) : IRequest<Result>;

public sealed class UpdateVaccineDefinitionCommandHandler : IRequestHandler<UpdateVaccineDefinitionCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Species> _species;
    private readonly IReadRepository<VaccineDefinition> _definitionsRead;
    private readonly IRepository<VaccineDefinition> _definitionsWrite;

    public UpdateVaccineDefinitionCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Species> species,
        IReadRepository<VaccineDefinition> definitionsRead,
        IRepository<VaccineDefinition> definitionsWrite)
    {
        _tenantContext = tenantContext;
        _species = species;
        _definitionsRead = definitionsRead;
        _definitionsWrite = definitionsWrite;
    }

    public async Task<Result> Handle(UpdateVaccineDefinitionCommand request, CancellationToken ct)
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
                "Sistem aşı tanımları güncellenemez.");
        }

        if (entity.TenantId != tenantId)
            return Result.Failure("VaccineDefinitions.NotFound", "Aşı tanımı bulunamadı.");

        if (request.SpeciesId is { } sid)
        {
            var species = await _species.FirstOrDefaultAsync(new SpeciesByIdSpec(sid), ct);
            if (species is null)
                return Result.Failure("VaccineDefinitions.InvalidSpecies", "Geçersiz tür (species) seçildi.");
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var dup = await _definitionsRead.CountAsync(
            new VaccineDefinitionTenantCodeExistsSpec(tenantId, normalizedCode, request.Id),
            ct);
        if (dup > 0)
            return Result.Failure("VaccineDefinitions.DuplicateCode", "Bu kiracıda aynı kod zaten kullanılıyor.");

        var updated = entity.UpdateDetails(
            request.Code,
            request.Name,
            request.Description,
            request.DefaultNextDueDays,
            request.SpeciesId,
            isCore: false);

        if (!updated.IsSuccess)
            return Result.Failure(updated.Error);

        await _definitionsWrite.UpdateAsync(entity, ct);
        await _definitionsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
