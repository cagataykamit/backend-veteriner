using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Remove;

public sealed class RemoveUserOperationClaimCommandHandler
    : IRequestHandler<RemoveUserOperationClaimCommand>
{
    private readonly IUserOperationClaimRepository _repo;
    private readonly IPermissionCacheInvalidator _cache;
    private readonly IUnitOfWork _uow;

    public RemoveUserOperationClaimCommandHandler(
        IUserOperationClaimRepository repo,
        IPermissionCacheInvalidator cache,
        IUnitOfWork uow)
    {
        _repo = repo;
        _cache = cache;
        _uow = uow;
    }

    public async ValueTask<Unit> Handle(RemoveUserOperationClaimCommand request, CancellationToken ct)
    {
        await _repo.RemoveAsync(request.UserId, request.OperationClaimId, ct);
        await _uow.SaveChangesAsync(ct);

        _cache.InvalidateUser(request.UserId);
        return Unit.Value;
    }
}
