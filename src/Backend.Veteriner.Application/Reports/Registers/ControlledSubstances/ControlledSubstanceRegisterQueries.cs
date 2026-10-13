using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;

/// <summary>Narkotik ve psikotropik ürünler stok ve sarf defteri (sayfalı). <c>IncludeVoided</c> kullanılmaz: geri alma kendi satırıdır.</summary>
public sealed record GetControlledSubstanceRegisterQuery(RegisterQuery Query, int Page, int PageSize)
    : IRequest<Result<RegisterReadResult<ControlledSubstanceRegisterItemDto>>>;

public sealed record ExportControlledSubstanceRegisterQuery(RegisterQuery Query, RegisterExportFormat Format)
    : IRequest<Result<RegisterExportResult>>;

public sealed class GetControlledSubstanceRegisterQueryHandler
    : IRequestHandler<GetControlledSubstanceRegisterQuery, Result<RegisterReadResult<ControlledSubstanceRegisterItemDto>>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<ControlledSubstanceRegisterItemDto> _reader;

    public GetControlledSubstanceRegisterQueryHandler(
        RegisterReportRunner runner,
        IRegisterReader<ControlledSubstanceRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterReadResult<ControlledSubstanceRegisterItemDto>>> Handle(
        GetControlledSubstanceRegisterQuery request,
        CancellationToken ct)
        => _runner.ReadPageAsync(_reader, request.Query, request.Page, request.PageSize, ct);
}

public sealed class ExportControlledSubstanceRegisterQueryHandler
    : IRequestHandler<ExportControlledSubstanceRegisterQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<ControlledSubstanceRegisterItemDto> _reader;

    public ExportControlledSubstanceRegisterQueryHandler(
        RegisterReportRunner runner,
        IRegisterReader<ControlledSubstanceRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterExportResult>> Handle(ExportControlledSubstanceRegisterQuery request, CancellationToken ct)
        => _runner.ExportAsync(_reader, request.Query, request.Format, ControlledSubstanceRegisterColumns.Export, ct);
}
