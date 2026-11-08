using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExternalApi;
using Backend.Veteriner.Domain.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Backend.Veteriner.Api.Auth;

/// <summary>
/// Dış API denetleyicisinin sınıf düzeyi kapısı (TIER-009): kimlik ve kapsam yetkilendirmede geçtikten sonra kiracı parametresi
/// reddedilir (kiracı yalnız anahtardan gelir), ardından paket ve abonelik kuralı (<see cref="ExternalApiAccess"/>) uygulanır.
/// Yeni dış uç bu sınıfa eklendiğinde kapı kendiliğinden çalışır.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExternalApiAccessAttribute : Attribute, IAsyncActionFilter
{
    public const string TenantQueryName = "tenantId";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var controller = (ControllerBase)context.Controller;

        if (http.Request.Query.ContainsKey(TenantQueryName))
        {
            context.Result = Result.Failure(
                    ExternalApiErrors.ValidationCode, "Dış API kiracı parametresi kabul etmez; kiracı API anahtarından belirlenir.")
                .ToActionResult(controller);
            return;
        }

        if (http.RequestServices.GetRequiredService<ITenantContext>().TenantId is not { } tenantId)
        {
            context.Result = new ChallengeResult(ExternalApiAuth.Scheme);
            return;
        }

        var allowed = await http.RequestServices.GetRequiredService<ExternalApiAccess>()
            .EnsureTenantAllowedAsync(tenantId, http.RequestAborted);
        if (!allowed.IsSuccess)
        {
            context.Result = allowed.ToActionResult(controller);
            return;
        }

        await next();
    }
}
