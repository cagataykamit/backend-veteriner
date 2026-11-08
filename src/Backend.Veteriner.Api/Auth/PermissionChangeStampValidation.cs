using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Backend.Veteriner.Infrastructure.Caching;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Backend.Veteriner.Api.Auth;

/// <summary>
/// Personel token'ı için izin değişim damgası denetimi (SEC-002). Token, kullanıcının izinleri değişmeden önce
/// üretildiyse kimlik doğrulama başarısız olur (401); istemci yenileme ucundan güncel izinle yeni token alır.
/// </summary>
public static class PermissionChangeStampValidation
{
    public static Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var sub = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var iat = principal?.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;

        if (!Guid.TryParse(sub, out var userId)
            || !long.TryParse(iat, NumberStyles.Integer, CultureInfo.InvariantCulture, out var issuedAt))
        {
            context.Fail("Token sub/iat içermiyor.");
            return Task.CompletedTask;
        }

        var stamps = context.HttpContext.RequestServices.GetRequiredService<PermissionChangeStamps>();
        if (stamps.IsStale(userId, issuedAt))
            context.Fail("Token, kullanıcının izinleri değişmeden önce üretildi; yenilenmeli.");

        return Task.CompletedTask;
    }
}
