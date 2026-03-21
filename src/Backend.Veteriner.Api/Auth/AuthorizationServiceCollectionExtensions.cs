using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Backend.Veteriner.Application.Auth;

namespace Backend.Veteriner.Api.Auth
{
    public static class AuthorizationServiceCollectionExtensions
    {
        public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
        {
            services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

            services.AddAuthorization(options =>
            {
                foreach (var code in PermissionCatalog.AllCodes)
                {
                    options.AddPolicy(code, p =>
                        p.Requirements.Add(new PermissionRequirement(code)));
                }
            });

            return services;
        }
    }
}