using Backend.Veteriner.Application.Common.Constants; // ?? EKLE
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Http;

public sealed class CorrelationIdHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _http;

    public CorrelationIdHandler(IHttpContextAccessor http) => _http = http;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headerName = Correlation.HeaderName;

        var cid = _http.HttpContext?.Items[headerName]?.ToString();
        if (!string.IsNullOrWhiteSpace(cid) && !request.Headers.Contains(headerName))
        {
            request.Headers.Add(headerName, cid);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
