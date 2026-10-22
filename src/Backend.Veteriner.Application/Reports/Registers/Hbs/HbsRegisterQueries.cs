using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Registers.Hbs;

/// <summary>HBS takibi — reçeteler (sayfalı). <c>Query.HbsStatus</c> verilmezse "Girilmedi"; <c>Query.VeterinarianUserId</c> geliş sorumlu hekimi.</summary>
public sealed record GetHbsPrescriptionRegisterQuery(RegisterQuery Query, int Page, int PageSize)
    : IRequest<Result<RegisterReadResult<HbsPrescriptionRegisterItemDto>>>;

public sealed record ExportHbsPrescriptionRegisterQuery(RegisterQuery Query, RegisterExportFormat Format)
    : IRequest<Result<RegisterExportResult>>;

/// <summary>HBS takibi — aşılar (sayfalı); yalnız takip kapsamındaki aşılar (uygulanmış, içe aktarılmamış).</summary>
public sealed record GetHbsVaccinationRegisterQuery(RegisterQuery Query, int Page, int PageSize)
    : IRequest<Result<RegisterReadResult<HbsVaccinationRegisterItemDto>>>;

public sealed record ExportHbsVaccinationRegisterQuery(RegisterQuery Query, RegisterExportFormat Format)
    : IRequest<Result<RegisterExportResult>>;

public sealed class GetHbsPrescriptionRegisterQueryHandler
    : IRequestHandler<GetHbsPrescriptionRegisterQuery, Result<RegisterReadResult<HbsPrescriptionRegisterItemDto>>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<HbsPrescriptionRegisterItemDto> _reader;

    public GetHbsPrescriptionRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<HbsPrescriptionRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterReadResult<HbsPrescriptionRegisterItemDto>>> Handle(GetHbsPrescriptionRegisterQuery request, CancellationToken ct)
        => _runner.ReadPageAsync(_reader, request.Query, request.Page, request.PageSize, ct);
}

public sealed class ExportHbsPrescriptionRegisterQueryHandler
    : IRequestHandler<ExportHbsPrescriptionRegisterQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<HbsPrescriptionRegisterItemDto> _reader;

    public ExportHbsPrescriptionRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<HbsPrescriptionRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterExportResult>> Handle(ExportHbsPrescriptionRegisterQuery request, CancellationToken ct)
        => _runner.ExportAsync(_reader, request.Query, request.Format, HbsPrescriptionRegisterColumns.Export, ct);
}

public sealed class GetHbsVaccinationRegisterQueryHandler
    : IRequestHandler<GetHbsVaccinationRegisterQuery, Result<RegisterReadResult<HbsVaccinationRegisterItemDto>>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<HbsVaccinationRegisterItemDto> _reader;

    public GetHbsVaccinationRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<HbsVaccinationRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterReadResult<HbsVaccinationRegisterItemDto>>> Handle(GetHbsVaccinationRegisterQuery request, CancellationToken ct)
        => _runner.ReadPageAsync(_reader, request.Query, request.Page, request.PageSize, ct);
}

public sealed class ExportHbsVaccinationRegisterQueryHandler
    : IRequestHandler<ExportHbsVaccinationRegisterQuery, Result<RegisterExportResult>>
{
    private readonly RegisterReportRunner _runner;
    private readonly IRegisterReader<HbsVaccinationRegisterItemDto> _reader;

    public ExportHbsVaccinationRegisterQueryHandler(RegisterReportRunner runner, IRegisterReader<HbsVaccinationRegisterItemDto> reader)
    {
        _runner = runner;
        _reader = reader;
    }

    public Task<Result<RegisterExportResult>> Handle(ExportHbsVaccinationRegisterQuery request, CancellationToken ct)
        => _runner.ExportAsync(_reader, request.Query, request.Format, HbsVaccinationRegisterColumns.Export, ct);
}
