using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clients.Commands.SetKvkkConsent;

public sealed class SetClientKvkkConsentCommandHandler
    : IRequestHandler<SetClientKvkkConsentCommand, Result<ClientKvkkConsentDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Client> _clients;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicKvkkFormTemplate> _templates;
    private readonly IClinicVeterinarianReader _userNames;
    private readonly TimeProvider _timeProvider;

    public SetClientKvkkConsentCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        IRepository<Client> clients,
        IClinicContext clinicContext,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicKvkkFormTemplate> templates,
        IClinicVeterinarianReader userNames,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _tenants = tenants;
        _clients = clients;
        _clinicContext = clinicContext;
        _clinics = clinics;
        _templates = templates;
        _userNames = userNames;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ClientKvkkConsentDto>> Handle(SetClientKvkkConsentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientKvkkConsentDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClientKvkkConsentDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<ClientKvkkConsentDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<ClientKvkkConsentDto>.Failure("Tenants.TenantInactive", "Pasif kiracı için müşteri güncellenemez.");

        var client = await _clients.FirstOrDefaultAsync(new ClientForUpdateByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result<ClientKvkkConsentDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        if (client.IsWalkIn)
            return Result<ClientKvkkConsentDto>.Failure("Clients.WalkInReadOnly", "Peşin müşteri sistem kaydıdır; düzenlenemez.");

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Result<ClientKvkkConsentDto>.Failure("Clients.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

        var consentText = request.Status == KvkkConsentStatus.None
            ? null
            : await ResolveCurrentConsentTextAsync(tenantId, ct);

        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        var change = client.RecordKvkkConsent(
            request.Status,
            request.RecordedOn,
            today,
            userId,
            consentText?.TextVersion,
            consentText?.TextHash);
        if (!change.IsSuccess)
            return Result<ClientKvkkConsentDto>.Failure(change.Error);

        client.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _clients.UpdateAsync(client, ct);
            await _clients.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ClientKvkkConsentDto>.Failure(
                "Clients.ConcurrencyConflict",
                "Müşteri kaydı başka biri tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }

        string? recordedByName = null;
        if (client.KvkkConsentRecordedByUserId is { } recordedBy)
            (await _userNames.GetNamesAsync([recordedBy], ct)).TryGetValue(recordedBy, out recordedByName);

        return Result<ClientKvkkConsentDto>.Success(ClientKvkkConsentDto.From(client, recordedByName));
    }

    /// <summary>
    /// İsteğin kliniğinde şu an geçerli açık rıza metni (ADR-032): kayıtlı şablon ya da taslak varsayılan (sürüm 0);
    /// <c>GET /clinics/{id}/kvkk-form-template</c> ile aynı sürüm ve özet. Klinik bağlamı yoksa ya da klinik kiracıda
    /// bulunamazsa null (sürüm bilinmiyor).
    /// </summary>
    private async Task<ClinicKvkkFormTemplateDto?> ResolveCurrentConsentTextAsync(Guid tenantId, CancellationToken ct)
    {
        if (_clinicContext.ClinicId is not { } clinicId)
            return null;

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return null;

        var row = await _templates.FirstOrDefaultAsync(
            new ClinicKvkkFormTemplateByClinicSpec(tenantId, clinicId, ClinicKvkkFormKind.Consent), ct);
        return row is null
            ? ClinicKvkkFormTemplateDto.Default(clinicId, ClinicKvkkFormKind.Consent)
            : ClinicKvkkFormTemplateDto.From(row);
    }
}
