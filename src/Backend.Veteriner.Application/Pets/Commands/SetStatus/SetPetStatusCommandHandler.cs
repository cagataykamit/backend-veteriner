using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Pets.Commands.SetStatus;

public sealed class SetPetStatusCommandHandler : IRequestHandler<SetPetStatusCommand, Result<PetStatusDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Pet> _pets;
    private readonly TimeProvider _timeProvider;

    public SetPetStatusCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IRepository<Pet> pets,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _pets = pets;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PetStatusDto>> Handle(SetPetStatusCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PetStatusDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<PetStatusDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<PetStatusDto>.Failure("Tenants.TenantInactive", "Pasif kiraci icin hayvan kaydi guncellenemez.");

        var pet = await _pets.FirstOrDefaultAsync(new PetForUpdateByIdSpec(tenantId, request.Id), ct);
        if (pet is null)
            return Result<PetStatusDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Result<PetStatusDto>.Failure("Pets.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        var change = pet.SetStatus(request.Status, request.DeceasedOn, today);
        if (!change.IsSuccess)
            return Result<PetStatusDto>.Failure(change.Error);

        pet.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _pets.UpdateAsync(pet, ct);
            await _pets.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PetStatusDto>.Failure(
                "Pets.ConcurrencyConflict",
                "Hayvan kaydı başka biri tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }

        return Result<PetStatusDto>.Success(PetStatusDto.From(pet));
    }
}
