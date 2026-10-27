using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Backend.Veteriner.Api.Auth;

/// <summary>
/// Platform yönetimi ucu kiracılar arası çalışır (ADR-040): eylem boyunca kiracı filtresi kapalıdır (sistem kapsamı).
/// Yalnız <see cref="AuthorizationPolicyNames.PlatformAdmin"/> ile korunan denetleyicide kullanılır; eylem filtresi
/// yetkilendirmeden sonra koşar. Kullanım yerleri mimari testte izinli listeye bağlıdır.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlatformTenantScopeAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        using var system = context.HttpContext.RequestServices.GetRequiredService<ITenantScope>().AsSystem();
        await next();
    }
}
