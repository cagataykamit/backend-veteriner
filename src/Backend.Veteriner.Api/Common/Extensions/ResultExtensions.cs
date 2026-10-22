using Backend.Veteriner.Domain.Shared;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Common.Extensions;

public static class ResultExtensions
{
    public static IActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        if (result.IsSuccess)
        {
            return controller.NoContent();
        }

        return MapFailure(controller, result.Error);
    }

    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        if (result.IsSuccess)
        {
            return controller.Ok(result.Value);
        }

        return MapFailure(controller, result.Error);
    }

    private static IActionResult MapFailure(ControllerBase controller, Error error)
    {
        var (statusCode, type) = MapStatusCode(error.Code);
        var traceId = Activity.Current?.Id ?? controller.HttpContext.TraceIdentifier;
        var correlationId = ResolveCorrelationId(controller.HttpContext, traceId);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ResolveProblemTitle(error.Code, statusCode),
            Detail = string.IsNullOrWhiteSpace(error.Message)
                ? "A business rule was violated."
                : error.Message,
            Type = type,
            Instance = controller.HttpContext.Request.Path
        };

        if (!string.IsNullOrWhiteSpace(error.Code))
        {
            problem.Extensions["code"] = error.Code;
        }
        if (error.Extensions is not null)
        {
            foreach (var (key, value) in error.Extensions)
                problem.Extensions.TryAdd(key, value);
        }
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["timestampUtc"] = DateTime.UtcNow;

        return controller.StatusCode(statusCode, problem);
    }

    private static string ResolveCorrelationId(HttpContext httpContext, string fallback)
    {
        return
            (httpContext.Items.TryGetValue("X-Correlation-ID", out var v) ? v?.ToString() : null)
            ?? (httpContext.Items.TryGetValue("CorrelationId", out var legacy) ? legacy?.ToString() : null)
            ?? (httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var cid) ? cid.ToString() : null)
            ?? fallback;
    }

    private static (int StatusCode, string Type) MapStatusCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
        }

        var normalized = code.ToLowerInvariant();

        // İçe aktarma partisi durum/tekrar çakışmaları (IMPORT_API_CONTRACT.md §8); "validationstale" aşağıdaki
        // "validation" dalına düşmesin diye önce değerlendirilir.
        if (normalized is "imports.invalidstate" or "imports.requestidconflict" or "imports.toomanyopendrafts"
            or "imports.anotherjobrunning" or "imports.validationstale" or "imports.haserrorrows"
            or "imports.reminderimpactchanged" or "imports.rollbackblocked" or "imports.commitblocked")
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        // Dosya katmanı (IMAGING_API_CONTRACT.md §2/§5).
        switch (normalized)
        {
            case "files.toolarge":
                return (StatusCodes.Status413PayloadTooLarge, "https://httpstatuses.io/413");
            case "files.unsupportedtype":
                return (StatusCodes.Status415UnsupportedMediaType, "https://httpstatuses.io/415");
            case "files.limitreached":
                return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
            case "files.malwaredetected":
                return (StatusCodes.Status422UnprocessableEntity, "https://httpstatuses.io/422");
            case "files.storageunavailable":
                return (StatusCodes.Status503ServiceUnavailable, "https://httpstatuses.io/503");
            case "files.accessurlinvalid":
                return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        // Saklama süresi sonunda temizlenen satır verisi (ADR-019 K9).
        if (normalized is "imports.payloadpurged")
        {
            return (StatusCodes.Status410Gone, "https://httpstatuses.io/410");
        }

        // İstemcinin gösterdiği hukuki belge sürümü güncel değil (ADR-029): yenileyip tekrar kabul etmeli.
        if (normalized is "legalacceptances.versionoutdated")
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        // Hasta sahibi portalı (ADR-031): müşteri/davet/bağ durumu işlemle çakışıyor.
        if (normalized is "ownerportal.clientoptedout" or "ownerportal.clientemailmissing" or "ownerportal.clientnotinvitable"
            or "ownerportal.alreadylinked" or "ownerportal.invitenotpending" or "ownerportal.linkalreadyclosed"
            or "portal.invites.passwordalreadyset")
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        if (normalized.Contains("duplicate"))
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        // ProductStocks.ConcurrencyConflict / StockMovements.ConcurrencyConflict vb.
        if (normalized.Contains("concurrencyconflict"))
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        // Appointments.ClinicTimeConflict / Appointments.PetTimeConflict ve legacy
        // Appointments.ClinicSlotDuplicate / Appointments.PetSlotDuplicate (Duplicate dalı zaten yakalar)
        // — overlap/conflict niteliğindeki tüm hatalar 409 Conflict döner; OpenAPI ile uyumlu.
        if (normalized.Contains("timeconflict") || normalized.Contains("slotduplicate"))
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        // Ödeme tahsisi: açık tutar aşımı, tahsisli kaydın kilitli alanları/iptali ve zaten iptal edilmiş kayıt (durum çakışması).
        if (normalized.Contains("exceedsoutstanding") || normalized.Contains("updateblockedbyallocation")
            || normalized.Contains("voidblockedbyallocation") || normalized.Contains("correctionbelowallocated")
            || normalized.Contains("alreadyvoided") || normalized.Contains("requestidvoided")
            || normalized.Contains("alreadyreversed") || normalized.Contains("alreadycancelled")
            || normalized.Contains("linkedtoadministration")
            || normalized.Contains("alreadyconverted") || normalized.Contains("alreadywaived"))
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        // Vefat etmiş hayvan için yeni randevu/geliş/muayene (ADR-020 S5): kaydın durumu ile çakışma.
        if (normalized is "pets.deceased")
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        // Satış ortağı komisyonu (ADR-037): ödenmiş/iptal satır yeniden "ödendi" işaretlenemez (durum çakışması).
        if (normalized is "discountredemptions.commissionnotaccrued")
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        if (normalized.Contains("sameplan") || normalized.Contains("notopen") || normalized.Contains("finalized"))
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        if (normalized.Contains("notfound"))
        {
            return (StatusCodes.Status404NotFound, "https://httpstatuses.io/404");
        }

        if (normalized.Contains("forbidden"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("tenantinactive"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("userlimitexceeded"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("notassignable"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("subscriptionreadonly") || normalized.Contains("subscriptioncancelled"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("tenantreadonly") || normalized.Contains("tenantcancelled"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("webhooksignatureinvalid") || normalized.Contains("webhooksignaturemissing"))
        {
            return (StatusCodes.Status401Unauthorized, "https://httpstatuses.io/401");
        }

        if (normalized.Contains("stripewebhooknotconfigured")
            || normalized.Contains("iyzicowebhooknotconfigured")
            || normalized.Contains("iyzicoconfigurationincomplete")
            || normalized.Contains("providerconfigurationincomplete")
            || normalized.Contains("iyzicoplanpricenotconfigured"))
        {
            return (StatusCodes.Status503ServiceUnavailable, "https://httpstatuses.io/503");
        }

        if (normalized.Contains("stripeapierror") || normalized.Contains("iyzicoapierror"))
        {
            return (StatusCodes.Status502BadGateway, "https://httpstatuses.io/502");
        }

        if (normalized.Contains("stripesecretmissing")
            || normalized.Contains("stripecheckouturlsmissing")
            || normalized.Contains("stripepricenotconfigured")
            || normalized.Contains("stripeconfigurationincomplete"))
        {
            return (StatusCodes.Status503ServiceUnavailable, "https://httpstatuses.io/503");
        }

        if (normalized.Contains("invalidcheckoutprovider"))
        {
            return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
        }

        if (normalized.Contains("iyzicobuyeremailmissing") || normalized.Contains("iyzicobuyeremailinvalid"))
        {
            return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
        }

        if (normalized.Contains("providermismatch"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("webhookpayloadinvalid") || normalized.Contains("webhookpayloadunsupported"))
        {
            return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
        }

        if (normalized.Contains("webhookreceiptpersistfailed"))
        {
            return (StatusCodes.Status500InternalServerError, "https://httpstatuses.io/500");
        }

        if (normalized.Contains("unauthorized") || normalized.Contains("unauthenticated"))
        {
            return (StatusCodes.Status401Unauthorized, "https://httpstatuses.io/401");
        }

        if (normalized.Contains("validation"))
        {
            return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
        }

        if (normalized.Contains("accessdenied") || normalized.Contains("auth.tenantnotmember"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("tenantmembers.cannotremoveself"))
        {
            return (StatusCodes.Status403Forbidden, "https://httpstatuses.io/403");
        }

        if (normalized.Contains("tenantmembers.cannotremovelastadmin")
            || normalized.Contains("tenantmembers.cannotremovesolemember"))
        {
            return (StatusCodes.Status409Conflict, "https://httpstatuses.io/409");
        }

        if (normalized.Contains("tenantmembers.notfound"))
        {
            return (StatusCodes.Status404NotFound, "https://httpstatuses.io/404");
        }

        if (normalized.Contains("tenantmembers.removefailed"))
        {
            return (StatusCodes.Status500InternalServerError, "https://httpstatuses.io/500");
        }

        return (StatusCodes.Status400BadRequest, "https://httpstatuses.io/400");
    }

    /// <summary>
    /// ModelState/FluentValidation ile aynı dil ve anlam hizası; kod öncelikli (validation vs HTTP durumu).
    /// </summary>
    private static string ResolveProblemTitle(string? code, int statusCode)
    {
        var normalized = code?.ToLowerInvariant() ?? "";

        if (normalized.Contains("validation"))
            return "Doğrulama hatası";

        return statusCode switch
        {
            StatusCodes.Status401Unauthorized => "Yetkisiz erişim",
            StatusCodes.Status403Forbidden => "Erişim reddedildi",
            StatusCodes.Status404NotFound => "Bulunamadı",
            StatusCodes.Status409Conflict => "Çakışma",
            StatusCodes.Status502BadGateway => "Upstream servis hatası",
            StatusCodes.Status503ServiceUnavailable => "Servis kullanılamıyor",
            StatusCodes.Status500InternalServerError => "Sunucu hatası",
            StatusCodes.Status400BadRequest when normalized.Contains("unauthorized")
                || normalized.Contains("unauthenticated") => "Yetkisiz erişim",
            _ => "İş kuralı ihlali"
        };
    }
}

