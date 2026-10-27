using System.Security.Claims;
using AwesomeAssertions;
using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Middleware;

/// <summary>
/// Kiracı yalnız imzalı JWT'den gelir. Sorgu <c>tenantId</c> kiracısız isteğe (anonim, portal token'ı) kiracı bağlamı
/// vermez; aksi halde üyelik denetimi olmadan başka kiracı hedeflenebilir ve ADR-040 filtresi de bu değeri kullanır.
/// </summary>
public sealed class TenantContextMiddlewareTests
{
    [Fact]
    public async Task Query_TenantId_Should_Not_Become_Tenant_Context_Without_Jwt_Tenant()
    {
        var (context, nextCalled) = await RunAsync(user: new ClaimsPrincipal(new ClaimsIdentity()), query: Guid.NewGuid());

        nextCalled.Should().BeTrue();
        context.Items.ContainsKey(TenantHttpContextKeys.ResolvedTenantId).Should().BeFalse();
    }

    [Fact]
    public async Task Jwt_Tenant_Should_Become_Tenant_Context()
    {
        var tenantId = Guid.NewGuid();

        var (context, nextCalled) = await RunAsync(UserWithTenant(tenantId), query: null);

        nextCalled.Should().BeTrue();
        context.Items[TenantHttpContextKeys.ResolvedTenantId].Should().Be(tenantId);
    }

    [Fact]
    public async Task Query_TenantId_Different_From_Jwt_Tenant_Should_Be_Rejected()
    {
        var (context, nextCalled) = await RunAsync(UserWithTenant(Guid.NewGuid()), query: Guid.NewGuid());

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private static ClaimsPrincipal UserWithTenant(Guid tenantId)
        => new(new ClaimsIdentity([new Claim(VeterinerClaims.TenantId, tenantId.ToString("D"))], "Test"));

    private static async Task<(HttpContext Context, bool NextCalled)> RunAsync(ClaimsPrincipal user, Guid? query)
    {
        var context = new DefaultHttpContext
        {
            User = user,
            // Ret gövdesindeki timestampUtc DI saatinden okunur.
            RequestServices = new ServiceCollection().AddSingleton(TimeProvider.System).BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        if (query is { } q)
            context.Request.QueryString = new QueryString($"?tenantId={q:D}");

        var nextCalled = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return (context, nextCalled);
    }
}
