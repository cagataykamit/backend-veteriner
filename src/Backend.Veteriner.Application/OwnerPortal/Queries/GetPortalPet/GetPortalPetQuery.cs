using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPet;

/// <summary>Bağın müşterisine ait tek hayvan; değilse <c>Portal.Pets.NotFound</c> (docs/PORTAL_API_CONTRACT.md §6.2).</summary>
public sealed record GetPortalPetQuery(Guid OwnerAccountId, Guid LinkId, Guid PetId) : IRequest<Result<PortalPetDetailDto>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.Pet";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId};PetId={PetId}";
}

public sealed class GetPortalPetQueryHandler : IRequestHandler<GetPortalPetQuery, Result<PortalPetDetailDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Pet> _pets;

    public GetPortalPetQueryHandler(OwnerPortalAccess access, IReadRepository<Pet> pets)
    {
        _access = access;
        _pets = pets;
    }

    public async Task<Result<PortalPetDetailDto>> Handle(GetPortalPetQuery request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result<PortalPetDetailDto>.Failure(scope.Error);

        var pet = await _pets.FirstOrDefaultAsync(
            new PortalPetDetailSpec(scope.Value!.TenantId, scope.Value.ClientId, request.PetId), ct);
        return pet is null
            ? Result<PortalPetDetailDto>.Failure(OwnerPortalErrors.PetNotFound)
            : Result<PortalPetDetailDto>.Success(pet);
    }
}
