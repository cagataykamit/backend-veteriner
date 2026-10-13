using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Registers.Vaccines;

/// <summary>Aşı kayıt defteri (sayfalı). <c>Query.IncludeVoided</c>: geri alınan girişler de gelsin.</summary>
public sealed record GetVaccineRegisterQuery(RegisterQuery Query, int Page, int PageSize)
    : IRequest<Result<RegisterReadResult<VaccineRegisterItemDto>>>;

public sealed record ExportVaccineRegisterQuery(RegisterQuery Query, RegisterExportFormat Format)
    : IRequest<Result<RegisterExportResult>>;

public sealed class GetVaccineRegisterQueryHandler
    : IRequestHandler<GetVaccineRegisterQuery, Result<RegisterReadResult<VaccineRegisterItemDto>>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<VaccineRegisterItemDto> _reader;

    public GetVaccineRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<VaccineRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterReadResult<VaccineRegisterItemDto>>> Handle(GetVaccineRegisterQuery request, CancellationToken ct)
        => _runner.ReadPageAsync(_reader, request.Query, request.Page, request.PageSize, ct);
}

public sealed class ExportVaccineRegisterQueryHandler : IRequestHandler<ExportVaccineRegisterQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<VaccineRegisterItemDto> _reader;

    public ExportVaccineRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<VaccineRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterExportResult>> Handle(ExportVaccineRegisterQuery request, CancellationToken ct)
        => _runner.ExportAsync(_reader, request.Query, request.Format, VaccineRegisterColumns.Export, ct);
}
