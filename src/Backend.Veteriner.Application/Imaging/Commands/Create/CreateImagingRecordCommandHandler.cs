using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imaging.Commands.Create;

/// <summary>
/// Görüntüleme kaydı oluşturur. Kesinleşmiş muayeneye bağlamak yeni (ek) kayıt sayılır ve serbesttir (ADR-013 uyumu);
/// kayıt <c>AddedAfterExaminationFinalization</c> işaretini alır.
/// </summary>
public sealed class CreateImagingRecordCommandHandler : IRequestHandler<CreateImagingRecordCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IRepository<ImagingRecord> _records;
    private readonly TimeProvider _timeProvider;

    public CreateImagingRecordCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IRepository<ImagingRecord> records,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _records = records;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateImagingRecordCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Result<Guid>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<Guid>.Failure(
                "ImagingRecords.ClinicContextMismatch",
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

        var examinationFinalized = false;
        if (request.ExaminationId is { } examId)
        {
            var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examId), ct);
            var link = ImagingExaminationLink.Validate(exam, clinicId, request.PetId);
            if (!link.IsSuccess)
                return Result<Guid>.Failure(link.Error);
            examinationFinalized = exam!.IsFinalized;
        }

        var created = ImagingRecord.Create(
            tenantId,
            clinicId,
            request.PetId,
            request.ExaminationId,
            examinationFinalized,
            request.PerformedAtUtc,
            request.Modality,
            request.Description,
            request.ClinicalNote,
            userId,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<Guid>.Failure(created.Error);

        await _records.AddAsync(created.Value!, ct);
        await _records.SaveChangesAsync(ct);
        return Result<Guid>.Success(created.Value!.Id);
    }
}
