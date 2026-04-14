using System.Globalization;
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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace Backend.Veteriner.Infrastructure.Billing;

public sealed class IyzicoBillingCheckoutProvider : IBillingCheckoutProvider
{
    private readonly BillingOptions _billingOptions;
    private readonly ILogger<IyzicoBillingCheckoutProvider> _logger;
    private readonly IUserReadRepository _users;
    private readonly IClientContext _clientContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHostEnvironment _hostEnvironment;

    public IyzicoBillingCheckoutProvider(
        IOptions<BillingOptions> billingOptions,
        ILogger<IyzicoBillingCheckoutProvider> logger,
        IUserReadRepository users,
        IClientContext clientContext,
        IHttpContextAccessor httpContextAccessor,
        IHostEnvironment hostEnvironment)
    {
        _billingOptions = billingOptions.Value;
        _logger = logger;
        _users = users;
        _clientContext = clientContext;
        _httpContextAccessor = httpContextAccessor;
        _hostEnvironment = hostEnvironment;
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

            var buyerIp = ResolveBuyerIpForIyzico();
            var request = BuildRequest(session, targetPlan, currentPlan, planAmount, buyerEmail, buyerIp, iyzico);
            LogCheckoutInitializePayloadDiagnostics(session.Id, planAmount, request, buyerEmail, buyerIp);

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

    private CreateCheckoutFormInitializeRequest BuildRequest(
        BillingCheckoutSession session,
        string targetPlan,
        string currentPlan,
        decimal amountTry,
        string buyerEmail,
        string buyerIp,
        IyzicoBillingOptions iyzico)
    {
        var conversationId = session.Id.ToString("D");
        var tenantId = session.TenantId.ToString("D");
        // İyzico tutar alanlarında ondalık basamak tutarlılığı (price = paidPrice = sepet satırı toplamı).
        var amountText = amountTry.ToString("F2", CultureInfo.InvariantCulture);
        var locale = NormalizeLocale(iyzico.Locale);
        var (buyerName, buyerSurname) = DeriveBuyerNameFromEmail(buyerEmail);

        return new CreateCheckoutFormInitializeRequest
        {
            Locale = locale,
            ConversationId = conversationId,
            Price = amountText,
            PaidPrice = amountText,
            Currency = Currency.TRY.ToString(),
            BasketId = conversationId,
            // Tek seferlik checkout-form ödemesi; SUBSCRIPTION grubu bazı akışlarda ek doğrulama tetikleyebilir.
            PaymentGroup = PaymentGroup.PRODUCT.ToString(),
            CallbackUrl = iyzico.CallbackUrl.Trim(),
            EnabledInstallments = [1],
            Buyer = new Buyer
            {
                Id = tenantId,
                Name = buyerName,
                Surname = buyerSurname,
                // Geçerli TR GSM biçimi (+90 + 10 hane); tamamen aynı rakam dizisi risk kurallarında sorun çıkarabilir.
                GsmNumber = "+905321234567",
                Email = buyerEmail,
                IdentityNumber = "11111111111",
                LastLoginDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                RegistrationDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                RegistrationAddress = $"{iyzico.City}, Turkey — tenant {tenantId[..8]}",
                Ip = buyerIp,
                City = iyzico.City,
                Country = iyzico.Country,
                ZipCode = "34000",
            },
            ShippingAddress = new Address
            {
                ContactName = $"{buyerName} {buyerSurname}",
                City = iyzico.City,
                Country = iyzico.Country,
                Description = "Digital subscription service",
                ZipCode = "34000",
            },
            BillingAddress = new Address
            {
                ContactName = $"{buyerName} {buyerSurname}",
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

    private void LogCheckoutInitializePayloadDiagnostics(
        Guid sessionId,
        decimal amountTry,
        CreateCheckoutFormInitializeRequest request,
        string buyerEmail,
        string buyerIp)
    {
        decimal basketSum = 0m;
        foreach (var item in request.BasketItems ?? [])
        {
            if (decimal.TryParse(item.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var line))
                basketSum += line;
        }

        var priceMatch = string.Equals(request.Price, request.PaidPrice, StringComparison.Ordinal)
                         && decimal.TryParse(request.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var p)
                         && p == basketSum
                         && p == amountTry;

        _logger.LogInformation(
            "Iyzico checkout initialize payload (masked): SessionId={SessionId} PaidPrice={PaidPrice} Price={Price} BasketSum={BasketSum} Currency={Currency} PricePaidBasketMatch={Match} BuyerEmailSet={EmailSet} EmailMasked={EmailMasked} BuyerIp={BuyerIp} BuyerCitySet={CitySet} BuyerCountrySet={CountrySet} BillingCitySet={BillCity} ShippingCitySet={ShipCity} CallbackUrlSet={CallbackSet}",
            sessionId,
            request.PaidPrice,
            request.Price,
            basketSum.ToString("F2", CultureInfo.InvariantCulture),
            request.Currency,
            priceMatch,
            !string.IsNullOrWhiteSpace(buyerEmail),
            MaskEmailForLog(buyerEmail),
            string.IsNullOrWhiteSpace(buyerIp) ? "(empty)" : IsLoopbackIp(buyerIp) ? $"{buyerIp} (loopback)" : "set",
            !string.IsNullOrWhiteSpace(request.Buyer?.City),
            !string.IsNullOrWhiteSpace(request.Buyer?.Country),
            !string.IsNullOrWhiteSpace(request.BillingAddress?.City),
            !string.IsNullOrWhiteSpace(request.ShippingAddress?.City),
            !string.IsNullOrWhiteSpace(request.CallbackUrl));
    }

    private static string MaskEmailForLog(string email)
    {
        var t = email.Trim();
        var at = t.IndexOf('@');
        if (at <= 0 || at >= t.Length - 1)
            return "(invalid)";
        return t[0] + "***@" + t[(at + 1)..];
    }

    /// <summary>
    /// Ödeme sayfası ile aynı müşteri oturumuna yakın IP; loopback Development+sandbox’ta config ile değiştirilebilir.
    /// </summary>
    private string ResolveBuyerIpForIyzico()
    {
        var resolved = ResolveBuyerIpRaw();
        if (!IsLoopbackIp(resolved))
            return resolved;

        var iyzico = _billingOptions.Iyzico;
        var fallback = iyzico.SandboxBuyerIp?.Trim() ?? "";

        if (_hostEnvironment.IsDevelopment()
            && IsIyzicoSandboxBaseUrl(iyzico.BaseUrl)
            && !string.IsNullOrEmpty(fallback)
            && IPAddress.TryParse(fallback, out _))
        {
            _logger.LogWarning(
                "Iyzico loopback buyer IP detected; sandbox buyer IP fallback applied. OriginalIp={OriginalIp} FallbackIp={FallbackIp} Environment={Environment}",
                resolved,
                fallback,
                _hostEnvironment.EnvironmentName);
            return fallback;
        }

        if (IsLoopbackIp(resolved))
        {
            var skipReason = !_hostEnvironment.IsDevelopment()
                ? "environment is not Development"
                : !IsIyzicoSandboxBaseUrl(iyzico.BaseUrl)
                    ? "BaseUrl is not sandbox-api.iyzipay.com"
                    : string.IsNullOrEmpty(fallback)
                        ? "Billing:Iyzico:SandboxBuyerIp is empty"
                        : !IPAddress.TryParse(fallback, out _)
                            ? "Billing:Iyzico:SandboxBuyerIp is not a valid IP address"
                            : "unknown";

            _logger.LogWarning(
                "Iyzico loopback buyer IP detected; sandbox buyer IP fallback not applied. OriginalIp={OriginalIp} SkipReason={SkipReason} Environment={Environment}",
                resolved,
                skipReason,
                _hostEnvironment.EnvironmentName);
        }

        return resolved;
    }

    private string ResolveBuyerIpRaw()
    {
        var http = _httpContextAccessor.HttpContext;
        if (http is not null)
        {
            var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(first))
                    return first;
            }

            var real = http.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrWhiteSpace(real))
                return real;
        }

        var ctx = _clientContext.IpAddress;
        if (!string.IsNullOrWhiteSpace(ctx))
            return ctx;

        _logger.LogWarning("Iyzico buyer IP could not be resolved; using 127.0.0.1 placeholder.");
        return "127.0.0.1";
    }

    private static bool IsIyzicoSandboxBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;
        var t = baseUrl.Trim();
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri))
            return string.Equals(uri.Host, "sandbox-api.iyzipay.com", StringComparison.OrdinalIgnoreCase);
        return t.Contains("sandbox-api.iyzipay.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return true;
        return ip == "::1"
               || ip == "127.0.0.1"
               || ip.StartsWith("::ffff:127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Name, string Surname) DeriveBuyerNameFromEmail(string email)
    {
        var local = email.Split('@')[0].Trim();
        if (string.IsNullOrEmpty(local))
            return ("Kayitli", "Kullanici");

        var parts = local.Split(new[] { '.', '_', '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (TrimName(parts[0]), TrimName(parts[1]));

        return (TrimName(local), "Kullanici");
    }

    private static string TrimName(string s)
    {
        var t = s.Trim();
        if (t.Length <= 32)
            return t.Length > 0 ? char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant() : "Kullanici";
        return t[..32];
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
