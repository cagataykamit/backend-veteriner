namespace Backend.Veteriner.Api.Middleware;

public static class RequestEnrichmentExtensions
{
    public static IApplicationBuilder UseRequestEnrichment(this IApplicationBuilder app)
        => app.UseMiddleware<RequestEnrichmentMiddleware>();
}
