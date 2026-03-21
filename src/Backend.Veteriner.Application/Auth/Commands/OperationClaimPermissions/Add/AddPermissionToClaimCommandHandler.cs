using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using MediatR;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Add
{
    /// <summary>
    /// Bir OperationClaim (rol) �zerine Permission ekler.
    /// 
    /// Kurumsal davran��:
    /// 1) Idempotent ekleme (varsa tekrar eklemez)
    /// 2) �lgili kullan�c�lar�n permission cache'ini d���r�r
    /// 3) Konfig�rasyona ba�l� olarak aktif oturumlar� revoke eder (logout-all)
    /// </summary>
    public sealed class AddPermissionToClaimCommandHandler : IRequestHandler<AddPermissionToClaimCommand>
    {
        private readonly IOperationClaimPermissionRepository _repo;
        private readonly IPermissionCacheInvalidator _cacheInvalidator;
        private readonly IRefreshTokenRepository _refreshRepo;
        private readonly PermissionChangeOptions _opt;

        public AddPermissionToClaimCommandHandler(
            IOperationClaimPermissionRepository repo,
            IPermissionCacheInvalidator cacheInvalidator,
            IRefreshTokenRepository refreshRepo,
            IOptions<PermissionChangeOptions> opt)
        {
            _repo = repo;
            _cacheInvalidator = cacheInvalidator;
            _refreshRepo = refreshRepo;
            _opt = opt.Value;
        }

        public async Task Handle(AddPermissionToClaimCommand cmd, CancellationToken ct)
        {
            // ------------------------------------------------------
            // 1) Idempotent kontrol
            // Ayn� role ayn� permission tekrar eklenmesin.
            // ------------------------------------------------------
            var exists = await _repo.ExistsAsync(cmd.OperationClaimId, cmd.PermissionId, ct);

            if (!exists)
                await _repo.AddAsync(cmd.OperationClaimId, cmd.PermissionId, ct);

            // ------------------------------------------------------
            // 2) Bu role sahip kullan�c�lar� bul
            // ��nk� permission setleri de�i�ti.
            // ------------------------------------------------------
            var userIds = await _repo.GetUserIdsByOperationClaimIdAsync(cmd.OperationClaimId, ct);

            // ------------------------------------------------------
            // 3) Permission cache invalidation
            // TTL beklemeden yeni permission seti okunabilsin.
            // (Refresh s�ras�nda yeni claim seti �retilecek.)
            // ------------------------------------------------------
            _cacheInvalidator.InvalidateUsers(userIds);

            // ------------------------------------------------------
            // 4) Opsiyonel g�venlik: aktif oturumlar� d���r
            // E�er konfig�rasyonda RevokeSessionsOnPermissionChange = true ise
            // ilgili kullan�c�lar�n t�m aktif refresh token'lar� revoke edilir.
            //
            // Bu "sert g�venlik" modudur.
            // Varsay�lan false olmas� �nerilir.
            // ------------------------------------------------------
            if (_opt.RevokeSessionsOnPermissionChange)
            {
                foreach (var userId in userIds)
                    await _refreshRepo.RevokeAllByUserAsync(userId, ct);

                // Projede UnitOfWork yok, repository SaveChanges �a��rm�yorsa
                // burada manuel persist edilir.
                await _refreshRepo.SaveChangesAsync(ct);
            }
        }
    }
}
