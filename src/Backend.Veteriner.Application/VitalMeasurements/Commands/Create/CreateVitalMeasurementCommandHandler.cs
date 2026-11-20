using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.VitalMeasurements;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Create;

public sealed class CreateVitalMeasurementCommandHandler : IRequestHandler<CreateVitalMeasurementCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IRepository<VitalMeasurement> _measurements;
    private readonly TimeProvider _timeProvider;

    public CreateVitalMeasurementCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Examination> examinations,
        IRepository<VitalMeasurement> measurements,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _hospitalizations = hospitalizations;
        _examinations = examinations;
        _measurements = measurements;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateVitalMeasurementCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için vital ölçümü kaydedilemez.");

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<Guid>.Failure(ClinicContextRule.Mismatch("VitalMeasurements"));

        var clinicId = _clinicContext.ClinicId ?? request.ClinicId;
        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<Guid>.Failure(clinicAccess.Error);

        var values = VitalSigns.Create(request.TemperatureC, request.HeartRateBpm, request.RespiratoryRatePerMin, request.WeightKg);
        if (!values.IsSuccess)
            return Result<Guid>.Failure(values.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var measuredAtUtc = VitalMeasuredAtWindow.ToUtc(request.MeasuredAtUtc);
        var window = VitalMeasuredAtWindow.Validate(measuredAtUtc, utcNow);
        if (!window.IsSuccess)
            return Result<Guid>.Failure(window.Error);

        if (await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct) is null)
            return Fail("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        if (await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct) is null)
            return Fail("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        if (request.HospitalizationId is { } hospitalizationId)
        {
            var hospitalization = await _hospitalizations.FirstOrDefaultAsync(
                new HospitalizationByIdSpec(tenantId, hospitalizationId), ct);
            if (hospitalization is null)
                return Fail("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

            var link = VitalMeasurementLinkRules.EnsureHospitalizationLink(hospitalization, clinicId, request.PetId);
            if (!link.IsSuccess)
                return Result<Guid>.Failure(link.Error);

            var within = VitalMeasurementLinkRules.EnsureWithinHospitalization(hospitalization, measuredAtUtc);
            if (!within.IsSuccess)
                return Result<Guid>.Failure(within.Error);
        }

        var addedAfterFinalization = false;
        if (request.ExaminationId is { } examinationId)
        {
            var examination = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examinationId), ct);
            if (examination is null)
                return Fail("Examinations.NotFound", "Muayene bulunamadı veya kiracıya ait değil.");

            var link = VitalMeasurementLinkRules.EnsureExaminationLink(examination, clinicId, request.PetId);
            if (!link.IsSuccess)
                return Result<Guid>.Failure(link.Error);

            // Kesinleşmiş muayene yeni ölçümü reddetmez (ADR-043 AK-6): gerekçeli sonradan ekleme olarak yazılır.
            addedAfterFinalization = examination.IsFinalized;
        }

        var created = VitalMeasurement.Create(
            tenantId,
            clinicId,
            request.PetId,
            request.HospitalizationId,
            request.ExaminationId,
            measuredAtUtc,
            values.Value!,
            request.Note,
            userId,
            utcNow);
        if (!created.IsSuccess)
            return Result<Guid>.Failure(created.Error);

        var measurement = created.Value!;
        if (addedAfterFinalization)
        {
            var late = measurement.MarkAddedAfterFinalization(request.LateEntryReason);
            if (!late.IsSuccess)
                return Result<Guid>.Failure(late.Error);
        }

        await _measurements.AddAsync(measurement, ct);
        await _measurements.SaveChangesAsync(ct);
        return Result<Guid>.Success(measurement.Id);
    }

    private static Result<Guid> Fail(string code, string message) => Result<Guid>.Failure(code, message);
}
