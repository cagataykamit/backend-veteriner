using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Clinics.Commands.Create;

public sealed class CreateClinicCommandHandler : IRequestHandler<CreateClinicCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IRepository<Clinic> _clinicsWrite;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashRegister> _cashRegisters;
    private readonly TimeProvider _timeProvider;

    public CreateClinicCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinicsRead,
        IRepository<Clinic> clinicsWrite,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashRegister> cashRegisters,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _clinicsRead = clinicsRead;
        _clinicsWrite = clinicsWrite;
        _planFeatures = planFeatures;
        _cashRegisters = cashRegisters;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateClinicCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için klinik oluşturulamaz.");

        var planAllows = await _planFeatures.EnsureCanAddActiveClinicAsync(tenantId, ct);
        if (!planAllows.IsSuccess)
            return Result<Guid>.Failure(planAllows.Error);

        var nameKey = request.Name.Trim().ToLowerInvariant();
        var duplicate = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByTenantAndNameCaseInsensitiveSpec(tenantId, nameKey), ct);
        if (duplicate is not null)
            return Result<Guid>.Failure(
                "Clinics.DuplicateName",
                "Bu kiracı altında aynı isimde bir klinik zaten var.");

        var clinic = new Clinic(
            tenantId,
            request.Name,
            request.City,
            request.Phone,
            request.Email,
            request.Address,
            request.Description,
            request.District);
        await _clinicsWrite.AddAsync(clinic, ct);

        // Her klinikte silinemeyen Ana kasa (TIER-008); klinikle aynı SaveChanges.
        await _cashRegisters.AddAsync(CashRegister.CreateMain(tenantId, clinic.Id, _timeProvider.GetUtcNow().UtcDateTime), ct);
        await _clinicsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(clinic.Id);
    }
}
