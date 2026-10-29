using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Reports;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Preferences.SavedFilters;

public sealed record ReportSavedFilterDto(
    Guid Id,
    string ReportKey,
    string Name,
    IReadOnlyDictionary<string, string> Filters,
    DateTime CreatedAtUtc)
{
    public static ReportSavedFilterDto From(ReportSavedFilter f) => new(f.Id, f.ReportKey, f.Name, f.Filters, f.CreatedAtUtc);
}

/// <summary>Oturumdaki kullanıcının bir rapordaki kayıtlı filtreleri (ada göre).</summary>
public sealed record GetMyReportSavedFiltersQuery(string ReportKey) : IRequest<Result<IReadOnlyList<ReportSavedFilterDto>>>;

public sealed class GetMyReportSavedFiltersQueryValidator : AbstractValidator<GetMyReportSavedFiltersQuery>
{
    public GetMyReportSavedFiltersQueryValidator()
    {
        RuleFor(x => x.ReportKey).Must(ReportKey.IsValid).WithMessage(ReportKey.InvalidMessage);
    }
}

public sealed class GetMyReportSavedFiltersQueryHandler
    : IRequestHandler<GetMyReportSavedFiltersQuery, Result<IReadOnlyList<ReportSavedFilterDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<ReportSavedFilter> _filters;

    public GetMyReportSavedFiltersQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<ReportSavedFilter> filters)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _filters = filters;
    }

    public async ValueTask<Result<IReadOnlyList<ReportSavedFilterDto>>> Handle(GetMyReportSavedFiltersQuery request, CancellationToken ct)
    {
        var scope = ReportPreferenceScope.Resolve(_tenantContext, _clientContext);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<ReportSavedFilterDto>>.Failure(scope.Error);

        var (tenantId, userId) = scope.Value;
        var rows = await _filters.ListAsync(new ReportSavedFiltersByReportSpec(tenantId, userId, request.ReportKey), ct);
        return Result<IReadOnlyList<ReportSavedFilterDto>>.Success(rows.Select(ReportSavedFilterDto.From).ToList());
    }
}
