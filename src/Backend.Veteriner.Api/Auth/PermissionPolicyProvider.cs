using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;


namespace Backend.Veteriner.Api.Auth;

/// <summary>
/// �stenen policy ad� i�in dinamik olarak PermissionRequirement �reten provider.
/// Mevcut policy varsa onu d�ner; yoksa yeni bir policy in�a eder.
/// </summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider, IAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // �nce base i�inde kay�tl� bir policy var m�?
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null) return existing;

        // Yoksa dinamik �ret: policyName � PermissionRequirement(policyName)
        var builder = new AuthorizationPolicyBuilder();
        builder.AddRequirements(new PermissionRequirement(policyName));
        return builder.Build();
    }
}
