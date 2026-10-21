using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.QuickRegister;
using Backend.Veteriner.Application.Clients.Commands.SetKvkkConsent;
using Backend.Veteriner.Application.Clients.Commands.Update;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Queries.GetById;
using Backend.Veteriner.Application.Clients.Queries.GetBalance;
using Backend.Veteriner.Application.Clients.Queries.GetDataExport;
using Backend.Veteriner.Application.Clients.Queries.GetInvoicingIdentity;
using Backend.Veteriner.Application.Clients.Queries.GetList;
using Backend.Veteriner.Application.Clients.Queries.GetPaymentSummary;
using Backend.Veteriner.Application.Clients.Queries.GetRecentSummary;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Müşteri (hayvan sahibi) yönetimi. Kiracı JWT <c>tenant_id</c>; handler’da <see cref="ITenantContext"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clients")]
[Produces("application/json")]
[Authorize]
public sealed class ClientsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClientsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Clients.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClientCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateClientCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var dto = result.Value!;
        return CreatedAtAction(
            nameof(GetById),
            new
            {
                version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0",
                id = dto.Id
            },
            dto);
    }

    /// <summary>
    /// Randevusuz gelişte müşteri + hayvan tek işlemde (telefon zorunlu). Visit açmaz; frontend sonra
    /// <c>POST /visits</c> çağırır. <c>Clients.Create</c> ve <c>Pets.Create</c> ikisi de gerekir.
    /// </summary>
    [HttpPost("quick-register")]
    [Authorize(Policy = PermissionCatalog.Clients.Create)]
    [Authorize(Policy = PermissionCatalog.Pets.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(QuickRegisterClientResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> QuickRegister([FromBody] QuickRegisterClientCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var dto = result.Value!;
        return CreatedAtAction(
            nameof(GetById),
            new
            {
                version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0",
                id = dto.ClientId
            },
            dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clients.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateClientCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (cmd.Id != Guid.Empty && id != cmd.Id)
            return Result.Failure("Clients.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        cmd = cmd with { Id = id };

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// KVKK açık rıza formu sonucu (ADR-021): <c>Accepted</c> / <c>Declined</c> (tarih opsiyonel, verilmezse bugün; gelecek olamaz)
    /// veya yanlış girişin geri alınması için <c>None</c>. <c>rowVersion</c> zorunlu (GET /clients/{id}); eski sürüm 409
    /// <c>Clients.ConcurrencyConflict</c>. Hatırlatma tercihine dokunmaz. Bkz. docs/KVKK_CONSENT_API_CONTRACT.md.
    /// </summary>
    [HttpPut("{id:guid}/kvkk-consent")]
    [Authorize(Policy = PermissionCatalog.Clients.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClientKvkkConsentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetKvkkConsent([FromRoute] Guid id, [FromBody] SetClientKvkkConsentBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new SetClientKvkkConsentCommand(id, body.Status, body.RecordedOn, body.RowVersion), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clients.Read)]
    [ProducesResponseType(typeof(ClientDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClientByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Düzenleme formu için fatura kimliği, TCKN/VKN tam değerle (ADR-036). Yalnız <c>Clients.Update</c>; diğer okuma uçları maskeli döner.
    /// Her okuma audit'e yazılır (<c>Client.InvoicingIdentity.Read</c>). Bkz. docs/INVOICING_IDENTITY_API_CONTRACT.md.
    /// </summary>
    [HttpGet("{id:guid}/invoicing-identity")]
    [Authorize(Policy = PermissionCatalog.Clients.Update)]
    [ProducesResponseType(typeof(ClientInvoicingIdentityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoicingIdentity([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClientInvoicingIdentityQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Müşteri detay ekranı için son randevular ve son muayeneler (tek yanıt). Yalnız bu müşterinin hayvanlarına ait kayıtlar;
    /// aktif klinik bağlamı varsa sonuçlar bu kliniğe indirgenir. Sıra: en yeni tarih önce; her blok en fazla 10 kayıt.
    /// </summary>
    [HttpGet("{id:guid}/recent-summary")]
    [Authorize(Policy = PermissionCatalog.Clients.Read)]
    [ProducesResponseType(typeof(ClientRecentSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecentSummary([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClientRecentSummaryQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Müşteri ödeme özeti: toplamlar, para birimi kırılımı ve son ödemeler (aktif klinik bağlamı varsa bu kliniğe indirgenir).
    /// </summary>
    [HttpGet("{id:guid}/payment-summary")]
    [Authorize(Policy = PermissionCatalog.Clients.Read)]
    [ProducesResponseType(typeof(ClientPaymentSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentSummary([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClientPaymentSummaryQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Müşteri veri kopyası (ADR-032 madde 2, KVKK bilgi talebi): müşteri bilgileri, rıza, hastalar, randevular, muayene özetleri,
    /// aşılar, tedavi/reçete özeti, ücret/ödemeler (iptaller işaretli) ve toplu bakiye. Salt okunur; erişim audit'e yazılır
    /// (<c>Client.DataExport</c>). Aktif klinik bağlamı varsa bu kliniğe indirgenir; bölüm başına üst sınır vardır.
    /// Bkz. docs/CLIENT_DATA_EXPORT_API_CONTRACT.md.
    /// </summary>
    [HttpGet("{id:guid}/data-export")]
    [Authorize(Policy = PermissionCatalog.Clients.Read)]
    [ProducesResponseType(typeof(ClientDataExportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDataExport([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClientDataExportQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Müşteri bakiyesi: ücret toplamı, tahsis edilen, açık kalan; hasta/ziyaret kırılımı ve bağlanmamış eski ödemeler (aktif klinik bağlamı varsa bu kliniğe indirgenir).
    /// </summary>
    [HttpGet("{id:guid}/balance")]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(ClientBalanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBalance([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClientBalanceQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Sayfalı müşteri listesi. <c>search</c> (veya <c>page.search</c>) ad, e-posta ve telefon alanlarında arar (Türkçe harf/ad sırası/telefon biçimi toleranslı, bkz. docs/SEARCH_API_CONTRACT.md). <c>sort</c>/<c>order</c> işlenmez.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clients.Read)]
    [ProducesResponseType(typeof(PagedResult<ClientListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(new GetClientsListQuery(PageRequestQuery.WithMergedSearch(paging, search)), ct);
        return result.ToActionResult(this);
    }
}

/// <summary>PUT /api/v1/clients/{id}/kvkk-consent gövdesi.</summary>
public sealed class SetClientKvkkConsentBody
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public KvkkConsentStatus Status { get; init; }

    /// <summary>Yalnız <c>Accepted</c>/<c>Declined</c> ile; opsiyonel (yyyy-MM-dd), verilmezse bugün.</summary>
    public DateOnly? RecordedOn { get; init; }

    /// <summary>GET /clients/{id} yanıtındaki Base64 sürüm; zorunlu.</summary>
    public string? RowVersion { get; init; }
}
