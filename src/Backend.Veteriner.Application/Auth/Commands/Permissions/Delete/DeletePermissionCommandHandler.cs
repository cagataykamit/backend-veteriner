using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Auth.Commands.Permissions.Delete;

    /// <summary>
    /// Permission siler.
    /// - Ýlgili role-permission iliþkilerini temizler.
    /// - Commit sonrasý cache invalidation yapar.
    /// - Opsiyonel refresh revoke uygular.
    /// </summary>
    public sealed class DeletePermissionCommandHandler : IRequestHandler<DeletePermissionCommand, Result>
    {
        private readonly IPermissionRepository _repo;
        private readonly IOperationClaimPermissionRepository _ocpRepo;
        private readonly IPermissionCacheInvalidator _cache;
        private readonly IRefreshTokenRepository _refreshRepo;
        private readonly IUnitOfWork _uow;
        private readonly PermissionChangeOptions _opt;

        public DeletePermissionCommandHandler(
            IPermissionRepository repo,
            IOperationClaimPermissionRepository ocpRepo,
            IPermissionCacheInvalidator cache,
            IRefreshTokenRepository refreshRepo,
            IUnitOfWork uow,
            IOptions<PermissionChangeOptions> opt)
        {
            _repo = repo;
            _ocpRepo = ocpRepo;
            _cache = cache;
            _refreshRepo = refreshRepo;
            _uow = uow;
            _opt = opt.Value;
        }

        public async Task<Result> Handle(DeletePermissionCommand cmd, CancellationToken ct)
        {
            var entity = await _repo.GetByIdAsync(cmd.Id, ct)
                         ?? null;

            if (entity is null)
            {
                return Result.Failure("Permissions.NotFound", "Permission not found.");
            }

            var userIds = await _ocpRepo.GetUserIdsByPermissionIdAsync(cmd.Id, ct);

            await _ocpRepo.RemoveAllByPermissionIdAsync(cmd.Id, ct);
            await _repo.DeleteAsync(entity, ct);

            await _uow.SaveChangesAsync(ct);

            _cache.InvalidateUsers(userIds);

            if (_opt.RevokeSessionsOnPermissionChange)
            {
                foreach (var userId in userIds)
                    await _refreshRepo.RevokeAllByUserAsync(userId, ct);
            }

            return Result.Success();
        }
    }