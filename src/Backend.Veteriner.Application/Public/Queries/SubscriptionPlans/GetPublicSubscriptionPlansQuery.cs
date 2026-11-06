using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.SalesPartners;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Public.Queries.SubscriptionPlans;

/// <summary>
/// Herkese açık plan kataloğu (WEB-007): kullanıcı sınırı <see cref="SubscriptionPlanCatalog"/>, fiyat checkout'un okuduğu
/// <see cref="SubscriptionPlanPrices"/> kuralından; site ile çekilen tutar ayrışmaz.
/// </summary>
public sealed record GetPublicSubscriptionPlansQuery : IRequest<PublicSubscriptionPlanCatalogDto>;

public sealed class GetPublicSubscriptionPlansQueryHandler
    : IRequestHandler<GetPublicSubscriptionPlansQuery, PublicSubscriptionPlanCatalogDto>
{
    private readonly IOptions<BillingOptions> _billingOptions;

    public GetPublicSubscriptionPlansQueryHandler(IOptions<BillingOptions> billingOptions) => _billingOptions = billingOptions;

    public ValueTask<PublicSubscriptionPlanCatalogDto> Handle(GetPublicSubscriptionPlansQuery request, CancellationToken ct)
    {
        var options = _billingOptions.Value;
        var plans = SubscriptionPlanCatalog.All
            .Select(p => new PublicSubscriptionPlanDto(
                SubscriptionPlanCatalog.ToApiCode(p.Code),
                p.Name,
                p.MaxUsers,
                SubscriptionPlanPrices.PeriodPriceMinorOrNull(options, p.Code, BillingPeriodKind.Monthly),
                SubscriptionPlanPrices.PeriodPriceMinorOrNull(options, p.Code, BillingPeriodKind.Yearly)))
            .ToList();

        return ValueTask.FromResult(new PublicSubscriptionPlanCatalogDto(
            SubscriptionPlanPrices.Currency(options),
            SubscriptionPlanPrices.VatIncluded,
            SubscriptionPricing.YearlyBilledMonths,
            plans));
    }
}
