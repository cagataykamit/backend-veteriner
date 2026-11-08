using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.CustomDevelopment;
using Backend.Veteriner.Domain.CustomDevelopment;
using Backend.Veteriner.Domain.Imports;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record CreateCustomDevelopmentRequestBody(
    string? RequestTitle,
    string? RequestText,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CustomDevelopmentTargetKind? TargetKind,
    string? RequestTarget,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CustomDevelopmentPriority? Priority);

/// <summary>Geçiş desteği talebi (TIER-021). <c>meetingDays</c> ve <c>meetingTimes</c> bayrak listesi: <c>"Monday, Wednesday"</c>.</summary>
public sealed record CreateMigrationSupportRequestBody(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImportSourceProfile? SourceProgram,
    string? OtherProgramName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MigrationRecordVolume? RecordVolume,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MigrationMeetingDays? MeetingDays,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MigrationMeetingTimes? MeetingTimes,
    string? Note);

/// <summary>
/// Size özel geliştirme talepleri, kiracı tarafı (TIER-010; docs/CUSTOM_DEVELOPMENT_REQUESTS_API_CONTRACT.md). Liste her pakette;
/// yeni talep Kurumsal ister (403 <c>Subscriptions.FeatureNotInPlan</c>) ve açık talep sınırına bağlıdır (409 <c>CustomDevelopment.TooManyOpen</c>).
/// Durumu yalnız platform yöneticisi değiştirir (<see cref="CustomDevelopmentRequestsAdminController"/>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/custom-development-requests")]
[Produces("application/json")]
[Authorize(Policy = PermissionCatalog.CustomDevelopmentRequests.Manage)]
public sealed class CustomDevelopmentRequestsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public CustomDevelopmentRequestsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CustomDevelopmentRequestListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetCustomDevelopmentRequestsQuery(), ct)).ToActionResult(this);
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CustomDevelopmentRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCustomDevelopmentRequestBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var command = new CreateCustomDevelopmentRequestCommand(
            body.RequestTitle, body.RequestText, body.TargetKind, body.RequestTarget, body.Priority);
        return (await _mediator.Send(command, ct)).ToActionResult(this);
    }

    /// <summary>Geçiş desteği talepleri, pakete göre ücret bilgisi ve görüşme süresi (TIER-021). Her pakette açık.</summary>
    [HttpGet("migration-support")]
    [ProducesResponseType(typeof(MigrationSupportOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMigrationSupport(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetMigrationSupportQuery(), ct)).ToActionResult(this);
    }

    /// <summary>Geçiş desteği ve kurulum görüşmesi talebi (TIER-021). Her pakette; açık talep varsa 409 <c>CustomDevelopment.MigrationAlreadyOpen</c>.</summary>
    [HttpPost("migration-support")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CustomDevelopmentRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMigrationSupport([FromBody] CreateMigrationSupportRequestBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var command = new CreateMigrationSupportRequestCommand(
            body.SourceProgram, body.OtherProgramName, body.RecordVolume, body.MeetingDays, body.MeetingTimes, body.Note);
        return (await _mediator.Send(command, ct)).ToActionResult(this);
    }
}
