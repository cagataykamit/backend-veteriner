using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPets;

/// <summary>Bağın müşterisinin hayvanları; vefat eden <c>status</c> ile işaretli görünür (docs/PORTAL_API_CONTRACT.md §6.2).</summary>
public sealed record GetPortalPetsQuery(Guid OwnerAccountId, Guid LinkId) : IRequest<Result<IReadOnlyList<PortalPetDto>>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.Pets";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed class GetPortalPetsQueryHandler : IRequestHandler<GetPortalPetsQuery, Result<IReadOnlyList<PortalPetDto>>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Pet> _pets;

    public GetPortalPetsQueryHandler(OwnerPortalAccess access, IReadRepository<Pet> pets)
    {
        _access = access;
        _pets = pets;
    }

    public async ValueTask<Result<IReadOnlyList<PortalPetDto>>> Handle(GetPortalPetsQuery request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<PortalPetDto>>.Failure(scope.Error);

        var pets = await _pets.ListAsync(new PortalPetsByClientSpec(scope.Value!.TenantId, scope.Value.ClientId), ct);
        return Result<IReadOnlyList<PortalPetDto>>.Success(pets);
    }
}
