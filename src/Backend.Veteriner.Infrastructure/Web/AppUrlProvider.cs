using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Web;

public sealed class AppUrlProvider : IAppUrlProvider
{
    private readonly IHttpContextAccessor _http;
    public AppUrlProvider(IHttpContextAccessor http) => _http = http;

    public string BuildAbsolute(string path, string? query = null)
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return path + (query is not null ? "?" + query : "");

        var scheme = ctx.Request.Scheme;          // http/https
        var host = ctx.Request.Host.Value;        // localhost:7173
        var basePath = ctx.Request.PathBase.Value; // genelde ""

        var url = $"{scheme}://{host}{basePath}{path}";
        if (!string.IsNullOrWhiteSpace(query))
            url += (query.StartsWith("?") ? query : "?" + query);

        return url;
    }
}
