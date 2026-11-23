using Backend.Veteriner.Application.Ai;
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
/// <see cref="SubscriptionPlanPrices"/> kuralından; site ile çekilen tutar ayrışmaz. Aylık yapay zekâ kredisi kotanın okuduğu
/// <see cref="AiOptions.MonthlyCreditsFor"/> kuralından (ADR-044 madde 9: değer tek yerde, sitede kopyalanmaz).
/// </summary>
public sealed record GetPublicSubscriptionPlansQuery : IRequest<PublicSubscriptionPlanCatalogDto>;

public sealed class GetPublicSubscriptionPlansQueryHandler
    : IRequestHandler<GetPublicSubscriptionPlansQuery, PublicSubscriptionPlanCatalogDto>
{
    private readonly IOptions<BillingOptions> _billingOptions;
    private readonly IOptions<AiOptions> _aiOptions;

    public GetPublicSubscriptionPlansQueryHandler(IOptions<BillingOptions> billingOptions, IOptions<AiOptions> aiOptions)
    {
        _billingOptions = billingOptions;
        _aiOptions = aiOptions;
    }

    public ValueTask<PublicSubscriptionPlanCatalogDto> Handle(GetPublicSubscriptionPlansQuery request, CancellationToken ct)
    {
        var options = _billingOptions.Value;
        var ai = _aiOptions.Value;
        var plans = SubscriptionPlanCatalog.All
            .Select(p =>
            {
                var apiCode = SubscriptionPlanCatalog.ToApiCode(p.Code);

                return new PublicSubscriptionPlanDto(
                    apiCode,
                    p.Name,
                    p.MaxUsers,
                    SubscriptionPlanPrices.PeriodPriceMinorOrNull(options, p.Code, BillingPeriodKind.Monthly),
                    SubscriptionPlanPrices.PeriodPriceMinorOrNull(options, p.Code, BillingPeriodKind.Yearly),
                    ai.MonthlyCreditsFor(apiCode));
            })
            .ToList();

        return ValueTask.FromResult(new PublicSubscriptionPlanCatalogDto(
            SubscriptionPlanPrices.Currency(options),
            SubscriptionPlanPrices.VatIncluded,
            SubscriptionPricing.YearlyBilledMonths,
            plans));
    }
}
