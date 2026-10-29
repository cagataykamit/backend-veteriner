using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Reports;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Preferences.Favorites;

public sealed record ReportFavoriteDto(string ReportKey, DateTime CreatedAtUtc);

/// <summary>Oturumdaki kullanıcının bu kiracıdaki rapor favorileri (eklenme sırasıyla).</summary>
public sealed record GetMyReportFavoritesQuery : IRequest<Result<IReadOnlyList<ReportFavoriteDto>>>;

public sealed class GetMyReportFavoritesQueryHandler
    : IRequestHandler<GetMyReportFavoritesQuery, Result<IReadOnlyList<ReportFavoriteDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<ReportFavorite> _favorites;

    public GetMyReportFavoritesQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<ReportFavorite> favorites)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _favorites = favorites;
    }

    public async ValueTask<Result<IReadOnlyList<ReportFavoriteDto>>> Handle(GetMyReportFavoritesQuery request, CancellationToken ct)
    {
        var scope = ReportPreferenceScope.Resolve(_tenantContext, _clientContext);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<ReportFavoriteDto>>.Failure(scope.Error);

        var (tenantId, userId) = scope.Value;
        var rows = await _favorites.ListAsync(new ReportFavoritesByUserSpec(tenantId, userId), ct);
        return Result<IReadOnlyList<ReportFavoriteDto>>.Success(
            rows.Select(f => new ReportFavoriteDto(f.ReportKey, f.CreatedAtUtc)).ToList());
    }
}
