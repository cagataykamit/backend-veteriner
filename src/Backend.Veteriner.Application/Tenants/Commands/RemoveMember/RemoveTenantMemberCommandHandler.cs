using System.Text.Json;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMember;

/// <summary>
/// Kiracıdan üye çıkarma: <see cref="UserTenant"/> silinir; tenant içi <see cref="Backend.Veteriner.Domain.Clinics.UserClinic"/> kayıtları silinir;
/// yalnızca <see cref="InviteAssignableOperationClaimsCatalog"/> içindeki operation claim'ler kullanıcıdan kaldırılır (global/platform claim'lere dokunulmaz).
/// </summary>
/// <remarks>
/// Read-only / cancelled tenant durumunda merkezi <see cref="Common.Behaviors.TenantSubscriptionWriteGuardBehavior{TRequest, TResponse}"/> bu command'ı önce keser.
/// </remarks>
public sealed class RemoveTenantMemberCommandHandler
    : IRequestHandler<RemoveTenantMemberCommand, Result<RemoveTenantMemberResultDto>>
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IUserTenantRepository _userTenantRepo;
    private readonly IReadRepository<UserTenant> _userTenantsRead;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<OperationClaim> _claimsRead;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IPermissionCacheInvalidator _cacheInvalidator;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IUnitOfWork _uow;

    public RemoveTenantMemberCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IUserTenantRepository userTenantRepo,
        IReadRepository<UserTenant> userTenantsRead,
        IUserClinicRepository userClinics,
        IReadRepository<OperationClaim> claimsRead,
        IUserOperationClaimRepository userOperationClaims,
        IPermissionCacheInvalidator cacheInvalidator,
        IAuditLogWriter auditLogWriter,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _userTenantRepo = userTenantRepo;
        _userTenantsRead = userTenantsRead;
        _userClinics = userClinics;
        _claimsRead = claimsRead;
        _userOperationClaims = userOperationClaims;
        _cacheInvalidator = cacheInvalidator;
        _auditLogWriter = auditLogWriter;
        _uow = uow;
    }

    public async Task<Result<RemoveTenantMemberResultDto>> Handle(RemoveTenantMemberCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<RemoveTenantMemberResultDto>.Failure(
                "Auth.PermissionDenied",
                "Kiracıdan üye çıkarmak için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<RemoveTenantMemberResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<RemoveTenantMemberResultDto>.Failure(
                "Tenants.AccessDenied",
                "Üye çıkarma yalnızca oturumdaki kiracı bağlamında yapılabilir.");
        }

        if (_clientContext.UserId is { } callerId && callerId == request.MemberId)
        {
            return Result<RemoveTenantMemberResultDto>.Failure(
                "TenantMembers.CannotRemoveSelf",
                "Kullanıcı kendini kiracıdan çıkaramaz; başka bir yönetici işlemi yapmalıdır.");
        }

        if (!await _userTenantRepo.ExistsAsync(request.MemberId, request.TenantId, ct))
        {
            return Result<RemoveTenantMemberResultDto>.Failure(
                "TenantMembers.NotFound",
                "Üye bu kiracıda bulunamadı.");
        }

        var totalMembers = await _userTenantsRead.CountAsync(
            new UserTenantsByTenantCountSpec(request.TenantId), ct);
        if (totalMembers <= 1)
        {
            return Result<RemoveTenantMemberResultDto>.Failure(
                "TenantMembers.CannotRemoveSoleMember",
                "Kiracıda tek üye kaldığı için çıkarılamaz; kiracı boş kalmamalıdır.");
        }

        var adminClaim = await _claimsRead.FirstOrDefaultAsync(new OperationClaimByNameSpec("admin"), ct);
        if (adminClaim is not null)
        {
            var adminCount = await _userTenantRepo.CountMembersHavingOperationClaimAsync(
                request.TenantId, adminClaim.Id, ct);
            var targetIsAdmin = await _userOperationClaims.ExistsAsync(request.MemberId, adminClaim.Id, ct);
            var adminsAfterRemoval = targetIsAdmin ? adminCount - 1 : adminCount;
            var membersAfterRemoval = totalMembers - 1;

            if (membersAfterRemoval > 0 && adminsAfterRemoval == 0)
            {
                return Result<RemoveTenantMemberResultDto>.Failure(
                    "TenantMembers.CannotRemoveLastAdmin",
                    "Bu işlem kiracıda hiç yönetici (Admin rolü) kalmamasına yol açar; son yönetici çıkarılamaz.");
            }
        }

        var clinics = await _userClinics.ListAccessibleClinicsAsync(
            request.MemberId, request.TenantId, isActive: null, ct);
        foreach (var c in clinics)
            await _userClinics.RemoveAsync(request.MemberId, c.Id, ct);

        var claimDetails = await _userOperationClaims.GetDetailsByUserIdAsync(request.MemberId, ct);
        foreach (var d in claimDetails)
        {
            if (!InviteAssignableOperationClaimsCatalog.IsAssignableName(d.OperationClaimName))
                continue;

            await _userOperationClaims.RemoveAsync(request.MemberId, d.OperationClaimId, ct);
        }

        var removed = await _userTenantRepo.TryRemoveMembershipAsync(request.MemberId, request.TenantId, ct);
        if (!removed)
        {
            return Result<RemoveTenantMemberResultDto>.Failure(
                "TenantMembers.RemoveFailed",
                "Kiracı üyeliği kaldırılamadı (beklenmeyen durum).");
        }

        await _uow.SaveChangesAsync(ct);

        var auditPayload = JsonSerializer.Serialize(
            new { request.TenantId, MemberUserId = request.MemberId },
            AuditJson);

        await _auditLogWriter.WriteAsync(
            new AuditLogEntry(
                ActorUserId: _clientContext.UserId,
                Action: "Tenants.MemberRemoved",
                TargetType: nameof(UserTenant),
                TargetId: $"{request.TenantId}:{request.MemberId}",
                Success: true,
                FailureReason: null,
                Route: _clientContext.Path,
                HttpMethod: _clientContext.Method,
                IpAddress: _clientContext.IpAddress,
                UserAgent: _clientContext.UserAgent,
                CorrelationId: _clientContext.CorrelationId,
                RequestName: nameof(RemoveTenantMemberCommand),
                RequestPayload: auditPayload,
                OccurredAtUtc: DateTime.UtcNow),
            ct);

        _cacheInvalidator.InvalidateUser(request.MemberId);

        return Result<RemoveTenantMemberResultDto>.Success(new RemoveTenantMemberResultDto(request.MemberId));
    }
}
