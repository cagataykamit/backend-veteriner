using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Iyzipay;
using Iyzipay.Model;
using Iyzipay.Request;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace Backend.Veteriner.Infrastructure.Billing;

public sealed class IyzicoBillingCheckoutProvider : IBillingCheckoutProvider
{
    private readonly BillingOptions _billingOptions;
    private readonly ILogger<IyzicoBillingCheckoutProvider> _logger;
    private readonly IUserReadRepository _users;
    private readonly IClientContext _clientContext;

    public IyzicoBillingCheckoutProvider(
        IOptions<BillingOptions> billingOptions,
        ILogger<IyzicoBillingCheckoutProvider> logger,
        IUserReadRepository users,
        IClientContext clientContext)
    {
        _billingOptions = billingOptions.Value;
        _logger = logger;
        _users = users;
        _clientContext = clientContext;
    }

    public BillingProvider Provider => BillingProvider.Iyzico;

    public async Task<Result<CheckoutPrepareResult>> PrepareCheckoutAsync(
        BillingCheckoutSession session,
        string? chargeCurrencyCode,
        long? chargeAmountMinor,
        decimal? prorationRatio,
        CancellationToken ct = default)
    {
        var iyzico = _billingOptions.Iyzico;
        var targetPlan = SubscriptionPlanCatalog.ToApiCode(session.TargetPlanCode);
        var currentPlan = SubscriptionPlanCatalog.ToApiCode(session.CurrentPlanCode);

        if (!IyzicoCheckoutPrerequisites.IsComplete(iyzico))
        {
            return Result<CheckoutPrepareResult>.Failure(
                "Billing.IyzicoConfigurationIncomplete",
                "Iyzico checkout için ApiKey, SecretKey, BaseUrl, CallbackUrl ve PlanPricesTry yapılandırması eksiksiz olmalı.");
        }

        decimal planAmount;
        if (chargeAmountMinor.HasValue && chargeAmountMinor.Value > 0)
        {
            planAmount = chargeAmountMinor.Value / 100m;
        }
        else if (!IyzicoCheckoutPrerequisites.TryGetPlanPrice(iyzico.PlanPricesTry, targetPlan, out planAmount))
        {
            return Result<CheckoutPrepareResult>.Failure(
                "Billing.IyzicoPlanPriceNotConfigured",
                $"Iyzico plan fiyatı bulunamadı: {targetPlan}. Billing:Iyzico:PlanPricesTry içine ekleyin.");
        }

        var buyerEmailResult = await ResolveBuyerEmailAsync(session, ct);
        if (!buyerEmailResult.IsSuccess)
            return Result<CheckoutPrepareResult>.Failure(buyerEmailResult.Error.Code, buyerEmailResult.Error.Message);

        var buyerEmail = buyerEmailResult.Value!;

        try
        {
            var options = new Iyzipay.Options
            {
                ApiKey = iyzico.ApiKey.Trim(),
                SecretKey = iyzico.SecretKey.Trim(),
                BaseUrl = iyzico.BaseUrl.Trim(),
            };

            var request = BuildRequest(session, targetPlan, currentPlan, planAmount, buyerEmail, iyzico);
            var response = await CheckoutFormInitialize.Create(request, options);

            if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                var reason = string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "Iyzico checkout initialize başarısız."
                    : response.ErrorMessage;
                return Result<CheckoutPrepareResult>.Failure("Billing.IyzicoApiError", reason);
            }

            if (string.IsNullOrWhiteSpace(response.PaymentPageUrl))
            {
                return Result<CheckoutPrepareResult>.Failure(
                    "Billing.IyzicoApiError",
                    "Iyzico PaymentPageUrl dönmedi.");
            }

            var externalReference = string.IsNullOrWhiteSpace(response.Token)
                ? response.ConversationId
                : response.Token;

            return Result<CheckoutPrepareResult>.Success(
                new CheckoutPrepareResult(
                    response.PaymentPageUrl.Trim(),
                    externalReference,
                    chargeCurrencyCode ?? "TRY",
                    chargeAmountMinor,
                    prorationRatio));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Iyzico checkout initialize hatası. Session: {SessionId}", session.Id);
            return Result<CheckoutPrepareResult>.Failure(
                "Billing.IyzicoApiError",
                "Iyzico checkout başlatılırken beklenmeyen hata oluştu.");
        }
    }

    private static CreateCheckoutFormInitializeRequest BuildRequest(
        BillingCheckoutSession session,
        string targetPlan,
        string currentPlan,
        decimal amountTry,
        string buyerEmail,
        IyzicoBillingOptions iyzico)
    {
        var conversationId = session.Id.ToString("D");
        var tenantId = session.TenantId.ToString("D");
        var amountText = amountTry.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var locale = NormalizeLocale(iyzico.Locale);

        return new CreateCheckoutFormInitializeRequest
        {
            Locale = locale,
            ConversationId = conversationId,
            Price = amountText,
            PaidPrice = amountText,
            Currency = Currency.TRY.ToString(),
            BasketId = conversationId,
            PaymentGroup = PaymentGroup.SUBSCRIPTION.ToString(),
            CallbackUrl = iyzico.CallbackUrl.Trim(),
            EnabledInstallments = [1],
            Buyer = new Buyer
            {
                Id = tenantId,
                Name = "Tenant",
                Surname = "Owner",
                GsmNumber = "+905555555555",
                Email = buyerEmail,
                IdentityNumber = "11111111111",
                LastLoginDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                RegistrationDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                RegistrationAddress = $"Tenant {tenantId} billing",
                Ip = "127.0.0.1",
                City = iyzico.City,
                Country = iyzico.Country,
                ZipCode = "34000",
            },
            ShippingAddress = new Address
            {
                ContactName = $"Tenant {tenantId[..8]}",
                City = iyzico.City,
                Country = iyzico.Country,
                Description = "Digital subscription service",
                ZipCode = "34000",
            },
            BillingAddress = new Address
            {
                ContactName = $"Tenant {tenantId[..8]}",
                City = iyzico.City,
                Country = iyzico.Country,
                Description = "Digital subscription service",
                ZipCode = "34000",
            },
            BasketItems =
            [
                new BasketItem
                {
                    Id = conversationId,
                    Name = $"Veteriner SaaS {currentPlan}->{targetPlan}",
                    Category1 = "Subscription",
                    Category2 = "SaaS",
                    ItemType = BasketItemType.VIRTUAL.ToString(),
                    Price = amountText,
                }
            ]
        };
    }

    private static string NormalizeLocale(string? locale)
        => string.Equals(locale?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "tr";

    private async Task<Result<string>> ResolveBuyerEmailAsync(BillingCheckoutSession session, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } currentUserId || currentUserId == Guid.Empty)
        {
            return Result<string>.Failure(
                "Billing.IyzicoBuyerEmailMissing",
                "Iyzico checkout için buyer email bulunamadı (oturum kullanıcı bilgisi eksik).");
        }

        var user = await _users.FirstOrDefaultAsync(new UserByIdWithRolesSpec(currentUserId), ct);
        var email = user?.Email?.Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning(
                "Iyzico checkout başlatılamadı: buyer email boş. TenantId={TenantId}, UserId={UserId}",
                session.TenantId,
                currentUserId);
            return Result<string>.Failure(
                "Billing.IyzicoBuyerEmailMissing",
                "Iyzico checkout için kullanıcı e-posta adresi bulunamadı.");
        }

        if (!IsValidEmail(email))
        {
            _logger.LogWarning(
                "Iyzico checkout başlatılamadı: buyer email formatı geçersiz. TenantId={TenantId}, UserId={UserId}",
                session.TenantId,
                currentUserId);
            return Result<string>.Failure(
                "Billing.IyzicoBuyerEmailInvalid",
                "Iyzico checkout için kullanıcı e-posta formatı geçersiz.");
        }

        return Result<string>.Success(email);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
