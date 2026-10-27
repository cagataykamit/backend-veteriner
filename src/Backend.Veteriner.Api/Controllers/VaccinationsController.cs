using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Vaccinations.Commands.Create;
using Backend.Veteriner.Application.Vaccinations.Commands.Update;
using Backend.Veteriner.Application.Vaccinations.Commands.UpdateHbs;
using Backend.Veteriner.Application.Vaccinations.Queries.GetHbsData;
using Backend.Veteriner.Application.Hbs.Contracts;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Queries.GetById;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Pet bazlı aşı kayıtları. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vaccinations")]
[Produces("application/json")]
[Authorize]
public sealed class VaccinationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthorizationService _authorization;

    public VaccinationsController(IMediator mediator, ITenantContext tenantContext, IAuthorizationService authorization)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
        _authorization = authorization;
    }

    /// <summary>
    /// Aşı kaydı oluşturur. <c>productId</c> yoksa 201 + çıplak <c>id</c>. <c>productId</c> (ve opsiyonel <c>quantity</c>, <c>batchId</c>, <c>requestId</c>) verilirse
    /// aynı işlemde stoktan düşen uygulama da yazılır (ayrıca <c>Administrations.Create</c> gerekir); yanıt <see cref="VaccinationCreateResultDto"/>, aynı <c>requestId</c> tekrarında 200.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(VaccinationCreateResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(VaccinationCreateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateVaccinationCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        // Stok düşümü uygulama iznindedir: aşı iznine ek olarak aranır.
        if (cmd.ProductId.HasValue
            && !(await _authorization.AuthorizeAsync(User, PermissionCatalog.Administrations.Create)).Succeeded)
        {
            return Result.Failure(
                "Vaccinations.AdministrationForbidden",
                "Ürünle (stok düşümlü) aşı kaydı için uygulama kaydı yetkisi gerekir.").ToActionResult(this);
        }

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        if (cmd.ProductId is null)
        {
            return CreatedAtAction(
                nameof(GetById),
                new { version = HttpContext.RequestedApiVersion?.ToString() ?? "1.0", id = created.Id },
                created.Id);
        }

        if (!created.Created)
            return Ok(created);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.RequestedApiVersion?.ToString() ?? "1.0", id = created.Id },
            created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateVaccinationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("Vaccinations.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var cmd = new UpdateVaccinationCommand(
            id,
            body.ClinicId,
            body.PetId,
            body.ExaminationId,
            body.VaccineDefinitionId,
            body.Status,
            body.AppliedAtUtc,
            body.DueAtUtc,
            body.Notes);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(VaccinationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetVaccinationByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// HBS ATS ekranına girilecek hazır veri (alan alan, kopyalanabilir). Sahip kimlik no tam değeri yalnız <c>Clients.Update</c> ile.
    /// Her okuma denetim kaydına yazılır. Bkz. docs/HBS_ASSISTANT_API_CONTRACT.md bölüm 4.
    /// </summary>
    [HttpGet("{id:guid}/hbs-data")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(HbsDataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHbsData([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetVaccinationHbsDataQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// ATS geri kaydı (durum, aşı uygulama belgesi seri no, "Gerekmiyor" gerekçesi); yalnız uygulanmış aşıda. Kesinleşmiş muayeneye bağlı
    /// aşıda da çalışır; klinik içerik değişmez. Bkz. docs/HBS_ASSISTANT_API_CONTRACT.md bölüm 5.
    /// </summary>
    [HttpPatch("{id:guid}/hbs")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HbsEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateHbs([FromRoute] Guid id, [FromBody] UpdateHbsEntryBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new UpdateVaccinationHbsCommand(id, body.Status, body.ReferenceNumber, body.NotRequiredReason), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Sayfalı aşı listesi. <c>search</c> (veya <c>page.search</c>): aşı adı, notlar; müşteri metin alanlarında; hayvan ad/tür/ırk (hayvan listesi ile aynı küme). <c>onlyOverdue</c>: planlı ve vadesi geçmiş aşılar (Scheduled, DueAtUtc bugünün İstanbul gün başından önce; bugün vadeli aşı saati geçse de gecikmiş değildir, ADR-028); açıkken istek <c>status</c> değerini yok sayar; vefat eden hayvanın aşısı çıkmaz. <c>onlyReceivingCare</c>: yalnız bakımdaki (vefat etmemiş) hayvanların aşıları; verilmezse tümü. <c>examinationId</c>: yalnız bu muayeneye bağlı aşılar (kiracı/klinik kapsamı yine uygulanır).
    /// <c>clientId</c>: yalnız bu müşterinin hayvanlarının aşıları. <c>dateFromUtc</c>/<c>dateToUtc</c> (iki uç dahil): birleşik tarih — Uygulandı ise <c>AppliedAtUtc</c>, değilse <c>DueAtUtc</c>
    /// (<c>dueFromUtc</c>/<c>dueToUtc</c>, <c>appliedFromUtc</c>/<c>appliedToUtc</c> geriye uyumluluk için korunur). Tüm süzgeçler AND.
    /// Sıralama: <c>sort=dueAtUtc|appliedAtUtc</c> (<c>nextDueAtUtc</c> = <c>dueAtUtc</c>), <c>order=asc|desc</c>; tarihi boş kayıtlar sonda. Sort yoksa birleşik tarih azalan.
    /// <c>hbsStatus</c>: HBS durumu süzgeci; yalnız takipteki (uygulanmış, içe aktarılmamış) aşılar eşleşir. Satırda <c>hbsStatus</c> takip dışı aşıda null.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(PagedResult<VaccinationListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] VaccinationStatus? status = null,
        [FromQuery] DateTime? dueFromUtc = null,
        [FromQuery] DateTime? dueToUtc = null,
        [FromQuery] DateTime? appliedFromUtc = null,
        [FromQuery] DateTime? appliedToUtc = null,
        [FromQuery] bool onlyOverdue = false,
        [FromQuery] Guid? examinationId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        [FromQuery] bool onlyReceivingCare = false,
        [FromQuery] HbsEntryStatus? hbsStatus = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new GetVaccinationsListQuery(
                PageRequestQuery.WithMergedSearch(paging, search),
                clinicId,
                petId,
                status,
                dueFromUtc,
                dueToUtc,
                appliedFromUtc,
                appliedToUtc,
                onlyOverdue,
                examinationId,
                clientId,
                dateFromUtc,
                dateToUtc,
                onlyReceivingCare,
                hbsStatus),
            ct);
        return result.ToActionResult(this);
    }
}
