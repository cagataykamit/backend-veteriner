using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>Belge uçlarının ortak giriş denetimi: izin, kiracı, kullanıcı ve klinik kapsamı (istek kliniği aktif klinikle çelişemez).</summary>
public sealed class EDocumentAccess
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScope;
    private readonly ICurrentUserPermissionChecker _permissions;

    public EDocumentAccess(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScope,
        ICurrentUserPermissionChecker permissions)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _clinicContext = clinicContext;
        _clinicScope = clinicScope;
        _permissions = permissions;
    }

    /// <summary>İstekte klinik yoksa aktif klinik bağlamı kullanılır; ikisi de yoksa kapsam hatası.</summary>
    public async Task<Result<EDocumentCaller>> ResolveAsync(string permission, Guid? requestClinicId, CancellationToken ct)
    {
        var basics = Basics(permission);
        if (!basics.IsSuccess)
            return basics;

        if (ClinicContextRule.Conflicts(requestClinicId, _clinicContext.ClinicId))
            return Result<EDocumentCaller>.Failure(ClinicContextRule.Mismatch("EDocuments"));

        if ((requestClinicId ?? _clinicContext.ClinicId) is not { } clinicId)
        {
            return Result<EDocumentCaller>.Failure(
                "EDocuments.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        return await WithClinicAsync(basics.Value!, clinicId, ct);
    }

    /// <summary>Var olan belge için: belgenin kliniğine erişim denetlenir.</summary>
    public async Task<Result<EDocumentCaller>> ResolveForClinicAsync(string permission, Guid clinicId, CancellationToken ct)
    {
        var basics = Basics(permission);
        if (!basics.IsSuccess)
            return basics;
        if (_clinicContext.ClinicId is { } active && active != clinicId)
            return Result<EDocumentCaller>.Failure("EDocuments.NotFound", "Belge bulunamadı.");
        return await WithClinicAsync(basics.Value!, clinicId, ct);
    }

    /// <summary>Klinik ayarı (bağlantı) için: aktif klinik bağlamından bağımsız, atanmış klinik kuralı geçerli (mükellef ayarlarıyla aynı).</summary>
    public async Task<Result<EDocumentCaller>> ResolveClinicSettingsAsync(string permission, Guid clinicId, CancellationToken ct)
    {
        var basics = Basics(permission);
        if (!basics.IsSuccess)
            return basics;
        return await WithClinicAsync(basics.Value!, clinicId, ct);
    }

    public Result<EDocumentCaller> Basics(string permission)
    {
        if (!_permissions.HasPermission(permission))
            return Result<EDocumentCaller>.Failure("Auth.PermissionDenied", $"Bu işlem için {permission} yetkisi gerekir.");
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<EDocumentCaller>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        if (_clientContext.UserId is not { } userId)
            return Result<EDocumentCaller>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");
        return Result<EDocumentCaller>.Success(new EDocumentCaller(tenantId, userId, Guid.Empty));
    }

    private async Task<Result<EDocumentCaller>> WithClinicAsync(EDocumentCaller caller, Guid clinicId, CancellationToken ct)
    {
        var scope = await _clinicScope.ResolveAsync(caller.TenantId, clinicId, ct);
        if (!scope.IsSuccess)
            return Result<EDocumentCaller>.Failure(scope.Error);
        return Result<EDocumentCaller>.Success(caller with { ClinicId = clinicId });
    }
}

public sealed record EDocumentCaller(Guid TenantId, Guid UserId, Guid ClinicId);
