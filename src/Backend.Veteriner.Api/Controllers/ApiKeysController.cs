using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExternalApi;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record CreateApiKeyBody(string? Name, IReadOnlyList<string>? Scopes);

/// <summary>
/// Dış API anahtarlarının panel yönetimi (TIER-009; docs/EXTERNAL_API_CONTRACT.md §6). Yalnız personel JWT'si; anahtarın kendisi
/// yalnız oluşturma yanıtında bir kez döner. Oluşturma Kurumsal ister (403 <c>Subscriptions.FeatureNotInPlan</c>); liste ve iptal
/// her pakette açık.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/api-keys")]
[Produces("application/json")]
[Authorize(Policy = PermissionCatalog.ApiKeys.Manage)]
public sealed class ApiKeysController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ApiKeysController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiKeyListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetApiKeysQuery(), ct)).ToActionResult(this);
    }

    /// <summary>Yanıttaki <c>plainTextKey</c> bir daha gösterilmez; sunucuda yalnız özeti ve ön eki kalır.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreatedApiKeyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        Response.Headers.CacheControl = "no-store";
        return (await _mediator.Send(new CreateApiKeyCommand(body.Name, body.Scopes), ct)).ToActionResult(this);
    }

    /// <summary>İptal geri alınmaz; iptal edilen anahtarla gelen istek 401 <c>ExternalApi.KeyRevoked</c> alır.</summary>
    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new RevokeApiKeyCommand(id), ct)).ToActionResult(this);
    }
}
