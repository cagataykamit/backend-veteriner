using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Files;
using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Application.Imaging.Specs;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imaging.Commands.Cancel;

/// <summary>Gerekçeli iptal (silme yok, ASAMA6 D10); kesinleşmiş muayeneye bağlı kayıtta da serbesttir (ADR-013).</summary>
public sealed class CancelImagingRecordCommandHandler
    : IRequestHandler<CancelImagingRecordCommand, Result<ImagingRecordWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ImagingRecord> _records;
    private readonly StoredFileAccessCloser _fileCloser;
    private readonly TimeProvider _timeProvider;

    public CancelImagingRecordCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ImagingRecord> records,
        StoredFileAccessCloser fileCloser,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _records = records;
        _fileCloser = fileCloser;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ImagingRecordWriteResultDto>> Handle(CancelImagingRecordCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

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

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cancelled = existing.Cancel(userId, request.Reason, now);
        if (!cancelled.IsSuccess)
            return Result<ImagingRecordWriteResultDto>.Failure(cancelled.Error);

        // Dosyalar izlenen varlıklar olarak işaretlenir; kayıt güncellemesi tek SaveChanges ile hepsini yazar.
        await _fileCloser.MarkOwnerFilesDeletedAsync(tenantId, StoredFileOwnerType.ImagingRecord, existing.Id, userId, now, ct);
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
