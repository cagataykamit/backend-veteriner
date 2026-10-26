using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.GetOutstandingBalancesReport;

public sealed class GetOutstandingBalancesReportQueryHandler
    : IRequestHandler<GetOutstandingBalancesReportQuery, Result<OutstandingBalancesReportResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IOutstandingBalancesReportReader _reader;

    public GetOutstandingBalancesReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IOutstandingBalancesReportReader reader)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _reader = reader;
    }

    public async ValueTask<Result<OutstandingBalancesReportResultDto>> Handle(
        GetOutstandingBalancesReportQuery request,
        CancellationToken ct)
    {
        var filter = await OutstandingBalancesReportFilter.ResolveAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, request.ClinicId, request.MinAmount, request.Search, ct);
        if (!filter.IsSuccess)
            return Result<OutstandingBalancesReportResultDto>.Failure(filter.Error);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, OutstandingBalancesReportConstants.MaxPageSize);

        var read = await _reader.ReadAsync(filter.Value!.ToReadRequest((page - 1) * pageSize, pageSize), ct);

        return Result<OutstandingBalancesReportResultDto>.Success(
            new OutstandingBalancesReportResultDto(Charge.DefaultCurrency, read.TotalCount, read.TotalOutstandingAmount, read.Items));
    }
}
