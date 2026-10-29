using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Registers.Examinations;

/// <summary>Muayene defteri (sayfalı). <c>From</c>/<c>To</c>: İstanbul takvim günleri, ikisi dahil.</summary>
public sealed record GetExaminationRegisterQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId,
    bool IncludeVoided,
    int Page,
    int PageSize) : IRequest<Result<RegisterReadResult<ExaminationRegisterItemDto>>>;

public sealed record ExportExaminationRegisterQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId,
    bool IncludeVoided) : IRequest<Result<RegisterExportResult>>;

public sealed record ExportExaminationRegisterXlsxQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId,
    bool IncludeVoided) : IRequest<Result<RegisterExportResult>>;

public sealed record ExportExaminationRegisterPdfQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId,
    bool IncludeVoided) : IRequest<Result<RegisterExportResult>>;

public sealed class GetExaminationRegisterQueryHandler
    : IRequestHandler<GetExaminationRegisterQuery, Result<RegisterReadResult<ExaminationRegisterItemDto>>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<ExaminationRegisterItemDto> _reader;

    public GetExaminationRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<ExaminationRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public ValueTask<Result<RegisterReadResult<ExaminationRegisterItemDto>>> Handle(
        GetExaminationRegisterQuery request,
        CancellationToken ct)
        => new(_runner.ReadPageAsync(
            _reader,
            new RegisterQuery(request.From, request.To, request.ClinicId, request.IncludeVoided),
            request.Page,
            request.PageSize,
            ct));
}

public sealed class ExportExaminationRegisterQueryHandler
    : IRequestHandler<ExportExaminationRegisterQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<ExaminationRegisterItemDto> _reader;
    private readonly TimeProvider _timeProvider;

    public ExportExaminationRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<ExaminationRegisterItemDto> reader, TimeProvider timeProvider)
    {
        _runner = runner;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(ExportExaminationRegisterQuery request, CancellationToken ct)
    {
        var loaded = await _runner.LoadForExportAsync(
            _reader, new RegisterQuery(request.From, request.To, request.ClinicId, request.IncludeVoided), RegisterExportFormat.Csv, ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        return Result<RegisterExportResult>.Success(new RegisterExportResult(
            RegisterCsvWriter.WriteUtf8Bom(ExaminationRegisterColumns.All, loaded.Value!),
            $"muayene-defteri-{RegisterReportRunner.FileDateStamp(_timeProvider.GetUtcNow().UtcDateTime)}.csv"));
    }
}

public sealed class ExportExaminationRegisterXlsxQueryHandler
    : IRequestHandler<ExportExaminationRegisterXlsxQuery, Result<RegisterExportResult>>
{
    private const string SheetName = "Muayene Defteri";

    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<ExaminationRegisterItemDto> _reader;
    private readonly TimeProvider _timeProvider;

    public ExportExaminationRegisterXlsxQueryHandler(RegisterReportRunner runner, IRegisterReader<ExaminationRegisterItemDto> reader, TimeProvider timeProvider)
    {
        _runner = runner;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(ExportExaminationRegisterXlsxQuery request, CancellationToken ct)
    {
        var loaded = await _runner.LoadForExportAsync(
            _reader, new RegisterQuery(request.From, request.To, request.ClinicId, request.IncludeVoided), RegisterExportFormat.Xlsx, ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        return Result<RegisterExportResult>.Success(new RegisterExportResult(
            RegisterXlsxWriter.WriteWorkbook(SheetName, ExaminationRegisterColumns.All, loaded.Value!),
            $"muayene-defteri-{RegisterReportRunner.FileDateStamp(_timeProvider.GetUtcNow().UtcDateTime)}.xlsx"));
    }
}

public sealed class ExportExaminationRegisterPdfQueryHandler
    : IRequestHandler<ExportExaminationRegisterPdfQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<ExaminationRegisterItemDto> _reader;

    public ExportExaminationRegisterPdfQueryHandler(RegisterReportRunner runner, IRegisterReader<ExaminationRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public ValueTask<Result<RegisterExportResult>> Handle(ExportExaminationRegisterPdfQuery request, CancellationToken ct)
        => new(_runner.ExportAsync(
            _reader,
            new RegisterQuery(request.From, request.To, request.ClinicId, request.IncludeVoided),
            RegisterExportFormat.Pdf,
            ExaminationRegisterColumns.Export,
            ct));
}
