using System.Security.Cryptography;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Mediator;

namespace Backend.Veteriner.Application.EmailVerification.Commands.Request;

public sealed class RequestEmailVerificationHandler : IRequestHandler<RequestEmailVerificationCommand, Unit>
{
    private readonly IUserReadRepository _users;
    private readonly IVerificationTokenRepository _repo;
    private readonly ITokenHashService _hash;
    private readonly IEmailSender _email;          // ?? Outbox �zerinden giden sender
    private readonly IAppUrlProvider _url;
    private readonly TimeProvider _timeProvider;

    // Ayn� kullan�c�ya 2 dakika i�inde yeniden do�rulama maili g�ndermeyelim
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

    public RequestEmailVerificationHandler(
        IUserReadRepository users,
        IVerificationTokenRepository repo,
        ITokenHashService hash,
        IEmailSender email,
        IAppUrlProvider url,
        TimeProvider timeProvider)
    {
        _users = users;
        _repo = repo;
        _hash = hash;
        _email = email;
        _url = url;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Unit> Handle(RequestEmailVerificationCommand request, CancellationToken ct)
    {
        // 1) Email normalize (case-insensitive search)
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        // 2) Kullan�c�y� bul
        var user = await _users.FirstOrDefaultAsync(new UserByEmailSpec(email), ct);
        if (user is null)
        {
            // Bilgi s�zd�rma yok
            return Unit.Value;
        }

        // 3) Zaten do�rulanm��sa tekrar mail g�ndermeye gerek yok
        if (user.EmailConfirmed)
            return Unit.Value;

        // 4) Kullan�c�n�n h�l� aktif (kullan�lmam�� + s�resi dolmam��) email verify token�� var m�?
        var existing = await _repo.GetActiveByUserAsync(
            user.Id,
            VerificationPurpose.EmailVerify,
            ct);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (existing is not null)
        {
            // Son 2 dakika i�inde olu�turulmu�sa yeni mail yollama (cooldown)
            if (existing.CreatedAtUtc > utcNow - ResendCooldown)
            {
                return Unit.Value;
            }

            // 2 dakika ge�mi�se, eski token�� kullan�lm�� sayal�m
            existing.MarkUsed(utcNow);
        }

        // 5) Yeni token �ret (Base64Url)
        var raw = GenerateSecureTokenBase64Url(32);
        var tokenHash = _hash.ComputeSha256(raw);

        var vt = new VerificationToken(
            user.Id,
            tokenHash,
            VerificationPurpose.EmailVerify,
            utcNow.AddHours(24),
            utcNow); // 24 saat ge�erli

        await _repo.AddAsync(vt, ct);

        // 6) Do�rulama linkini olu�tur
        var link = _url.BuildAbsolute("/api/email/confirm", $"token={Uri.EscapeDataString(raw)}");
        var subject = "E-posta doğrulama";
        var body =
            $"Merhaba,\n\n" +
            $"E-posta adresinizi doğrulamak için aşağıdaki bağlantıya tıklayın:\n{link}\n\n" +
            $"Bu bağlantı 24 saat geçerlidir.";

        // 7) E-postay� Outbox�a enqueue et (TransactionalEmailSender)
        await _email.SendAsync(user.Email, subject, body, ct);

        // 8) Tek SaveChanges:
        //    - Yeni VerificationToken
        //    - existing.MarkUsed() (varsa)
        //    - OutboxMessages (interceptor buffer�� burada bo�alt�r)
        await _repo.SaveChangesAsync(ct);

        return Unit.Value;
    }

    // Base64Url token �retimi
    private static string GenerateSecureTokenBase64Url(int bytesLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(bytesLength);
        var b64 = Convert.ToBase64String(bytes);
        return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
