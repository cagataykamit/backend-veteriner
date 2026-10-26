using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Remove;

public sealed class RemoveUserOperationClaimCommandHandler
    : IRequestHandler<RemoveUserOperationClaimCommand>
{
    private readonly IUserOperationClaimRepository _repo;

    public RemoveUserOperationClaimCommandHandler(IUserOperationClaimRepository repo)
        => _repo = repo;

    public async ValueTask<Unit> Handle(RemoveUserOperationClaimCommand request, CancellationToken ct)
    {
        await _repo.RemoveAsync(request.UserId, request.OperationClaimId, ct);
        return Unit.Value;
    }
}
