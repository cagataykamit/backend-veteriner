using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using MediatR;

namespace Backend.Veteriner.Application.Users.Commands.Claims.Add;

/// <summary>
/// Admin: Kullanıcıya rol (OperationClaim) atar.
/// 
/// Kurumsal davranış:
/// - Idempotent çalışır; ilişki zaten varsa tekrar eklemez.
/// - Repository sadece state değiştirir.
/// - Commit noktası UnitOfWork'tür.
/// - Başarılı commit sonrası yalnız ilgili kullanıcının permission cache'i düşürülür.
/// </summary>
public sealed class AdminAddUserClaimCommandHandler : IRequestHandler<AdminAddUserClaimCommand>
{
    private readonly IUserOperationClaimRepository _repo;
    private readonly IPermissionCacheInvalidator _cache;
    private readonly IUnitOfWork _uow;

    public AdminAddUserClaimCommandHandler(
        IUserOperationClaimRepository repo,
        IPermissionCacheInvalidator cache,
        IUnitOfWork uow)
    {
        _repo = repo;
        _cache = cache;
        _uow = uow;
    }

    public async Task Handle(AdminAddUserClaimCommand request, CancellationToken ct)
    {
        // 1) Idempotent kontrol
        var exists = await _repo.ExistsAsync(request.UserId, request.OperationClaimId, ct);
        if (exists) return;

        // 2) İlişkiyi ekle
        var entity = new UserOperationClaim(request.UserId, request.OperationClaimId);
        await _repo.AddAsync(entity, ct);

        // 3) Tek commit noktası
        await _uow.SaveChangesAsync(ct);

        // 4) Kullanıcının efektif permission seti değişti -> cache düş
        _cache.InvalidateUser(request.UserId);
    }
}