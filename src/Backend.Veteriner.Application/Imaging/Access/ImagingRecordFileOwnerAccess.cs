using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Files.Access;
using Backend.Veteriner.Application.Imaging.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imaging.Access;

/// <summary>Görüntüleme kaydı dosyaları: okuma kapsamı ve dosya kabulü (iptal, kesinleşme kilidi).</summary>
internal sealed class ImagingRecordFileOwnerAccess(
    IClinicContext clinicContext,
    IClinicReadScopeResolver clinicScopeResolver,
    IReadRepository<ImagingRecord> records,
    IReadRepository<Examination> examinations) : IStoredFileOwnerAccess
{
    public StoredFileOwnerType OwnerType => StoredFileOwnerType.ImagingRecord;

    public async Task<Result<Guid>> ResolveForReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct)
    {
        var record = await ImagingRecordReadScope.FindReadableAsync(
            records, clinicContext, clinicScopeResolver, tenantId, ownerId, ct);
        return record is null ? NotFound() : Result<Guid>.Success(record.ClinicId);
    }

    public async Task<Result<Guid>> ResolveForAttachAsync(Guid tenantId, Guid ownerId, CancellationToken ct)
    {
        var record = await records.FirstOrDefaultAsync(new ImagingRecordByIdSpec(tenantId, ownerId), ct);
        if (record is null || (clinicContext.ClinicId is { } contextClinicId && record.ClinicId != contextClinicId))
            return NotFound();

        var scope = await clinicScopeResolver.ResolveAsync(tenantId, record.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<Guid>.Failure(scope.Error);

        var accepts = record.EnsureAcceptsFiles();
        if (!accepts.IsSuccess)
            return Result<Guid>.Failure(accepts.Error);

        var open = await ExaminationLinkedRecordLock.EnsureImagingEditableAsync(
            examinations, tenantId, record, record.ExaminationId, ct);
        return open.IsSuccess ? Result<Guid>.Success(record.ClinicId) : Result<Guid>.Failure(open.Error);
    }

    private static Result<Guid> NotFound()
        => Result<Guid>.Failure(ImagingRecordReadScope.NotFoundCode, ImagingRecordReadScope.NotFoundMessage);
}

/// <summary>
/// Görüntüleme kaydı okuma kapsamı (tek kural): kiracı, aktif klinik bağlamı ve klinik ataması; kapsam dışı kayıt yokmuş gibi
/// davranır (IDOR, Lab detay deseni).
/// </summary>
internal static class ImagingRecordReadScope
{
    public const string NotFoundCode = "ImagingRecords.NotFound";
    public const string NotFoundMessage = "Görüntüleme kaydı bulunamadı.";

    public static async Task<ImagingRecord?> FindReadableAsync(
        IReadRepository<ImagingRecord> records,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        Guid tenantId,
        Guid id,
        CancellationToken ct)
    {
        var record = await records.FirstOrDefaultAsync(new ImagingRecordByIdSpec(tenantId, id), ct);
        if (record is null || (clinicContext.ClinicId is { } contextClinicId && record.ClinicId != contextClinicId))
            return null;

        var scope = await clinicScopeResolver.ResolveAsync(tenantId, record.ClinicId, ct);
        return scope.IsSuccess ? record : null;
    }
}
