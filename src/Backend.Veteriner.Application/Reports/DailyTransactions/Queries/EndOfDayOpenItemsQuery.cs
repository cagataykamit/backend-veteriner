using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.DailyTransactions.Queries;

/// <summary>Gün sonu açık işler: seçilen pencere için <see cref="IVisitOpenItemsReader"/> özeti (kural orada, burada kopyalanmaz).</summary>
public sealed record GetEndOfDayOpenItemsQuery(DateOnly From, DateOnly To, Guid? ClinicId)
    : IRequest<Result<EndOfDayOpenItemsDto>>;

public sealed record EndOfDayOpenItemsDto(DateOnly From, DateOnly To, Guid ClinicId, OpenItemsSummaryDto OpenItems);

public sealed class GetEndOfDayOpenItemsQueryHandler : IRequestHandler<GetEndOfDayOpenItemsQuery, Result<EndOfDayOpenItemsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IVisitOpenItemsReader _openItems;

    public GetEndOfDayOpenItemsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICurrentUserPermissionChecker permissions,
        IVisitOpenItemsReader openItems)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _permissions = permissions;
        _openItems = openItems;
    }

    public async Task<Result<EndOfDayOpenItemsDto>> Handle(GetEndOfDayOpenItemsQuery request, CancellationToken ct)
    {
        var filter = await DailyTransactionsReportFilter.ResolveAsync(
            _tenantContext, _clinicContext, _scopeResolver, request.From, request.To, request.ClinicId, null, null, null, ct);
        if (!filter.IsSuccess)
            return Result<EndOfDayOpenItemsDto>.Failure(filter.Error);

        var f = filter.Value!;
        if (f.ClinicId is not { } clinicId)
        {
            return Result<EndOfDayOpenItemsDto>.Failure(
                "EndOfDay.ClinicScopeRequired",
                "Gün sonu açık işleri için klinik belirlenemedi (clinicId veya aktif klinik bağlamı gerekir).");
        }

        // Ücretsiz ziyaret: seçilen pencerede gelenler. Tamamlanmamış geliş: pencerenin sonuna kadar gelip hâlâ açık olanlar.
        var summary = await _openItems.GetSummaryAsync(
            new OpenItemsSummaryRequest(
                OpenItemsScope.For(f.TenantId, clinicId, _permissions),
                f.FromUtc,
                f.ToUtcExclusive,
                f.ToUtcExclusive),
            ct);

        return Result<EndOfDayOpenItemsDto>.Success(new EndOfDayOpenItemsDto(request.From, request.To, clinicId, summary));
    }
}
