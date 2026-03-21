using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Remove;

public sealed class RemoveUserOperationClaimCommandHandler
    : IRequestHandler<RemoveUserOperationClaimCommand>
{
    private readonly IUserOperationClaimRepository _repo;

    public RemoveUserOperationClaimCommandHandler(IUserOperationClaimRepository repo)
        => _repo = repo;

    public Task Handle(RemoveUserOperationClaimCommand request, CancellationToken ct)
        => _repo.RemoveAsync(request.UserId, request.OperationClaimId, ct);
}
