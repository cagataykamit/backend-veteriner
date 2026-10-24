using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.OwnerPortal;

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

                // Composite (any-of) policy: KVKK şablonları okuma (açık rıza ADR-021, aydınlatma ADR-024). Formu müşteri kartından
                // yazdıran (Clients.Read) ve metni Ayarlar'da düzenleyen kullanıcı aynı metni okur.
                options.AddPolicy(AuthorizationPolicyNames.ClinicsKvkkFormRead, p =>
                    p.Requirements.Add(new PermissionAnyOfRequirement(new[]
                    {
                        PermissionCatalog.Clinics.Read,
                        PermissionCatalog.Clinics.Update,
                        PermissionCatalog.Clients.Read,
                    })));

                // Composite (any-of) policy: yatış bölüm/oda tanımları okuma (HOSP-002). Yatış formunda oda seçen kullanıcı
                // ile Ayarlar'da tanımları düzenleyen kullanıcı aynı listeyi okur; yazma Clinics.Update ile korunur.
                options.AddPolicy(AuthorizationPolicyNames.ClinicsHospitalizationWardsRead, p =>
                    p.Requirements.Add(new PermissionAnyOfRequirement(new[]
                    {
                        PermissionCatalog.Hospitalizations.Read,
                        PermissionCatalog.Clinics.Read,
                        PermissionCatalog.Clinics.Update,
                    })));

                // Hasta sahibi portalı (ADR-031 K6): yalnız portal şeması ve portal hesabı. Personel token'ı bu şemada
                // hedef kitle uyuşmazlığıyla doğrulanmaz; personel politikaları da portal şemasını kullanmaz.
                options.AddPolicy(AuthorizationPolicyNames.PlatformAdmin, p => p
                    .RequireAuthenticatedUser()
                    .RequireAssertion(ctx => ctx.User.FindAll(VeterinerClaims.PlatformAdmin)
                        .Any(c => string.Equals(c.Value, bool.TrueString, StringComparison.OrdinalIgnoreCase))));

                options.AddPolicy(OwnerPortalAuth.Policy, p => p
                    .AddAuthenticationSchemes(OwnerPortalAuth.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(OwnerPortalAuth.PrincipalTypeClaim, OwnerPortalAuth.OwnerPrincipalType));
            });

            return services;
        }
    }
}