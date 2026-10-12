using System;
using System.Threading;
using System.Threading.Tasks;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Auth.PasswordReset.Commands.Confirm;

public sealed class ConfirmPasswordResetHandler : IRequestHandler<ConfirmPasswordResetCommand, Unit>
{
    private readonly IVerificationTokenRepository _verRepo;
    private readonly ITokenHashService _hash;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly ILogger<ConfirmPasswordResetHandler> _logger;
    private readonly IClientContext _client;

    public ConfirmPasswordResetHandler(
        IVerificationTokenRepository verRepo,
        ITokenHashService hash,
        IUserRepository users,
        IPasswordHasher hasher,
        IRefreshTokenRepository refreshRepo,
        IClientContext client,
        ILogger<ConfirmPasswordResetHandler> logger)
    {
        _verRepo = verRepo;
        _hash = hash;
        _users = users;
        _hasher = hasher;
        _refreshRepo = refreshRepo;
        _client = client;
        _logger = logger;
    }

    public async Task<Unit> Handle(ConfirmPasswordResetCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new UnauthorizedAccessException("Geçersiz token.");

        // Parola politikas� FluentValidation'da kontrol ediliyor.

        // ?? Token normalize (URL-encoded gelebilir; '+' bo�lu�a d�n��m�� olabilir)
        var raw = request.Token.Trim();
        try { raw = Uri.UnescapeDataString(raw); } catch { /* ignore */ }
        raw = raw.Replace(' ', '+');

        var tokenHash = _hash.ComputeSha256(raw);

        // Debug seviyesinde k�smi log (prod�da hassas veri i�ermiyor)
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var hashShort = tokenHash.Length > 12 ? tokenHash[..12] + "â€¦" : tokenHash;
            _logger.LogDebug("ConfirmPR: hash='{HashShort}'", hashShort);
        }

        var vt = await _verRepo.GetActiveByHashAsync(tokenHash, VerificationPurpose.PasswordReset, ct)
                 ?? throw new UnauthorizedAccessException("Token bulunamadı veya süresi dolmuş.");

        var user = vt.User ?? await _users.GetByIdAsync(vt.UserId, ct)
                   ?? throw new UnauthorizedAccessException("Kullanıcı bulunamadı.");

        // ?? Yeni parolay� bcrypt ile g�ncelle
        var newHash = _hasher.Hash(request.NewPassword);
        user.UpdatePasswordHash(newHash);

        // Token�� kullan�lm�� i�aretle
        vt.MarkUsed();

        // ?? G�venlik: t�m aktif refresh token�lar� iptal et
        var reason = $"password-reset ip={_client.IpAddress ?? "n/a"} ua=\"{_client.UserAgent ?? "n/a"}\"";
        await _refreshRepo.RevokeAllByUserAsync(user.Id, ct);
        // overload varsa � await _refreshRepo.RevokeAllByUserAsync(user.Id, reason, ct);

        // Tek SaveChanges ile commit (ayn� DbContext payla��ld���ndan hepsi kaydolur)
        await _verRepo.SaveChangesAsync(ct);

        _logger.LogInformation("ConfirmPR: password reset completed for userId={UserId}", user.Id);
        return Unit.Value;
    }
}
