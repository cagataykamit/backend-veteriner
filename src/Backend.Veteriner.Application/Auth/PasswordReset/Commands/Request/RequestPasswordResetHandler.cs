using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Auth.PasswordReset.Commands.Request;

public sealed class RequestPasswordResetHandler : IRequestHandler<RequestPasswordResetCommand, Unit>
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private readonly IUserReadRepository _users;
    private readonly IVerificationTokenRepository _repo;
    private readonly ITokenHashService _hash;
    private readonly IEmailSender _email;
    private readonly AppOptions _appOptions;
    private readonly ILogger<RequestPasswordResetHandler> _logger;

    public RequestPasswordResetHandler(
        IUserReadRepository users,
        IVerificationTokenRepository repo,
        ITokenHashService hash,
        IEmailSender email,
        IOptions<AppOptions> appOptions,
        ILogger<RequestPasswordResetHandler> logger)
    {
        _users = users;
        _repo = repo;
        _hash = hash;
        _email = email;
        _appOptions = appOptions.Value;
        _logger = logger;
    }

    public async Task<Unit> Handle(RequestPasswordResetCommand request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        _logger.LogInformation("PasswordReset: START for {Email}", email);

        // 1) Kullan�c�y� bul
        var user = await _users.FirstOrDefaultAsync(new UserByEmailSpec(email), ct);
        if (user is null)
        {
            _logger.LogWarning("PasswordReset: user NOT FOUND for {Email}", email);
            return Unit.Value; // bilgi s�zd�rma yok
        }

        _logger.LogInformation("PasswordReset: user found {UserId}", user.Id);

        // 2) Aktif token kontrol� (kullan�lmam�� ve s�resi dolmam��)
        var active = await _repo.GetActiveByUserAsync(user.Id, VerificationPurpose.PasswordReset, ct);
        if (active is not null)
        {
            var age = DateTime.UtcNow - active.CreatedAtUtc;
            if (age < Cooldown)
            {
                _logger.LogInformation("PasswordReset: active token exists within cooldown ({AgeSeconds}s). Skipping email.", (int)age.TotalSeconds);
                return Unit.Value; // cooldown penceresi i�inde -> yeni mail yok
            }

            // Cooldown a��ld�ysa mevcut aktifi ge�ersiz k�l (kullan�lm�� i�aretle)
            // Domain entity'nizde MarkUsed() varsa onu �a��r�n:
            active.MarkUsed();
            // Yoksa:
            // active.UsedAtUtc = DateTime.UtcNow;
        }

        // 3) Yeni token olu�tur (1 saat ge�erli)
        var raw = GenerateSecureTokenBase64Url(32); // 256-bit
        var tokenHash = _hash.ComputeSha256(raw);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var shortHash = tokenHash.Length > 12 ? tokenHash[..12] + "…" : tokenHash;
            _logger.LogDebug("PasswordReset: token generated hash='{Hash}'", shortHash);
        }

        var vt = new VerificationToken(
            user.Id,
            tokenHash,
            VerificationPurpose.PasswordReset,
            DateTime.UtcNow.AddHours(1));

        await _repo.AddAsync(vt, ct);

        // 4) Link ve e-posta
        var frontendBaseUrl = (_appOptions.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
        var link = $"{frontendBaseUrl}/auth/reset-password?token={Uri.EscapeDataString(raw)}";
        var subject = "Şifre Sıfırlama";
        var bodyText = new StringBuilder()
            .AppendLine("Merhaba,")
            .AppendLine()
            .AppendLine("Şifrenizi sıfırlamak için bağlantı:")
            .AppendLine(link)
            .AppendLine()
            .AppendLine("Bu bağlantı 1 saat boyunca geçerlidir.")
            .ToString();

        await _email.SendAsync(user.Email, subject, bodyText, ct);

        // 5) Tek commit � hem active'in MarkUsed() de�i�ikli�i hem yeni token hem de Outbox persist olur
        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation("PasswordReset: DONE for {Email}", email);
        return Unit.Value;
    }

    // G�venli Base64URL token �retimi (RFC 4648 uyumlu)
    private static string GenerateSecureTokenBase64Url(int bytesLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(bytesLength);
        var b64 = Convert.ToBase64String(bytes);
        return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
