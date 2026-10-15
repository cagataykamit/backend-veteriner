using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Commands.AddAddendum;
using Backend.Veteriner.Application.Examinations.Commands.Create;
using Backend.Veteriner.Application.Examinations.Commands.Finalize;
using Backend.Veteriner.Application.Examinations.Commands.Update;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Queries.GetAddenda;
using Backend.Veteriner.Application.Examinations.Queries.GetById;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Application.Examinations.Queries.GetRelatedSummary;
using Backend.Veteriner.Application.ExaminationTemplates.Commands.Apply;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Muayene (klinik) kayıtları. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/examinations")]
[Produces("application/json")]
[Authorize]
public sealed class ExaminationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ExaminationsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Examinations.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ExaminationWriteResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    /// <remarks>Kanonik alan <c>visitReason</c>. Eski istemciler <c>complaint</c> gönderebilir (yalnızca <c>visitReason</c> boşken kullanılır). Yanıtta yalnızca <c>visitReason</c> döner.</remarks>
    public async Task<IActionResult> Create([FromBody] CreateExaminationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var visitReason = ExaminationVisitReasonResolver.Resolve(body.VisitReason, body.Complaint);
        var findings = body.Findings ?? string.Empty;

        var cmd = new CreateExaminationCommand(
            body.ClinicId,
            body.PetId,
            body.AppointmentId,
            body.ExaminedAtUtc,
            visitReason,
            findings,
            body.Assessment,
            body.Notes,
            body.Anamnesis,
            body.Plan,
            body.WeightKg,
            body.TemperatureC,
            body.HeartRateBpm,
            body.RespiratoryRatePerMin,
            body.VitalsMeasuredAtUtc,
            body.VisitId);

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var writeResult = result.Value!;
        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = writeResult.Id },
            writeResult);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Examinations.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ExaminationWriteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    /// <remarks>Kanonik alan <c>visitReason</c>. Eski istemciler <c>complaint</c> gönderebilir. Yanıtta yalnızca <c>visitReason</c>.</remarks>
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateExaminationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("Examinations.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var visitReason = ExaminationVisitReasonResolver.Resolve(body.VisitReason, body.Complaint);

        var cmd = new UpdateExaminationCommand(
            id,
            body.ClinicId,
            body.PetId,
            body.AppointmentId,
            body.ExaminedAtUtc,
            visitReason,
            body.Findings,
            body.Assessment,
            body.Notes,
            body.RowVersion,
            body.Anamnesis,
            body.Plan,
            body.WeightKg,
            body.TemperatureC,
            body.HeartRateBpm,
            body.RespiratoryRatePerMin,
            body.VitalsMeasuredAtUtc,
            body.ClearVitals);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Muayeneyi kesinleştirir (Taslak → Kesinleşti); sonrasında içerik salt okunurdur, düzeltme yalnız ek not ile yapılır.
    /// </summary>
    [HttpPost("{id:guid}/finalize")]
    [Authorize(Policy = PermissionCatalog.Examinations.Finalize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ExaminationFinalizeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Finalize([FromRoute] Guid id, [FromBody] FinalizeExaminationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new FinalizeExaminationCommand(id, body.RowVersion), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Muayene şablonunu uygular (ADR-020 S4): metinleri mevcut içeriği silmeden birleştirir, satırları uygulama kaydı (stok düşer)
    /// veya hizmet kalemi olarak yazar; ücret önerisi ADR-018 akışıyla doğar. Tek transaction; kesinleşmiş muayeneye uygulanamaz.
    /// Şablon izninin yanında muayene güncelleme ve uygulama kaydı oluşturma izni de gerekir.
    /// </summary>
    [HttpPost("{id:guid}/apply-template")]
    [Authorize(Policy = PermissionCatalog.ExaminationTemplates.Apply)]
    [Authorize(Policy = PermissionCatalog.Examinations.Update)]
    [Authorize(Policy = PermissionCatalog.Administrations.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ExaminationTemplateApplyResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApplyTemplate([FromRoute] Guid id, [FromBody] ApplyExaminationTemplateBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ApplyExaminationTemplateCommand(id, body.TemplateId, body.RowVersion), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kesinleşmiş muayeneye gerekçeli, değişmez ek not ekler.</summary>
    [HttpPost("{id:guid}/addenda")]
    [Authorize(Policy = PermissionCatalog.Examinations.Addendum)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ExaminationAddendumDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddAddendum([FromRoute] Guid id, [FromBody] AddExaminationAddendumBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new AddExaminationAddendumCommand(id, body.Reason, body.Text), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(GetAddenda),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            result.Value);
    }

    /// <summary>Muayenenin ek notları, eskiden yeniye (sayfalama yok).</summary>
    [HttpGet("{id:guid}/addenda")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ExaminationAddendumDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAddenda([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetExaminationAddendaQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(ExaminationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetExaminationByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Muayene detayına bağlı tedavi, reçete, laboratuvar sonucu, yatış ve ödeme özetleri (tek yanıt).
    /// </summary>
    [HttpGet("{id:guid}/related-summary")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(ExaminationRelatedSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRelatedSummary([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetExaminationRelatedSummaryQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Sayfalı muayene listesi.
    /// Filtreler (hepsi AND): opsiyonel <c>clinicId</c>, <c>clientId</c> (müşterinin hayvanları), <c>petId</c>, <c>appointmentId</c>, <c>visitId</c>,
    /// <c>examinedOnLocalDate</c> (İstanbul takvim günü → UTC <c>[start,end)</c>) veya <c>dateFromUtc</c> (dahil) / <c>dateToUtc</c> (hariç) (<c>ExaminedAtUtc</c>).
    /// Metin araması: <c>search</c> veya <c>page.search</c> — başvuru nedeni, bulgular, değerlendirme, notlar; müşteri + hayvan metin eşlemesi ile pet id kümesi (hayvan listesi ile aynı örüntü). Arama doluysa mevcut LIKE/OR metin kuralları diğer filtrelerle AND birleşir.
    /// Sıralama: <c>sort=examinedAtUtc</c> (<c>examinationDateUtc</c> eş adı), <c>order=asc|desc</c>; sort yoksa en yeni üstte.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(PagedResult<ExaminationListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? appointmentId = null,
        [FromQuery] Guid? visitId = null,
        [FromQuery] DateOnly? examinedOnLocalDate = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        [FromQuery] Guid? clientId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(
            new GetExaminationsListQuery(
                PageRequestQuery.WithMergedSearch(paging, search),
                clinicId,
                petId,
                appointmentId,
                examinedOnLocalDate,
                dateFromUtc,
                dateToUtc,
                visitId,
                clientId),
            ct);
        return result.ToActionResult(this);
    }
}
