using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>
/// Defter sorgularının ortak akışı: doğrulama + klinik kapsamı, sayfalı okuma ve dışa aktarma için tüm satırları
/// (tavanlı) yükleme. İki defter ve üç çıktı aynı okuyucu çağrısından geçer.
/// </summary>
public sealed class RegisterReportRunner
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;

    public RegisterReportRunner(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
    }

    public async Task<Result<RegisterReadResult<TRow>>> ReadPageAsync<TRow>(
        IRegisterReader<TRow> reader,
        RegisterQuery query,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var filter = await RegisterReportFilter.ResolveAsync(_tenantContext, _clinicContext, _scopeResolver, query, ct);
        if (!filter.IsSuccess)
            return Result<RegisterReadResult<TRow>>.Failure(filter.Error);

        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, RegisterReportConstants.MaxPageSize);

        return Result<RegisterReadResult<TRow>>.Success(
            await reader.ReadAsync(filter.Value!.ToReadRequest((safePage - 1) * safeSize, safeSize), ct));
    }

    public async Task<Result<IReadOnlyList<TRow>>> LoadForExportAsync<TRow>(
        IRegisterReader<TRow> reader,
        RegisterQuery query,
        CancellationToken ct)
    {
        var filter = await RegisterReportFilter.ResolveAsync(_tenantContext, _clinicContext, _scopeResolver, query, ct);
        if (!filter.IsSuccess)
            return Result<IReadOnlyList<TRow>>.Failure(filter.Error);

        var read = await reader.ReadAsync(filter.Value!.ToReadRequest(0, RegisterReportConstants.MaxExportRows), ct);

        if (read.TotalCount > RegisterReportConstants.MaxExportRows)
        {
            return Result<IReadOnlyList<TRow>>.Failure(
                "Registers.ExportTooManyRows",
                $"Dışa aktarma en fazla {RegisterReportConstants.MaxExportRows} satır destekler; filtreyi daraltın.");
        }

        return Result<IReadOnlyList<TRow>>.Success(read.Items);
    }

    /// <summary>Tüm eşleşen satırları (tavanlı) yükler ve seçilen biçimde dosyaya yazar; ekranla aynı okuyucu çağrısıdır.</summary>
    public async Task<Result<RegisterExportResult>> ExportAsync<TRow>(
        IRegisterReader<TRow> reader,
        RegisterQuery query,
        RegisterExportFormat format,
        RegisterExportSpec<TRow> spec,
        CancellationToken ct)
    {
        var loaded = await LoadForExportAsync(reader, query, ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        return Result<RegisterExportResult>.Success(BuildFile(spec, loaded.Value!, format));
    }

    /// <summary>Hazır satırlardan seçilen biçimde dosya üretir (dosya adı: önek + İstanbul tarihi).</summary>
    public static RegisterExportResult BuildFile<TRow>(RegisterExportSpec<TRow> spec, IReadOnlyList<TRow> rows, RegisterExportFormat format)
    {
        var stamp = FileDateStamp(DateTime.UtcNow);
        return format == RegisterExportFormat.Csv
            ? new RegisterExportResult(RegisterCsvWriter.WriteUtf8Bom(spec.Columns, rows), $"{spec.FileBaseName}-{stamp}.csv")
            : new RegisterExportResult(RegisterXlsxWriter.WriteWorkbook(spec.SheetName, spec.Columns, rows), $"{spec.FileBaseName}-{stamp}.xlsx");
    }

    /// <summary>Dosya adındaki İstanbul takvim günü.</summary>
    public static string FileDateStamp(DateTime utcNow)
        => OperationDayBounds.ToLocalDate(utcNow).ToString("yyyyMMdd");
}
