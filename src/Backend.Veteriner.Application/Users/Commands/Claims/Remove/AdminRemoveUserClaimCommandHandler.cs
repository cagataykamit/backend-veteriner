using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Users.Commands.Claims.Remove;

/// <summary>
/// Admin: Kullanıcıdan rol (OperationClaim) kaldırır.
///
/// Kurumsal davranış:
/// - Repository sadece state değiştirir.
/// - Commit sınırı UnitOfWork'tür.
/// - Başarılı commit sonrası yalnız ilgili kullanıcının permission cache'i düşürülür.
/// </summary>
public sealed class AdminRemoveUserClaimCommandHandler : IRequestHandler<AdminRemoveUserClaimCommand>
{
    private readonly IUserOperationClaimRepository _repo;
    private readonly IPermissionCacheInvalidator _cache;
    private readonly IUnitOfWork _uow;

    public AdminRemoveUserClaimCommandHandler(
        IUserOperationClaimRepository repo,
        IPermissionCacheInvalidator cache,
        IUnitOfWork uow)
    {
        _repo = repo;
        _cache = cache;
        _uow = uow;
    }

    public async Task Handle(AdminRemoveUserClaimCommand request, CancellationToken ct)
    {
        // 1) Rol ilişkisini kaldır
        await _repo.RemoveAsync(request.UserId, request.OperationClaimId, ct);

        // 2) Değişiklikleri commit et
        await _uow.SaveChangesAsync(ct);

        // 3) Kullanıcının permission cache'ini düşür
        _cache.InvalidateUser(request.UserId);
    }
}