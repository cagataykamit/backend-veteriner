using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Assign;

public sealed class AssignUserOperationClaimCommandHandler
    : IRequestHandler<AssignUserOperationClaimCommand, Result<Guid>>
{
    private readonly IUserReadRepository _users;
    private readonly IOperationClaimReadRepository _operationClaims;
    private readonly IUserOperationClaimRepository _repo;
    private readonly IUnitOfWork _uow;

    public AssignUserOperationClaimCommandHandler(
        IUserReadRepository users,
        IOperationClaimReadRepository operationClaims,
        IUserOperationClaimRepository repo,
        IUnitOfWork uow)
    {
        _users = users;
        _operationClaims = operationClaims;
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result<Guid>> Handle(AssignUserOperationClaimCommand request, CancellationToken ct)
    {
        var user = await _users.FirstOrDefaultAsync(new UserByIdWithRolesSpec(request.UserId), ct);
        if (user is null)
            return Result<Guid>.Failure("UserOperationClaims.UserNotFound", "User not found.");

        var claimExists = await _operationClaims.ExistsAsync(request.OperationClaimId, ct);
        if (!claimExists)
            return Result<Guid>.Failure("UserOperationClaims.OperationClaimNotFound", "Operation claim not found.");

        var linkExists = await _repo.ExistsAsync(request.UserId, request.OperationClaimId, ct);
        if (linkExists)
            return Result<Guid>.Failure("UserOperationClaims.Duplicate", "User already has this operation claim.");

        var entity = new UserOperationClaim(request.UserId, request.OperationClaimId);
        await _repo.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<Guid>.Success(entity.Id);
    }
}
