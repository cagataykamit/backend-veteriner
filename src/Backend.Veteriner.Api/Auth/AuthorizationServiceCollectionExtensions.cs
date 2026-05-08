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
            services.AddSingleton<IAuthorizationHandler, PermissionAnyOfAuthorizationHandler>();
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

            services.AddAuthorization(options =>
            {
                foreach (var code in PermissionCatalog.AllCodes)
                {
                    options.AddPolicy(code, p =>
                        p.Requirements.Add(new PermissionRequirement(code)));
                }

                // Composite (any-of) policy: randevu planlama amaçlı klinik çalışma saatleri / randevu
                // varsayılanları okuma. Sekreter ve Veteriner gibi Clinics.Read'e sahip olmayan ama
                // randevu oluşturabilen rollerin frontend'de gerçek çalışma saatlerini görebilmesi için.
                // Update endpointleri etkilenmez; PUT/POST işlemleri Clinics.Update ile korunmaya devam eder.
                options.AddPolicy(AuthorizationPolicyNames.ClinicsSchedulingRead, p =>
                    p.Requirements.Add(new PermissionAnyOfRequirement(new[]
                    {
                        PermissionCatalog.Clinics.Read,
                        PermissionCatalog.Clinics.Update,
                        PermissionCatalog.Appointments.Create,
                        PermissionCatalog.Appointments.Reschedule,
                    })));
            });

            return services;
        }
    }
}