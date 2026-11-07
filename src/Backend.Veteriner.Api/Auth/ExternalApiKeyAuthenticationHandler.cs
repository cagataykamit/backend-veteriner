using System.Diagnostics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.ExternalApi;
using Backend.Veteriner.Domain.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Backend.Veteriner.Api.Auth;

/// <summary>
/// Dış API anahtar şeması (TIER-009; docs/EXTERNAL_API_CONTRACT.md §2). Yalnız <see cref="ExternalApiAuth.Policy"/> bu şemayı ister;
/// panel politikaları varsayılan JWT şemasıyla kalır, bu yüzden anahtar panelde, panel JWT'si burada geçmez. Kural
/// <see cref="ExternalApiAccess"/>'tedir; burada yalnız başlık okunur ve 401/403 gövdesi yazılır.
/// </summary>
public sealed class ExternalApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string FailureItemKey = "ExternalApi.AuthFailure";

    public ExternalApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Panel oturumu dış API'de kimlik sayılmaz; iki kimlik birlikte gelirse hangisinin geçerli olduğu belirsiz kalmasın.
        if (Request.Headers.ContainsKey(HeaderNames.Authorization))
            return Fail(ExternalApiErrors.BearerNotAccepted);

        var values = Request.Headers[ExternalApiAuth.HeaderName];
        if (values.Count > 1)
            return Fail(ExternalApiErrors.KeyInvalid);

        var access = Context.RequestServices.GetRequiredService<ExternalApiAccess>();
        var result = await access.AuthenticateAsync(values.ToString(), Context.RequestAborted);
        if (!result.IsSuccess)
            return Fail(result.Error);

        var caller = result.Value!;
        var claims = new List<Claim>
        {
            new(ExternalApiAuth.PrincipalTypeClaim, ExternalApiAuth.ApiKeyPrincipalType),
            new(ExternalApiAuth.KeyIdClaim, caller.KeyId.ToString()),
        };
        claims.AddRange(caller.Scopes.Select(s => new Claim(ExternalApiAuth.ScopeClaim, s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var error = Context.Items[FailureItemKey] is Error e ? e : ExternalApiErrors.KeyMissing;
        Response.Headers.WWWAuthenticate = $"ApiKey header=\"{ExternalApiAuth.HeaderName}\"";
        return WriteProblemAsync(StatusCodes.Status401Unauthorized, "Yetkisiz erişim", error);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => WriteProblemAsync(StatusCodes.Status403Forbidden, "Erişim reddedildi", ExternalApiErrors.ScopeMissing);

    private AuthenticateResult Fail(Error error)
    {
        Context.Items[FailureItemKey] = error;
        return AuthenticateResult.Fail(error.Code);
    }

    private async Task WriteProblemAsync(int status, string title, Error error)
    {
        if (Response.HasStarted)
            return;

        var traceId = Activity.Current?.Id ?? Context.TraceIdentifier;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
            Detail = error.Message,
            Instance = Request.Path,
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] =
            (Context.Items.TryGetValue(Correlation.HeaderName, out var cid) ? cid?.ToString() : null) ?? traceId;
        problem.AddTimestamp(Context);

        Response.StatusCode = status;
        Response.ContentType = "application/problem+json";
        await Response.WriteAsJsonAsync(problem);
    }
}
