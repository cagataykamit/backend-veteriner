using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Visits.Access;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using Mediator;

namespace Backend.Veteriner.Application.CareInstructions.Commands.Create;

public sealed class CreateCareInstructionCommandHandler : IRequestHandler<CreateCareInstructionCommand, Result<Guid>>
{
    private const string Prefix = "CareInstructions";

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Visit> _visits;
    private readonly IRepository<CareInstruction> _careInstructionsWrite;
    private readonly TimeProvider _timeProvider;

    public CreateCareInstructionCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Examination> examinations,
        IReadRepository<Visit> visits,
        IRepository<CareInstruction> careInstructionsWrite,
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
        _visits = visits;
        _careInstructionsWrite = careInstructionsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateCareInstructionCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<Guid>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için bakım talimatı oluşturulamaz.");
        }

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<Guid>.Failure(
                $"{Prefix}.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var clinicId = _clinicContext.ClinicId ?? request.ClinicId;
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!scope.IsSuccess)
            return Result<Guid>.Failure(scope.Error);

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        Hospitalization? hospitalization = null;
        if (request.HospitalizationId is { } hospitalizationId)
        {
            hospitalization = await _hospitalizations.FirstOrDefaultAsync(
                new HospitalizationByIdSpec(tenantId, hospitalizationId), ct);
            if (hospitalization is null)
                return Result<Guid>.Failure("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

            if (hospitalization.ClinicId != clinicId)
            {
                return Result<Guid>.Failure(
                    $"{Prefix}.HospitalizationClinicMismatch",
                    "Yatış kaydının kliniği talimat kliniği ile uyuşmuyor.");
            }

            if (hospitalization.PetId != request.PetId)
            {
                return Result<Guid>.Failure(
                    $"{Prefix}.HospitalizationPetMismatch",
                    "Yatış kaydının hayvanı talimat hayvanı ile uyuşmuyor.");
            }

            if (request.ExaminationId.HasValue && hospitalization.ExaminationId.HasValue
                && request.ExaminationId != hospitalization.ExaminationId)
            {
                return Result<Guid>.Failure(
                    $"{Prefix}.HospitalizationExaminationMismatch",
                    "Yatış kaydının muayenesi ile istekteki muayene uyuşmuyor.");
            }

            if (request.VisitId.HasValue && hospitalization.VisitId.HasValue
                && request.VisitId != hospitalization.VisitId)
            {
                return Result<Guid>.Failure(
                    $"{Prefix}.VisitMismatch",
                    "Yatış kaydının geliş kaydı ile istekteki geliş kaydı uyuşmuyor.");
            }
        }

        var examinationId = request.ExaminationId ?? hospitalization?.ExaminationId;
        Examination? exam = null;
        if (examinationId is { } examId)
        {
            exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examId), ct);
            if (exam is null)
                return Result<Guid>.Failure("Examinations.NotFound", "Muayene bulunamadı veya kiracıya ait değil.");

            if (exam.ClinicId != clinicId)
            {
                return Result<Guid>.Failure(
                    $"{Prefix}.ExaminationClinicMismatch",
                    "Muayenenin kliniği talimat kliniği ile uyuşmuyor.");
            }

            if (exam.PetId != request.PetId)
            {
                return Result<Guid>.Failure(
                    $"{Prefix}.ExaminationPetMismatch",
                    "Muayenenin hayvanı talimat hayvanı ile uyuşmuyor.");
            }
        }

        var visit = await LinkedRecordVisitResolver.ResolveAsync(
            _visits, tenantId, clinicId, request.PetId, exam, request.VisitId ?? hospitalization?.VisitId, Prefix, "talimat", ct);
        if (!visit.IsSuccess)
            return Result<Guid>.Failure(visit.Error);

        CareInstruction entity;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            entity = new CareInstruction(
                tenantId,
                clinicId,
                request.PetId,
                request.HospitalizationId,
                examinationId,
                visit.Value,
                userId,
                request.Text,
                utcNow);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure($"{Prefix}.Validation", ex.Message);
        }

        await _careInstructionsWrite.AddAsync(entity, ct);
        await _careInstructionsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }
}
