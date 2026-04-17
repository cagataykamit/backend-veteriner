using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.Tenants.Commands.Create;
using Backend.Veteriner.Application.Tenants.Commands.CreateInvite;
using Backend.Veteriner.Application.Tenants.Commands.Checkout;
using Backend.Veteriner.Application.Tenants.Commands.PlanChanges;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Queries.GetById;
using Backend.Veteriner.Application.Tenants.Queries.GetList;
using Backend.Veteriner.Application.Tenants.Queries.GetAssignableOperationClaimsForInvite;
using Backend.Veteriner.Application.Tenants.Queries.GetInvites;
using Backend.Veteriner.Application.Tenants.Queries.GetMembers;
using Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionCheckout;
using Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionSummary;
using Backend.Veteriner.Application.Tenants.Queries.GetPendingPlanChange;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using System.Collections.Generic;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Kiracı (tenant) yönetimi. İstek başına tenant bağlamı henüz JWT'den otomatik çözülmüyor; listeleme platform yetkisi varsayar.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tenants")]
[Produces("application/json")]
[Authorize]
public sealed class TenantsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public TenantsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Kiracıya kullanıcı daveti oluşturur. Yetki: <c>Tenants.InviteCreate</c>; JWT <c>tenant_id</c> route ile aynı olmalı.</summary>
    [HttpPost("{tenantId:guid}/invites")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CreateTenantInviteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvite([FromRoute] Guid tenantId, [FromBody] CreateTenantInviteBody? body, CancellationToken ct)
    {
        if (body is null)
        {
            return Result<CreateTenantInviteResultDto>.Failure(
                    "Invites.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var cmd = new CreateTenantInviteCommand(
            tenantId,
            body.Email,
            body.ClinicId,
            body.OperationClaimId,
            body.ExpiresAtUtc);
        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Davet ekranı için atanabilir operation claim (rol) listesi. Yetki: <c>Tenants.InviteCreate</c>.
    /// Yanıttaki <c>operationClaimId</c> değerleri doğrudan <c>POST …/invites</c> gövdesine yazılmalıdır (kullanıcının mevcut claim listesi değil).
    /// </summary>
    [HttpGet("{tenantId:guid}/assignable-operation-claims")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(IReadOnlyList<AssignableOperationClaimForInviteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAssignableOperationClaimsForInvite([FromRoute] Guid tenantId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetAssignableOperationClaimsForInviteQuery(tenantId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant paneli: kiracı üyelerini sayfalı listeler (<c>UserTenants</c>). <c>PageRequest.search</c> e-postada contains (büyük/küçük harf duyarsız);
    /// <c>sort</c>/<c>order</c> işlenmez. Global <c>/api/v1/admin/users</c> ile karıştırılmamalıdır. Yetki: <c>Tenants.InviteCreate</c>; JWT <c>tenant_id</c> route ile aynı olmalı.
    /// </summary>
    [HttpGet("{tenantId:guid}/members")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(PagedResult<TenantMemberListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMembers([FromRoute] Guid tenantId, [FromQuery] PageRequest req, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetTenantMembersQuery(tenantId, req), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant paneli: kiracı davetlerini sayfalı listeler. Opsiyonel <c>status</c> (<see cref="TenantInviteStatus"/>).
    /// <c>PageRequest.search</c> e-postada contains; <c>sort</c>/<c>order</c> işlenmez. Yetki: <c>Tenants.InviteCreate</c>; JWT <c>tenant_id</c> route ile aynı olmalı.
    /// </summary>
    [HttpGet("{tenantId:guid}/invites")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(PagedResult<TenantInviteListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInvites(
        [FromRoute] Guid tenantId,
        [FromQuery] PageRequest req,
        [FromQuery] TenantInviteStatus? status,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetTenantInvitesQuery(tenantId, req, status), ct);
        return result.ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Tenants.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTenantCommand cmd, CancellationToken ct)
    {
        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            id);
    }

    /// <summary>
    /// Kiracı abonelik özeti (plan, durum, trial, kalan gün). <c>Subscriptions.Read</c> veya <c>Tenants.Read</c> gerekir;
    /// JWT <c>tenant_id</c> route <c>tenantId</c> ile eşleşmeli (platform <c>Tenants.Read</c> ile başka kiracı görülebilir).
    /// </summary>
    [HttpGet("{tenantId:guid}/subscription-summary")]
    [Authorize]
    [ProducesResponseType(typeof(TenantSubscriptionSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubscriptionSummary([FromRoute] Guid tenantId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetTenantSubscriptionSummaryQuery(tenantId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Bekleyen plan değişikliği kaydını döner.
    /// Kayıt yoksa yanıt 200 OK + null içerik döner (404 değildir).
    /// </summary>
    [HttpGet("{tenantId:guid}/subscription-plan-change/pending")]
    [Authorize(Policy = PermissionCatalog.Subscriptions.Manage)]
    [ProducesResponseType(typeof(PendingSubscriptionPlanChangeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingSubscriptionPlanChange([FromRoute] Guid tenantId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPendingSubscriptionPlanChangeQuery(tenantId), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{tenantId:guid}/subscription-plan-change/downgrade")]
    [Authorize(Policy = PermissionCatalog.Subscriptions.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PendingSubscriptionPlanChangeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ScheduleSubscriptionDowngrade(
        [FromRoute] Guid tenantId,
        [FromBody] ScheduleSubscriptionDowngradeBody? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Result<PendingSubscriptionPlanChangeDto>.Failure(
                    "Subscriptions.PlanChange.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new ScheduleSubscriptionDowngradeCommand(tenantId, body.TargetPlanCode, body.Reason),
            ct);
        return result.ToActionResult(this);
    }

    [HttpDelete("{tenantId:guid}/subscription-plan-change/pending")]
    [Authorize(Policy = PermissionCatalog.Subscriptions.Manage)]
    [ProducesResponseType(typeof(PendingSubscriptionPlanChangeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelPendingSubscriptionPlanChange([FromRoute] Guid tenantId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CancelPendingSubscriptionPlanChangeCommand(tenantId), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{tenantId:guid}/subscription-checkout")]
    [Authorize(Policy = PermissionCatalog.Subscriptions.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SubscriptionCheckoutSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartSubscriptionCheckout(
        [FromRoute] Guid tenantId,
        [FromBody] StartSubscriptionCheckoutBody? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                    "Subscriptions.Checkout.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new StartSubscriptionCheckoutCommand(tenantId, body.TargetPlanCode), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{tenantId:guid}/subscription-checkout/{checkoutSessionId:guid}")]
    [Authorize(Policy = PermissionCatalog.Subscriptions.Manage)]
    [ProducesResponseType(typeof(SubscriptionCheckoutSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubscriptionCheckoutStatus(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid checkoutSessionId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetSubscriptionCheckoutQuery(tenantId, checkoutSessionId), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{tenantId:guid}/subscription-checkout/{checkoutSessionId:guid}/finalize")]
    [Authorize(Policy = PermissionCatalog.Subscriptions.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SubscriptionCheckoutSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> FinalizeSubscriptionCheckout(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid checkoutSessionId,
        [FromBody] FinalizeSubscriptionCheckoutBody? body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new FinalizeSubscriptionCheckoutCommand(tenantId, checkoutSessionId, body?.ExternalReference), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Tenants.Read)]
    [ProducesResponseType(typeof(TenantDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Tenants.Read)]
    [ProducesResponseType(typeof(PagedResult<TenantListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] PageRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantsListQuery(req), ct);
        return result.ToActionResult(this);
    }
}
