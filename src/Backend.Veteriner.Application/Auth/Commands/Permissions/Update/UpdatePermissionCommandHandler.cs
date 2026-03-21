using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Auth.Commands.Permissions.Update;

    /// <summary>
    /// Permission günceller.
    /// - Permission deðiþince etkilenen kullanýcýlarýn permission cache'i düþürülür.
    /// - Opsiyonel: etkilenen kullanýcýlarýn refresh oturumlarý revoke edilir.
    /// - Commit noktasý: UnitOfWork
    /// </summary>
    public sealed class UpdatePermissionCommandHandler : IRequestHandler<UpdatePermissionCommand, Result>
    {
        private readonly IPermissionRepository _repo;
        private readonly IOperationClaimPermissionRepository _ocpRepo;
        private readonly IPermissionCacheInvalidator _cache;
        private readonly IRefreshTokenRepository _refreshRepo;
        private readonly IUnitOfWork _uow;
        private readonly PermissionChangeOptions _opt;

        public UpdatePermissionCommandHandler(
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

        public async Task<Result> Handle(UpdatePermissionCommand cmd, CancellationToken ct)
        {
            var entity = await _repo.GetByIdAsync(cmd.Id, ct)
                         ?? null;

            if (entity is null)
            {
                return Result.Failure("Permissions.NotFound", "Permission not found.");
            }

            if (!string.Equals(entity.Code, cmd.Code, StringComparison.OrdinalIgnoreCase)
                && await _repo.ExistsByCodeAsync(cmd.Code, ct))
            {
                return Result.Failure("Permissions.DuplicateCode", "Code already in use.");
            }

            var userIds = await _ocpRepo.GetUserIdsByPermissionIdAsync(cmd.Id, ct);

            entity.Rename(cmd.Code);
            entity.UpdateDetails(cmd.Description, group: null);

            await _repo.UpdateAsync(entity, ct);
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