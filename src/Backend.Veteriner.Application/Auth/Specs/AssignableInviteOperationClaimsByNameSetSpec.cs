using Ardalis.Specification;
using Backend.Veteriner.Domain.Auth;

namespace Backend.Veteriner.Application.Auth.Specs;

/// <summary>Whitelist isim kümesi (küçük harfe normalize edilmiş) ile eşleşen operation claim kayıtları.</summary>
public sealed class AssignableInviteOperationClaimsByNameSetSpec : Specification<OperationClaim>
{
    public AssignableInviteOperationClaimsByNameSetSpec(HashSet<string> normalizedLowerNames)
    {
        Query.Where(c => normalizedLowerNames.Contains(c.Name.ToLower()));
    }
}
