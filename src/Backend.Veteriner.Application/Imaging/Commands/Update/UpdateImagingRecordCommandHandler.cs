using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Application.Imaging.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imaging.Commands.Update;

public sealed class UpdateImagingRecordCommandHandler
    : IRequestHandler<UpdateImagingRecordCommand, Result<ImagingRecordWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IRepository<ImagingRecord> _records;
    private readonly TimeProvider _timeProvider;

    public UpdateImagingRecordCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Examination> examinations,
        IRepository<ImagingRecord> records,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _examinations = examinations;
        _records = records;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ImagingRecordWriteResultDto>> Handle(UpdateImagingRecordCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        var existing = await _records.FirstOrDefaultAsync(new ImagingRecordForUpdateByIdSpec(tenantId, request.Id), ct);
        if (existing is null
            || (_clinicContext.ClinicId is { } contextClinicId && existing.ClinicId != contextClinicId))
        {
            return Fail("ImagingRecords.NotFound", "Görüntüleme kaydı bulunamadı.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, existing.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<ImagingRecordWriteResultDto>.Failure(scope.Error);

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Fail("ImagingRecords.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

        if (existing.Status == ImagingRecordStatus.Cancelled)
            return Fail("ImagingRecords.AlreadyCancelled", "Görüntüleme kaydı iptal edilmiş; değiştirilemez.");

        if (request.ExaminationId is { } examId && examId != existing.ExaminationId)
        {
            var target = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examId), ct);
            var link = ImagingExaminationLink.Validate(target, existing.ClinicId, existing.PetId);
            if (!link.IsSuccess)
                return Result<ImagingRecordWriteResultDto>.Failure(link.Error);
        }

        var open = await ExaminationLinkedRecordLock.EnsureImagingEditableAsync(
            _examinations, tenantId, existing, request.ExaminationId, ct);
        if (!open.IsSuccess)
            return Result<ImagingRecordWriteResultDto>.Failure(open.Error);

        var updated = existing.UpdateDetails(
            request.ExaminationId,
            request.PerformedAtUtc,
            request.Modality,
            request.Description,
            request.ClinicalNote,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!updated.IsSuccess)
            return Result<ImagingRecordWriteResultDto>.Failure(updated.Error);

        existing.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _records.UpdateAsync(existing, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "ImagingRecords.ConcurrencyConflict",
                "Görüntüleme kaydı eşzamanlı olarak güncellendi; sayfayı yenileyip tekrar deneyin.");
        }

        return Result<ImagingRecordWriteResultDto>.Success(
            new ImagingRecordWriteResultDto(existing.Id, ExaminationRowVersion.Encode(existing.RowVersion)));
    }

    private static Result<ImagingRecordWriteResultDto> Fail(string code, string message)
        => Result<ImagingRecordWriteResultDto>.Failure(code, message);
}
