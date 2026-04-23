using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.Tenants.Commands.AssignMemberClinic;
using Backend.Veteriner.Application.Tenants.Commands.AssignMemberRole;
using Backend.Veteriner.Application.Tenants.Commands.CancelInvite;
using Backend.Veteriner.Application.Tenants.Commands.Create;
using Backend.Veteriner.Application.Tenants.Commands.CreateInvite;
using Backend.Veteriner.Application.Tenants.Commands.RemoveMemberClinic;
using Backend.Veteriner.Application.Tenants.Commands.RemoveMemberRole;
using Backend.Veteriner.Application.Tenants.Commands.ResendInvite;
using Backend.Veteriner.Application.Tenants.Commands.UpdateSettings;
using Backend.Veteriner.Application.Tenants.Commands.Checkout;
using Backend.Veteriner.Application.Tenants.Commands.PlanChanges;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Queries.GetById;
using Backend.Veteriner.Application.Tenants.Queries.GetList;
using Backend.Veteriner.Application.Tenants.Queries.GetAssignableOperationClaimsForInvite;
using Backend.Veteriner.Application.Tenants.Queries.GetAssignableRolePermissionMatrix;
using Backend.Veteriner.Application.Tenants.Queries.GetInviteById;
using Backend.Veteriner.Application.Tenants.Queries.GetInvites;
using Backend.Veteriner.Application.Tenants.Queries.GetMemberById;
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
    /// Davet whitelist rolleri için DB’deki permission bağlarının özeti (read-only matris). Yetki: <c>Tenants.InviteCreate</c>;
    /// JWT <c>tenant_id</c> route ile aynı olmalı. Davet oluşturma / rol seçimi ile aynı whitelist sırası.
    /// </summary>
    [HttpGet("{tenantId:guid}/assignable-role-permission-matrix")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(IReadOnlyList<TenantAssignableRolePermissionMatrixRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAssignableRolePermissionMatrix([FromRoute] Guid tenantId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetTenantAssignableRolePermissionMatrixQuery(tenantId), ct);
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
    /// Tenant paneli: tek üye detayı. Tenant-scoped; <c>createdAtUtc</c> <c>UserTenant.CreatedAtUtc</c>'dir.
    /// <c>roles</c> yalnız whitelist claim'lerini içerir (teknik/internal claim'ler gizlenir);
    /// <c>clinics</c> üyenin bu kiracı içindeki kliniklerini listeler. Üye bu kiracıda yoksa 404 <c>Members.NotFound</c> (sızma maskelemesi).
    /// Global <c>/api/v1/admin/users/{id}</c> ile karıştırılmamalıdır. Yetki: <c>Tenants.InviteCreate</c>.
    /// </summary>
    [HttpGet("{tenantId:guid}/members/{memberId:guid}")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(TenantMemberDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemberById(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid memberId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetTenantMemberByIdQuery(tenantId, memberId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant paneli: üyeye whitelist içinden rol (OperationClaim) atar. Idempotent (<c>alreadyAssigned</c>).
    /// Global <c>/api/v1/admin/users/{userId}/operation-claims/{claimId}</c> yüzeyine düşmez.
    /// Yetki: <c>Tenants.InviteCreate</c>. Read-only/cancelled tenant'ta §23 write-guard bu command'ı keser.
    /// </summary>
    [HttpPost("{tenantId:guid}/members/{memberId:guid}/roles/{operationClaimId:guid}")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(AssignTenantMemberRoleResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignMemberRole(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid memberId,
        [FromRoute] Guid operationClaimId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new AssignTenantMemberRoleCommand(tenantId, memberId, operationClaimId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant paneli: üyeden whitelist içinden rol (OperationClaim) kaldırır. Idempotent (<c>alreadyRemoved</c>).
    /// Self-protect: çağıran kendi üzerinden rol çıkaramaz (<c>Invites.SelfRoleRemoveForbidden</c>).
    /// Yetki: <c>Tenants.InviteCreate</c>. Read-only/cancelled tenant'ta §23 write-guard bu command'ı keser.
    /// </summary>
    [HttpDelete("{tenantId:guid}/members/{memberId:guid}/roles/{operationClaimId:guid}")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(RemoveTenantMemberRoleResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMemberRole(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid memberId,
        [FromRoute] Guid operationClaimId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new RemoveTenantMemberRoleCommand(tenantId, memberId, operationClaimId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant paneli: üyeye bu kiracının kliniğini atar (Faz 4B). Idempotent (<c>alreadyAssigned</c>).
    /// Global admin yüzeyine düşmez; klinik bu kiracıya ait değilse <c>Clinics.NotFound</c>, pasifse <c>Clinics.Inactive</c>.
    /// Yetki: <c>Tenants.InviteCreate</c>. Read-only/cancelled tenant'ta §23 write-guard bu command'ı keser.
    /// Permission cache invalidation yapılmaz (clinic membership permission setini değiştirmez).
    /// </summary>
    [HttpPost("{tenantId:guid}/members/{memberId:guid}/clinics/{clinicId:guid}")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(AssignTenantMemberClinicResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignMemberClinic(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid memberId,
        [FromRoute] Guid clinicId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new AssignTenantMemberClinicCommand(tenantId, memberId, clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant paneli: üyeden bu kiracının kliniğini kaldırır (Faz 4B). Idempotent (<c>alreadyRemoved</c>).
    /// Self-protect: çağıran kendi üzerinden klinik çıkaramaz (<c>Clinics.SelfClinicRemoveForbidden</c>).
    /// Pasif klinik üzerinde de çalışır (yanlış atamayı temizlemek için). Son-klinik koruması ve
    /// session/refresh revoke bu fazda kapsam dışıdır. Yetki: <c>Tenants.InviteCreate</c>.
    /// </summary>
    [HttpDelete("{tenantId:guid}/members/{memberId:guid}/clinics/{clinicId:guid}")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(RemoveTenantMemberClinicResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMemberClinic(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid memberId,
        [FromRoute] Guid clinicId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new RemoveTenantMemberClinicCommand(tenantId, memberId, clinicId), ct);
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

    /// <summary>
    /// Tenant paneli: tek davet detayı. Tenant-scoped; davetin <c>TenantId</c>'si route ve JWT <c>tenant_id</c> ile eşleşmelidir.
    /// Ham token asla dönmez (yalnız create/resend yanıtında bir kez görülür). Yetki: <c>Tenants.InviteCreate</c>.
    /// </summary>
    [HttpGet("{tenantId:guid}/invites/{inviteId:guid}")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(TenantInviteDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInviteById(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid inviteId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetTenantInviteByIdQuery(tenantId, inviteId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant paneli: bekleyen daveti iptal eder (Revoked). Idempotent: zaten iptal edilmişse
    /// 200 OK + <c>alreadyCancelled=true</c> döner. Accepted davet iptal edilemez (409 benzeri iş kuralı).
    /// Read-only/cancelled abonelikler için abonelik guard'ı engelleyebilir. Yetki: <c>Tenants.InviteCreate</c>.
    /// </summary>
    [HttpPost("{tenantId:guid}/invites/{inviteId:guid}/cancel")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [ProducesResponseType(typeof(CancelTenantInviteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelInvite(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid inviteId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CancelTenantInviteCommand(tenantId, inviteId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant paneli: bekleyen daveti yeniden üretir. Aynı davet kaydı üzerinde yeni token hash ve yeni expiry yazılır
    /// (Id değişmez; <c>CreatedAtUtc</c> korunur). Yalnızca Pending davet için çalışır. Ham token yanıtta bir kez döner.
    /// Create akışı değişmez; duplicate-pending kuralı aynı kayıt güncellendiği için tetiklenmez. Yetki: <c>Tenants.InviteCreate</c>.
    /// </summary>
    [HttpPost("{tenantId:guid}/invites/{inviteId:guid}/resend")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ResendTenantInviteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendInvite(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid inviteId,
        [FromBody] ResendTenantInviteBody? body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ResendTenantInviteCommand(tenantId, inviteId, body?.ExpiresAtUtc), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Tenant-scoped kurum (tenant) ayarlarını günceller. Yetki: <c>Tenants.InviteCreate</c>;
    /// JWT <c>tenant_id</c> route <c>tenantId</c> ile aynı olmalı. Global admin
    /// <c>POST /api/v1/tenants</c> yüzeyine dokunmaz.
    /// </summary>
    /// <remarks>Route tenantId kaynak doğrudur; body.tenantId verilmişse route ile aynı olmalıdır.</remarks>
    [HttpPut("{tenantId:guid}/settings")]
    [Authorize(Policy = PermissionCatalog.Tenants.InviteCreate)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TenantDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSettings(
        [FromRoute] Guid tenantId,
        [FromBody] UpdateTenantSettingsBody? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Result<TenantDetailDto>.Failure(
                    "Tenants.Settings.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.TenantId is { } bodyTenantId && bodyTenantId != Guid.Empty && bodyTenantId != tenantId)
        {
            return Result<TenantDetailDto>.Failure(
                    "Tenants.RouteIdMismatch",
                    "Route tenantId ile body tenantId uyuşmuyor.")
                .ToActionResult(this);
        }

        var result = await _mediator.Send(new UpdateTenantSettingsCommand(tenantId, body.Name), ct);
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
