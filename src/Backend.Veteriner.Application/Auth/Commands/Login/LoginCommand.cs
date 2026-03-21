using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Login;

/// <summary>
/// Kimlik doğrulama. Üyelik doğrulaması sunucuda yapılır.
/// </summary>
/// <param name="Email">E-posta.</param>
/// <param name="Password">Şifre.</param>
/// <param name="TenantId">
/// Çok kiracılı kullanıcıda hangi kiracıya girileceği (zorunlu).
/// Tek kiracılı kullanıcıda atlanabilir; yanlış GUID gönderilirse <c>Auth.TenantMismatch</c>.
/// </param>
public sealed record LoginCommand(string Email, string Password, Guid? TenantId = null)
    : IRequest<Result<LoginResultDto>>;
