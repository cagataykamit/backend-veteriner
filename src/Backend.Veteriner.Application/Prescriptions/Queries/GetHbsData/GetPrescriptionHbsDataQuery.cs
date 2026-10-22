using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Hbs.Contracts;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Prescriptions.Queries.GetHbsData;

/// <summary>Reçetenin HBS e-Reçete ekranına girilecek hazır verisi (ADR-036 Dilim 2). Her okuma denetim kaydına yazılır.</summary>
public sealed record GetPrescriptionHbsDataQuery(Guid Id) : IRequest<Result<HbsDataDto>>, IAuditableRequest
{
    public string AuditAction => "Prescription.HbsData.Read";
    public string? AuditTarget => $"PrescriptionId={Id}";
}

/// <summary>Sahip kimlik no tam değeri yalnız <c>Clients.Update</c> izniyle; aksi hâlde maskeli.</summary>
public sealed class GetPrescriptionHbsDataQueryHandler : IRequestHandler<GetPrescriptionHbsDataQuery, Result<HbsDataDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IUserClinicRepository _userClinics;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IHbsSourceReader _sources;

    public GetPrescriptionHbsDataQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IUserOperationClaimRepository userOperationClaims,
        IUserClinicRepository userClinics,
        ICurrentUserPermissionChecker permissions,
        IHbsSourceReader sources)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _userOperationClaims = userOperationClaims;
        _userClinics = userClinics;
        _permissions = permissions;
        _sources = sources;
    }

    public async Task<Result<HbsDataDto>> Handle(GetPrescriptionHbsDataQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<HbsDataDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Result<HbsDataDto>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var source = await _sources.GetPrescriptionAsync(tenantId, request.Id, ct);
        if (source is null
            || !await HbsRecordReadAccess.CanReadAsync(_clinicContext, _userOperationClaims, _userClinics, userId, source.ClinicId, ct))
        {
            return Result<HbsDataDto>.Failure("Prescriptions.NotFound", "Reçete kaydı bulunamadı.");
        }

        return Result<HbsDataDto>.Success(HbsDataBuilder.ForPrescription(
            source,
            HbsDataBuilder.Entry(source.Hbs, isTracked: true),
            revealOwnerIdentity: _permissions.HasPermission(PermissionCatalog.Clients.Update)));
    }
}
