using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.VaccineDefinitions.Commands.Create;

public sealed record CreateVaccineDefinitionCommand(
    Guid? SpeciesId,
    string Name,
    string Code,
    string? Description,
    int? DefaultNextDueDays) : IRequest<Result<Guid>>;

public sealed class CreateVaccineDefinitionCommandHandler
    : IRequestHandler<CreateVaccineDefinitionCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Species> _species;
    private readonly IReadRepository<VaccineDefinition> _definitionsRead;
    private readonly IRepository<VaccineDefinition> _definitionsWrite;
    private readonly TimeProvider _timeProvider;

    public CreateVaccineDefinitionCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Species> species,
        IReadRepository<VaccineDefinition> definitionsRead,
        IRepository<VaccineDefinition> definitionsWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _species = species;
        _definitionsRead = definitionsRead;
        _definitionsWrite = definitionsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateVaccineDefinitionCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (request.SpeciesId is { } sid)
        {
            var species = await _species.FirstOrDefaultAsync(new SpeciesByIdSpec(sid), ct);
            if (species is null)
                return Result<Guid>.Failure("VaccineDefinitions.InvalidSpecies", "Geçersiz tür (species) seçildi.");
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var dup = await _definitionsRead.CountAsync(
            new VaccineDefinitionTenantCodeExistsSpec(tenantId, normalizedCode, excludeId: null),
            ct);
        if (dup > 0)
            return Result<Guid>.Failure("VaccineDefinitions.DuplicateCode", "Bu kiracıda aynı kod zaten kullanılıyor.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var entity = VaccineDefinition.CreateTenant(
            tenantId,
            request.Code,
            request.Name,
            utcNow,
            request.SpeciesId,
            request.Description,
            request.DefaultNextDueDays,
            isCore: false);

        await _definitionsWrite.AddAsync(entity, ct);
        await _definitionsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }
}
