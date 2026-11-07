using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VaccinationSchedules;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record VaccinationScheduleItemBody(
    Guid SpeciesId,
    Guid VaccineDefinitionId,
    int StartAgeWeeks,
    int DoseCount,
    int? DoseIntervalWeeks,
    int? RepeatIntervalMonths,
    string? Notes);

public sealed record ApplyVaccinationPlanBody(Guid ClinicId, IReadOnlyList<VaccinationPlanApplyItem>? Items);

/// <summary>
/// Otomatik aşı sihirbazı (TIER-005; docs/VACCINATION_SCHEDULE_API_CONTRACT.md): klinik aşı takvimi ve hayvan için önerilen plan.
/// Takvim paketten bağımsız okunur; takvim yazma, öneri ve onay paket ister (403 <c>Subscriptions.FeatureNotInPlan</c>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vaccination-schedule")]
[Produces("application/json")]
[Authorize]
public sealed class VaccinationScheduleController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public VaccinationScheduleController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet("items")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Read)]
    [ProducesResponseType(typeof(VaccinationScheduleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetItems([FromQuery] Guid? speciesId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetVaccinationScheduleQuery(speciesId), ct)).ToActionResult(this);
    }

    [HttpPost("items")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VaccinationScheduleItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateItem([FromBody] VaccinationScheduleItemBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var command = new CreateVaccinationScheduleItemCommand(
            body.SpeciesId, body.VaccineDefinitionId, body.StartAgeWeeks, body.DoseCount, body.DoseIntervalWeeks, body.RepeatIntervalMonths, body.Notes);
        return (await _mediator.Send(command, ct)).ToActionResult(this);
    }

    /// <summary>Tür ve aşı değişmez; gövdedeki <c>speciesId</c> ve <c>vaccineDefinitionId</c> yok sayılır.</summary>
    [HttpPut("items/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VaccinationScheduleItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateItem([FromRoute] Guid id, [FromBody] VaccinationScheduleItemBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var command = new UpdateVaccinationScheduleItemCommand(
            id, body.StartAgeWeeks, body.DoseCount, body.DoseIntervalWeeks, body.RepeatIntervalMonths, body.Notes);
        return (await _mediator.Send(command, ct)).ToActionResult(this);
    }

    [HttpDelete("items/{id:guid}")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteItem([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new DeleteVaccinationScheduleItemCommand(id), ct)).ToActionResult(this);
    }

    /// <summary>Başlangıç şablonu: yalnız takvimde olmayan tür + aşı satırları eklenir.</summary>
    [HttpPost("starter-template")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Update)]
    [ProducesResponseType(typeof(VaccinationScheduleTemplateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LoadStarterTemplate(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new LoadVaccinationScheduleStarterTemplateCommand(), ct)).ToActionResult(this);
    }

    /// <summary>Takvimden önerilen plan; kayıt oluşturmaz.</summary>
    [HttpGet("pets/{petId:guid}/plan")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(VaccinationPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlan([FromRoute] Guid petId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetVaccinationPlanQuery(petId), ct)).ToActionResult(this);
    }

    /// <summary>Onaylanan dozlar planlı aşı kaydı olur (tümü ya da hiçbiri).</summary>
    [HttpPost("pets/{petId:guid}/plan")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(VaccinationPlanApplyResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApplyPlan([FromRoute] Guid petId, [FromBody] ApplyVaccinationPlanBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new ApplyVaccinationPlanCommand(petId, body.ClinicId, body.Items), ct)).ToActionResult(this);
    }
}
