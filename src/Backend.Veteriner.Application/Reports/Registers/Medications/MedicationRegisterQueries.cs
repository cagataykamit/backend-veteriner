using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Registers.Medications;

/// <summary>İlaç kayıt defteri (sayfalı). <c>Query.IncludeVoided</c>: geri alınan girişler de gelsin.</summary>
public sealed record GetMedicationRegisterQuery(RegisterQuery Query, int Page, int PageSize)
    : IRequest<Result<RegisterReadResult<MedicationRegisterItemDto>>>;

public sealed record ExportMedicationRegisterQuery(RegisterQuery Query, RegisterExportFormat Format)
    : IRequest<Result<RegisterExportResult>>;

public sealed class GetMedicationRegisterQueryHandler
    : IRequestHandler<GetMedicationRegisterQuery, Result<RegisterReadResult<MedicationRegisterItemDto>>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<MedicationRegisterItemDto> _reader;

    public GetMedicationRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<MedicationRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterReadResult<MedicationRegisterItemDto>>> Handle(GetMedicationRegisterQuery request, CancellationToken ct)
        => _runner.ReadPageAsync(_reader, request.Query, request.Page, request.PageSize, ct);
}

public sealed class ExportMedicationRegisterQueryHandler : IRequestHandler<ExportMedicationRegisterQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<MedicationRegisterItemDto> _reader;

    public ExportMedicationRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<MedicationRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterExportResult>> Handle(ExportMedicationRegisterQuery request, CancellationToken ct)
        => _runner.ExportAsync(_reader, request.Query, request.Format, MedicationRegisterColumns.Export, ct);
}
