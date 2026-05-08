using Microsoft.AspNetCore.Authorization;

namespace Backend.Veteriner.Api.Auth;

/// <summary>
/// Tek bir endpoint için "verilen permission kodlarından en az biri" mantığını taşıyan composite requirement.
/// Mevcut tek-permission policy modeline ek olarak çalışır (PermissionRequirement davranışı bozulmaz).
/// </summary>
/// <remarks>
/// Kullanım: <c>AuthorizationOptions</c> üzerinde adlandırılmış policy olarak kaydedilir
/// (bkz. <see cref="AuthorizationServiceCollectionExtensions"/>) ve controller'da
/// <c>[Authorize(Policy = "...")]</c> ile uygulanır. Politika adı bir <see cref="Application.Auth.PermissionCatalog"/>
/// koduyla çakışmamalıdır; çünkü <see cref="PermissionPolicyProvider"/> kayıtlı olmayan adları
/// dinamik olarak tek-permission gereksinimine çevirir.
/// </remarks>
public sealed record PermissionAnyOfRequirement(IReadOnlyCollection<string> Codes) : IAuthorizationRequirement;
