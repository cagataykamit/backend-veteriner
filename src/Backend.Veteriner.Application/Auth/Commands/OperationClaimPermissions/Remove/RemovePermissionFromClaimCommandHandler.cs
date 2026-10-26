using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Remove
{
    /// <summary>
    /// Bir OperationClaim (rol) �zerinden bir Permission kald�r�r.
    ///
    /// Kurumsal davran��:
    /// 1) Rol�permission ili�kisini siler.
    /// 2) Bu role sahip kullan�c�lar�n permission cache'ini d���r�r.
    /// 3) Konfig�rasyona ba�l� olarak ilgili kullan�c�lar�n t�m refresh oturumlar�n� revoke eder.
    ///
    /// B�ylece:
    /// - Cache tutars�zl��� olu�maz.
    /// - G�venlik politikas� gerektiriyorsa an�nda logout-all uygulanabilir.
    /// </summary>
    public sealed class RemovePermissionFromClaimCommandHandler
        : IRequestHandler<RemovePermissionFromClaimCommand>
    {
        private readonly IOperationClaimPermissionRepository _repo;
        private readonly IPermissionCacheInvalidator _cacheInvalidator;
        private readonly IRefreshTokenRepository _refreshRepo;
        private readonly PermissionChangeOptions _opt;

        /// <summary>
        /// Constructor injection:
        /// - IOperationClaimPermissionRepository: rol�permission ili�ki y�netimi
        /// - IPermissionCacheInvalidator: permission cache d���rme i�lemi
        /// - IRefreshTokenRepository: oturum revoke i�lemleri
        /// - PermissionChangeOptions: oturum politikas�n� belirleyen konfig�rasyon
        /// </summary>
        public RemovePermissionFromClaimCommandHandler(
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

        /// <summary>
        /// �� ak���:
        /// - Permission ili�kisinin kald�r�lmas�
        /// - �lgili kullan�c�lar�n belirlenmesi
        /// - Cache invalidation
        /// - Opsiyonel: t�m refresh token'lar�n revoke edilmesi
        /// </summary>
        public async ValueTask<Unit> Handle(RemovePermissionFromClaimCommand cmd, CancellationToken ct)
        {
            // 1) Rol�permission ili�kisini kald�r
            await _repo.RemoveAsync(cmd.OperationClaimId, cmd.PermissionId, ct);

            // 2) Bu role sahip kullan�c�lar� bul
            var userIds = await _repo
                .GetUserIdsByOperationClaimIdAsync(cmd.OperationClaimId, ct);

            // 3) Permission cache'i d���r
            //    B�ylece kullan�c� bir sonraki permission okumas�nda g�ncel veri al�r.
            _cacheInvalidator.InvalidateUsers(userIds);

            // 4) E�er konfig�rasyonda aktifse:
            //    G�venlik sertle�tirmesi amac�yla ilgili kullan�c�lar�n
            //    t�m aktif refresh token'lar�n� revoke et.
            if (_opt.RevokeSessionsOnPermissionChange)
            {
                foreach (var userId in userIds)
                {
                    await _refreshRepo.RevokeAllByUserAsync(userId, ct);
                }

                await _refreshRepo.SaveChangesAsync(ct);
            }

            return Unit.Value;
        }
    }
}
