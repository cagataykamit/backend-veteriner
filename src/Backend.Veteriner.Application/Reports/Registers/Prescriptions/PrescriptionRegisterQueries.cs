using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Registers.Prescriptions;

/// <summary>Reçete defteri (sayfalı; sayfa ve sayım reçete bazlı). <c>From</c>/<c>To</c>: İstanbul takvim günleri, ikisi dahil.</summary>
public sealed record GetPrescriptionRegisterQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId,
    int Page,
    int PageSize) : IRequest<Result<RegisterReadResult<PrescriptionRegisterEntryDto>>>;

public sealed record ExportPrescriptionRegisterQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId) : IRequest<Result<RegisterExportResult>>;

public sealed record ExportPrescriptionRegisterXlsxQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId) : IRequest<Result<RegisterExportResult>>;

public sealed record ExportPrescriptionRegisterPdfQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId) : IRequest<Result<RegisterExportResult>>;

public sealed class GetPrescriptionRegisterQueryHandler
    : IRequestHandler<GetPrescriptionRegisterQuery, Result<RegisterReadResult<PrescriptionRegisterEntryDto>>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<PrescriptionRegisterEntryDto> _reader;

    public GetPrescriptionRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<PrescriptionRegisterEntryDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public ValueTask<Result<RegisterReadResult<PrescriptionRegisterEntryDto>>> Handle(
        GetPrescriptionRegisterQuery request,
        CancellationToken ct)
        => new(_runner.ReadPageAsync(
            _reader,
            new RegisterQuery(request.From, request.To, request.ClinicId, IncludeVoided: false),
            request.Page,
            request.PageSize,
            ct));
}

public sealed class ExportPrescriptionRegisterQueryHandler
    : IRequestHandler<ExportPrescriptionRegisterQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<PrescriptionRegisterEntryDto> _reader;
    private readonly TimeProvider _timeProvider;

    public ExportPrescriptionRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<PrescriptionRegisterEntryDto> reader, TimeProvider timeProvider)
    {
        _runner = runner;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(ExportPrescriptionRegisterQuery request, CancellationToken ct)
    {
        var loaded = await _runner.LoadForExportAsync(
            _reader, new RegisterQuery(request.From, request.To, request.ClinicId, IncludeVoided: false), RegisterExportFormat.Csv, ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        return Result<RegisterExportResult>.Success(new RegisterExportResult(
            RegisterCsvWriter.WriteUtf8Bom(PrescriptionRegisterColumns.All, loaded.Value!),
            $"recete-defteri-{RegisterReportRunner.FileDateStamp(_timeProvider.GetUtcNow().UtcDateTime)}.csv"));
    }
}

public sealed class ExportPrescriptionRegisterXlsxQueryHandler
    : IRequestHandler<ExportPrescriptionRegisterXlsxQuery, Result<RegisterExportResult>>
{
    private const string SheetName = "Recete Defteri";

    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<PrescriptionRegisterEntryDto> _reader;
    private readonly TimeProvider _timeProvider;

    public ExportPrescriptionRegisterXlsxQueryHandler(RegisterReportRunner runner, IRegisterReader<PrescriptionRegisterEntryDto> reader, TimeProvider timeProvider)
    {
        _runner = runner;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(ExportPrescriptionRegisterXlsxQuery request, CancellationToken ct)
    {
        var loaded = await _runner.LoadForExportAsync(
            _reader, new RegisterQuery(request.From, request.To, request.ClinicId, IncludeVoided: false), RegisterExportFormat.Xlsx, ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        return Result<RegisterExportResult>.Success(new RegisterExportResult(
            RegisterXlsxWriter.WriteWorkbook(SheetName, PrescriptionRegisterColumns.All, loaded.Value!),
            $"recete-defteri-{RegisterReportRunner.FileDateStamp(_timeProvider.GetUtcNow().UtcDateTime)}.xlsx"));
    }
}

public sealed class ExportPrescriptionRegisterPdfQueryHandler
    : IRequestHandler<ExportPrescriptionRegisterPdfQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<PrescriptionRegisterEntryDto> _reader;

    public ExportPrescriptionRegisterPdfQueryHandler(RegisterReportRunner runner, IRegisterReader<PrescriptionRegisterEntryDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public ValueTask<Result<RegisterExportResult>> Handle(ExportPrescriptionRegisterPdfQuery request, CancellationToken ct)
        => new(_runner.ExportAsync(
            _reader,
            new RegisterQuery(request.From, request.To, request.ClinicId, IncludeVoided: false),
            RegisterExportFormat.Pdf,
            PrescriptionRegisterColumns.Export,
            ct));
}
