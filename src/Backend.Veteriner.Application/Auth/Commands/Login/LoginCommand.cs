using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Login;

/// <summary>
/// Kimlik doğrulama. Üyelik doğrulaması sunucuda yapılır.
/// </summary>
/// <param name="Email">E-posta.</param>
/// <param name="Password">Şifre.</param>
/// <param name="TenantId">
/// İsteğe bağlı uyumluluk alanı: kullanıcı tek kiracılıdır (<c>UserTenants</c> başına en fazla bir satır).
/// Boş bırakılır veya çözümlenen kiracı ile aynı GUID gönderilir; farklı GUID ise <c>Auth.TenantMismatch</c>.
/// Veride birden fazla kiracı üyeliği kalırsa <c>Auth.UserMultipleTenantsForbidden</c>.
/// </param>
public sealed record LoginCommand(string Email, string Password, Guid? TenantId = null)
    : IRequest<Result<LoginResultDto>>;
