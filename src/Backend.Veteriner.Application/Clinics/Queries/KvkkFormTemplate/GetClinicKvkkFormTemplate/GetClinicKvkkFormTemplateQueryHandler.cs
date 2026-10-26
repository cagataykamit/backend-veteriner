using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clinics.Queries.KvkkFormTemplate.GetClinicKvkkFormTemplate;

/// <summary>Klinik KVKK şablonu (açık rıza veya aydınlatma); o türde kayıt yoksa taslak varsayılan metin döner (ADR-021, ADR-024).</summary>
public sealed class GetClinicKvkkFormTemplateQueryHandler
    : IRequestHandler<GetClinicKvkkFormTemplateQuery, Result<ClinicKvkkFormTemplateDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicKvkkFormTemplate> _templates;

    public GetClinicKvkkFormTemplateQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicKvkkFormTemplate> templates)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
        _templates = templates;
    }

    public async ValueTask<Result<ClinicKvkkFormTemplateDto>> Handle(
        GetClinicKvkkFormTemplateQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
        if (clinic is null)
            return Result<ClinicKvkkFormTemplateDto>.Failure("Clinics.NotFound", "Klinik bulunamadı.");

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct)
            && !await _userClinics.ExistsAsync(userId, clinic.Id, ct))
        {
            return Result<ClinicKvkkFormTemplateDto>.Failure(
                "Clinics.AccessDenied",
                "Bu klinik için atanmış üyeliğiniz yok.");
        }

        var row = await _templates.FirstOrDefaultAsync(
            new ClinicKvkkFormTemplateByClinicSpec(tenantId, request.ClinicId, request.Kind), ct);

        return Result<ClinicKvkkFormTemplateDto>.Success(row is null
            ? ClinicKvkkFormTemplateDto.Default(request.ClinicId, request.Kind)
            : ClinicKvkkFormTemplateDto.From(row));
    }
}
