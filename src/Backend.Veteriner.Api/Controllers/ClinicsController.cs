using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Commands.Activate;
using Backend.Veteriner.Application.Clinics.Commands.AppointmentSettings.UpdateClinicAppointmentSettings;
using Backend.Veteriner.Application.Clinics.Commands.Create;
using Backend.Veteriner.Application.Clinics.Commands.Deactivate;
using Backend.Veteriner.Application.Clinics.Commands.KvkkFormTemplate.UpdateClinicKvkkFormTemplate;
using Backend.Veteriner.Application.Clinics.Commands.TaxpayerSettings.UpdateClinicTaxpayerSettings;
using Backend.Veteriner.Application.Clinics.Commands.Update;
using Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Queries.AppointmentSettings.GetClinicAppointmentSettings;
using Backend.Veteriner.Application.Clinics.Queries.GetById;
using Backend.Veteriner.Application.Clinics.Queries.GetList;
using Backend.Veteriner.Application.Clinics.Queries.KvkkFormTemplate.GetClinicKvkkFormTemplate;
using Backend.Veteriner.Application.Clinics.Queries.TaxpayerSettings.GetClinicTaxpayerSettings;
using Backend.Veteriner.Application.Clinics.Queries.WorkingHours.GetClinicWorkingHours;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateRoom;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateWard;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetRoomActive;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetWardActive;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateRoom;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateWard;
using Backend.Veteriner.Application.Hospitalizations.Wards.Queries.GetClinicWardsWithRooms;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik yönetimi. Kiracı JWT <c>tenant_id</c>; handler’da <see cref="ITenantContext"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Clinics.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateClinicCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new
            {
                version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0",
                id
            },
            id);
    }

    /// <summary>Klinik haftalık çalışma saatleri. Kayıt yoksa varsayılan program döner.</summary>
    /// <remarks>
    /// Read yetkisi composite "any-of": <c>Clinics.Read</c>, <c>Clinics.Update</c>,
    /// <c>Appointments.Create</c> veya <c>Appointments.Reschedule</c> permission'larından en az biri.
    /// Sekreter / Veteriner gibi Clinics.Read'e sahip olmayan ama randevu oluşturan rollerin
    /// frontend'de gerçek çalışma saatlerini görebilmesi için.
    /// </remarks>
    [HttpGet("{id:guid}/working-hours")]
    [Authorize(Policy = AuthorizationPolicyNames.ClinicsSchedulingRead)]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicWorkingHourDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkingHours([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicWorkingHoursQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik haftalık çalışma saatlerini kaydeder (tam 7 gün).</summary>
    [HttpPut("{id:guid}/working-hours")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicWorkingHourDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutWorkingHours(
        [FromRoute] Guid id,
        [FromBody] UpdateClinicWorkingHoursRequest? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Result<IReadOnlyList<ClinicWorkingHourDto>>.Failure(
                    "Clinics.WorkingHours.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new UpdateClinicWorkingHoursCommand(id, body.Items), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik randevu varsayılan ayarları. Kayıt yoksa default değerler döner.</summary>
    /// <remarks>
    /// Read yetkisi composite "any-of" (working-hours ile aynı): <c>Clinics.Read</c>, <c>Clinics.Update</c>,
    /// <c>Appointments.Create</c> veya <c>Appointments.Reschedule</c> permission'larından en az biri.
    /// </remarks>
    [HttpGet("{id:guid}/appointment-settings")]
    [Authorize(Policy = AuthorizationPolicyNames.ClinicsSchedulingRead)]
    [ProducesResponseType(typeof(ClinicAppointmentSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAppointmentSettings([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClinicAppointmentSettingsQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik randevu varsayılan ayarlarını kaydeder.</summary>
    [HttpPut("{id:guid}/appointment-settings")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicAppointmentSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutAppointmentSettings(
        [FromRoute] Guid id,
        [FromBody] UpdateClinicAppointmentSettingsRequest? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Result<ClinicAppointmentSettingsDto>.Failure(
                    "Clinics.AppointmentSettings.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new UpdateClinicAppointmentSettingsCommand(
            id,
            body.DefaultAppointmentDurationMinutes,
            body.SlotIntervalMinutes,
            body.AllowOverlappingAppointments), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Klinik KVKK açık rıza formu şablon metni (ADR-021). Kayıt yoksa yer tutucu varsayılan metin döner (<c>isDefault=true</c>).
    /// Yer tutucular istemcide doldurulur. Bkz. docs/KVKK_CONSENT_API_CONTRACT.md.
    /// </summary>
    /// <remarks>Okuma yetkisi composite "any-of": <c>Clinics.Read</c>, <c>Clinics.Update</c> veya <c>Clients.Read</c>.</remarks>
    [HttpGet("{id:guid}/kvkk-form-template")]
    [Authorize(Policy = AuthorizationPolicyNames.ClinicsKvkkFormRead)]
    [ProducesResponseType(typeof(ClinicKvkkFormTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetKvkkFormTemplate([FromRoute] Guid id, CancellationToken ct)
        => GetKvkkTemplate(id, ClinicKvkkFormKind.Consent, ct);

    /// <summary>
    /// Klinik KVKK form şablon metnini kaydeder. Kayıt yokken <c>rowVersion</c> boş, varken GET'teki değer; uyuşmazlık 409.
    /// </summary>
    [HttpPut("{id:guid}/kvkk-form-template")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicKvkkFormTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> PutKvkkFormTemplate(
        [FromRoute] Guid id,
        [FromBody] UpdateClinicKvkkFormTemplateRequest? body,
        CancellationToken ct)
        => PutKvkkTemplate(id, ClinicKvkkFormKind.Consent, body, ct);

    /// <summary>
    /// Klinik KVKK aydınlatma metni şablonu (ADR-024). Açık rıza formu şablonuyla aynı sözleşme, izin ve sürüm kuralı;
    /// kayıt yoksa taslak varsayılan aydınlatma metni döner (<c>isDefault=true</c>). Bkz. docs/KVKK_CONSENT_API_CONTRACT.md.
    /// </summary>
    /// <remarks>Okuma yetkisi composite "any-of": <c>Clinics.Read</c>, <c>Clinics.Update</c> veya <c>Clients.Read</c>.</remarks>
    [HttpGet("{id:guid}/kvkk-disclosure-template")]
    [Authorize(Policy = AuthorizationPolicyNames.ClinicsKvkkFormRead)]
    [ProducesResponseType(typeof(ClinicKvkkFormTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetKvkkDisclosureTemplate([FromRoute] Guid id, CancellationToken ct)
        => GetKvkkTemplate(id, ClinicKvkkFormKind.Disclosure, ct);

    /// <summary>
    /// Klinik aydınlatma metni şablonunu kaydeder. Kayıt yokken <c>rowVersion</c> boş, varken GET'teki değer; uyuşmazlık 409.
    /// </summary>
    [HttpPut("{id:guid}/kvkk-disclosure-template")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicKvkkFormTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> PutKvkkDisclosureTemplate(
        [FromRoute] Guid id,
        [FromBody] UpdateClinicKvkkFormTemplateRequest? body,
        CancellationToken ct)
        => PutKvkkTemplate(id, ClinicKvkkFormKind.Disclosure, body, ct);

    /// <summary>
    /// Klinik mükellef ayarları (ADR-036 Dilim 1). Kayıt yoksa varsayılan döner (<c>isConfigured=false</c>, KDV %20).
    /// Bkz. docs/INVOICING_IDENTITY_API_CONTRACT.md §3.
    /// </summary>
    [HttpGet("{id:guid}/taxpayer-settings")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicTaxpayerSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaxpayerSettings([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClinicTaxpayerSettingsQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik mükellef ayarlarını kaydeder (tam değiştirme; kayıt yoksa oluşur). Audit'te mükellef kimliği maskelenir.</summary>
    [HttpPut("{id:guid}/taxpayer-settings")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicTaxpayerSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutTaxpayerSettings(
        [FromRoute] Guid id,
        [FromBody] UpdateClinicTaxpayerSettingsRequest? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Result<ClinicTaxpayerSettingsDto>.Failure(
                    "Clinics.TaxpayerSettings.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new UpdateClinicTaxpayerSettingsCommand(
            id,
            body.TaxpayerType,
            body.TaxIdentityNumber,
            body.TaxOffice,
            body.TaxpayerName,
            body.Province,
            body.District,
            body.AddressLine,
            body.DefaultVatRate,
            body.WithholdingRate), ct);
        return result.ToActionResult(this);
    }

    private async Task<IActionResult> GetKvkkTemplate(Guid id, ClinicKvkkFormKind kind, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClinicKvkkFormTemplateQuery(id, kind), ct);
        return result.ToActionResult(this);
    }

    private async Task<IActionResult> PutKvkkTemplate(
        Guid id,
        ClinicKvkkFormKind kind,
        UpdateClinicKvkkFormTemplateRequest? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                    "Clinics.KvkkFormTemplate.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new UpdateClinicKvkkFormTemplateCommand(id, kind, body.Text, body.RowVersion), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Geliş açarken sorumlu hekim seçimi: kliniğe atanmış, aktif ve <c>Veteriner</c> rolündeki kullanıcılar.
    /// Geliş doğrulaması (<c>responsibleVeterinarianUserId</c>) aynı kuralı kullanır.
    /// </summary>
    [HttpGet("{id:guid}/veterinarians")]
    [Authorize(Policy = PermissionCatalog.Visits.Create)]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicVeterinarianDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVeterinarians([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicVeterinariansQuery(id), ct);
        return result.ToActionResult(this);
    }

    // =========================================================
    // Yatış bölüm/oda tanımları (HOSP-002) — Ayarlar > Tanımlar; bkz. docs/HOSPITALIZATION_MODEL_API_CONTRACT.md §10.
    // =========================================================

    /// <summary>Kliniğin yatış bölümleri ve odaları (aktif/pasif dahil; sıra, sonra ad). Yatış formu aktifleri süzer.</summary>
    /// <remarks>Okuma yetkisi composite "any-of": <c>Hospitalizations.Read</c>, <c>Clinics.Read</c> veya <c>Clinics.Update</c>.</remarks>
    [HttpGet("{id:guid}/hospitalization-wards")]
    [Authorize(Policy = AuthorizationPolicyNames.ClinicsHospitalizationWardsRead)]
    [ProducesResponseType(typeof(IReadOnlyList<HospitalizationWardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHospitalizationWards([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicWardsWithRoomsQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Yeni yatış bölümü; aktif bölümler arasında ad benzersizdir (409 <c>HospitalizationWards.DuplicateName</c>).</summary>
    [HttpPost("{id:guid}/hospitalization-wards")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateHospitalizationWard(
        [FromRoute] Guid id,
        [FromBody] CreateHospitalizationWardBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CreateWardCommand(id, body.Name, body.SortOrder), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(GetHospitalizationWards),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            result.Value);
    }

    /// <summary>Bölüm adı ve sırası; yanıt bölümün güncel hali (odalarıyla).</summary>
    [HttpPut("{id:guid}/hospitalization-wards/{wardId:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HospitalizationWardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateHospitalizationWard(
        [FromRoute] Guid id,
        [FromRoute] Guid wardId,
        [FromBody] UpdateHospitalizationWardBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != wardId)
            return Result.Failure("HospitalizationWards.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var result = await _mediator.Send(new UpdateWardCommand(id, wardId, body.Name, body.SortOrder), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bölümü pasife alır (idempotent). Pasif bölüm yeni yatışa atanamaz; mevcut yatışlar etkilenmez.</summary>
    [HttpPost("{id:guid}/hospitalization-wards/{wardId:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(HospitalizationWardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeactivateHospitalizationWard([FromRoute] Guid id, [FromRoute] Guid wardId, CancellationToken ct)
        => SetHospitalizationWardActive(id, wardId, isActive: false, ct);

    /// <summary>Bölümü yeniden etkinleştirir (idempotent); ad çakışırsa 409 <c>HospitalizationWards.DuplicateName</c>.</summary>
    [HttpPost("{id:guid}/hospitalization-wards/{wardId:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(HospitalizationWardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ActivateHospitalizationWard([FromRoute] Guid id, [FromRoute] Guid wardId, CancellationToken ct)
        => SetHospitalizationWardActive(id, wardId, isActive: true, ct);

    private async Task<IActionResult> SetHospitalizationWardActive(Guid id, Guid wardId, bool isActive, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new SetWardActiveCommand(id, wardId, isActive), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bölüme yeni oda/kafes; aynı bölümün aktif odaları arasında ad benzersizdir (409 <c>HospitalizationRooms.DuplicateName</c>).</summary>
    [HttpPost("{id:guid}/hospitalization-wards/{wardId:guid}/rooms")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateHospitalizationRoom(
        [FromRoute] Guid id,
        [FromRoute] Guid wardId,
        [FromBody] CreateHospitalizationRoomBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CreateRoomCommand(id, wardId, body.Name, body.SortOrder), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(GetHospitalizationWards),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            result.Value);
    }

    /// <summary>Oda adı ve sırası; oda bölümler arası taşınmaz. Yanıt odanın güncel hali.</summary>
    [HttpPut("{id:guid}/hospitalization-wards/{wardId:guid}/rooms/{roomId:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(HospitalizationRoomDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateHospitalizationRoom(
        [FromRoute] Guid id,
        [FromRoute] Guid wardId,
        [FromRoute] Guid roomId,
        [FromBody] UpdateHospitalizationRoomBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != roomId)
            return Result.Failure("HospitalizationRooms.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var result = await _mediator.Send(new UpdateRoomCommand(id, wardId, roomId, body.Name, body.SortOrder), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Odayı pasife alır (idempotent). Pasif oda yeni yatışa atanamaz; odadaki mevcut yatışlar etkilenmez.</summary>
    [HttpPost("{id:guid}/hospitalization-wards/{wardId:guid}/rooms/{roomId:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(HospitalizationRoomDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeactivateHospitalizationRoom(
        [FromRoute] Guid id, [FromRoute] Guid wardId, [FromRoute] Guid roomId, CancellationToken ct)
        => SetHospitalizationRoomActive(id, wardId, roomId, isActive: false, ct);

    /// <summary>Odayı yeniden etkinleştirir (idempotent); ad çakışırsa 409 <c>HospitalizationRooms.DuplicateName</c>.</summary>
    [HttpPost("{id:guid}/hospitalization-wards/{wardId:guid}/rooms/{roomId:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(HospitalizationRoomDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ActivateHospitalizationRoom(
        [FromRoute] Guid id, [FromRoute] Guid wardId, [FromRoute] Guid roomId, CancellationToken ct)
        => SetHospitalizationRoomActive(id, wardId, roomId, isActive: true, ct);

    private async Task<IActionResult> SetHospitalizationRoomActive(Guid id, Guid wardId, Guid roomId, bool isActive, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new SetRoomActiveCommand(id, wardId, roomId, isActive), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik detayı.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kiracıya göre sayfalı klinik listesi.</summary>
    /// <remarks><c>PageRequest.search</c>, <c>sort</c> ve <c>order</c> şu an işlenmez.</remarks>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(PagedResult<ClinicListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList([FromQuery] PageRequest page, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(new GetClinicsListQuery(paging), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik bilgilerini (ad, şehir, iletişim/profil alanları) günceller.</summary>
    /// <remarks>Route id kaynak doğrudur; body.id verilmişse route ile aynı olmalıdır.</remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateClinicBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bodyId && bodyId != Guid.Empty && bodyId != id)
            return Result.Failure("Clinics.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        var cmd = new UpdateClinicCommand(
            id,
            body.Name,
            body.City,
            body.Phone,
            body.Email,
            body.Address,
            body.Description);
        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kliniği pasife alır (idempotent).</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(DeactivateClinicResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new DeactivateClinicCommand(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kliniği yeniden aktifleştirir (idempotent).</summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(ActivateClinicResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ActivateClinicCommand(id), ct);
        return result.ToActionResult(this);
    }
}
