using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Pdf;
using Backend.Veteriner.Application.Tenants;
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
    private readonly ITrialLimitPolicy _trialLimitPolicy;
    private readonly TimeProvider _timeProvider;

    public RegisterReportRunner(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ITrialLimitPolicy trialLimitPolicy,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _trialLimitPolicy = trialLimitPolicy;
        _timeProvider = timeProvider;
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
        RegisterExportFormat format,
        CancellationToken ct)
    {
        var filter = await RegisterReportFilter.ResolveAsync(_tenantContext, _clinicContext, _scopeResolver, query, ct);
        if (!filter.IsSuccess)
            return Result<IReadOnlyList<TRow>>.Failure(filter.Error);

        // Satır tavanı kiracıya ve biçime göre (TRIAL-004, REPORT-007): deneme 1.000, ücretli ReportsSharedLimits.MaxExportRows, PDF en çok MaxPdfExportRows.
        var rowLimit = (await _trialLimitPolicy.GetExportRowLimitAsync(filter.Value!.TenantId, ct)).ForFormat(format);
        var read = await reader.ReadAsync(filter.Value.ToReadRequest(0, rowLimit.MaxRows), ct);

        var withinLimit = rowLimit.Ensure(read.TotalCount, "Registers.ExportTooManyRows");
        if (!withinLimit.IsSuccess)
            return Result<IReadOnlyList<TRow>>.Failure(withinLimit.Error);

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
        var loaded = await LoadForExportAsync(reader, query, format, ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        return Result<RegisterExportResult>.Success(BuildFile(spec, loaded.Value!, format, query.From, query.To, utcNow));
    }

    /// <summary>
    /// Hazır satırlardan seçilen biçimde dosya üretir (dosya adı: önek + İstanbul tarihi). <paramref name="from"/>/<paramref name="to"/>
    /// yalnız PDF başlığındaki dönem içindir.
    /// </summary>
    public static RegisterExportResult BuildFile<TRow>(
        RegisterExportSpec<TRow> spec,
        IReadOnlyList<TRow> rows,
        RegisterExportFormat format,
        DateOnly from,
        DateOnly to,
        DateTime utcNow)
    {
        var stamp = FileDateStamp(utcNow);
        return format switch
        {
            RegisterExportFormat.Csv => new RegisterExportResult(RegisterCsvWriter.WriteUtf8Bom(spec.Columns, rows), $"{spec.FileBaseName}-{stamp}.csv"),
            RegisterExportFormat.Xlsx => new RegisterExportResult(
                RegisterXlsxWriter.WriteWorkbook(spec.SheetName, spec.Columns, rows), $"{spec.FileBaseName}-{stamp}.xlsx"),
            _ => new RegisterExportResult(
                ReportPdfWriter.Write(new ReportPdfHeader(spec.Title, ReportPdfHeader.PeriodText(from, to), utcNow), spec.Columns, rows),
                $"{spec.FileBaseName}-{stamp}.pdf"),
        };
    }

    /// <summary>Dosya adındaki İstanbul takvim günü.</summary>
    public static string FileDateStamp(DateTime utcNow)
        => OperationDayBounds.ToLocalDate(utcNow).ToString("yyyyMMdd");
}
