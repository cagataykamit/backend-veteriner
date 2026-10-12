namespace Backend.Veteriner.Api.Middleware;

public static class SecurityHeadersExtensions
{
    private const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// API yanıtlarına temel tarayıcı koruma başlıkları ekler. Swagger UI satır içi betik kullandığından
    /// <c>/swagger</c> altında CSP uygulanmaz.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var headers = ctx.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";

                if (!ctx.Request.Path.StartsWithSegments("/swagger"))
                    headers["Content-Security-Policy"] = ApiContentSecurityPolicy;

                return Task.CompletedTask;
            });

            await next();
        });
}
