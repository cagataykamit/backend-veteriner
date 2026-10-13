using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Registers.VaccineInfo;

/// <summary>
/// Aşı bilgi raporu (sayfalı). <c>Query.ProductId</c> ve <c>Query.VeterinarianUserId</c> süzgeçleri bu rapora özgüdür;
/// <c>Query.IncludeVoided</c>: iptal edilen uygulamalar da gelsin.
/// </summary>
public sealed record GetVaccineInfoRegisterQuery(RegisterQuery Query, int Page, int PageSize)
    : IRequest<Result<RegisterReadResult<VaccineInfoRegisterItemDto>>>;

public sealed record ExportVaccineInfoRegisterQuery(RegisterQuery Query, RegisterExportFormat Format)
    : IRequest<Result<RegisterExportResult>>;

public sealed class GetVaccineInfoRegisterQueryHandler
    : IRequestHandler<GetVaccineInfoRegisterQuery, Result<RegisterReadResult<VaccineInfoRegisterItemDto>>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<VaccineInfoRegisterItemDto> _reader;

    public GetVaccineInfoRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<VaccineInfoRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterReadResult<VaccineInfoRegisterItemDto>>> Handle(
        GetVaccineInfoRegisterQuery request,
        CancellationToken ct)
        => _runner.ReadPageAsync(_reader, request.Query, request.Page, request.PageSize, ct);
}

public sealed class ExportVaccineInfoRegisterQueryHandler
    : IRequestHandler<ExportVaccineInfoRegisterQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<VaccineInfoRegisterItemDto> _reader;

    public ExportVaccineInfoRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<VaccineInfoRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterExportResult>> Handle(ExportVaccineInfoRegisterQuery request, CancellationToken ct)
        => _runner.ExportAsync(_reader, request.Query, request.Format, VaccineInfoRegisterColumns.Export, ct);
}
