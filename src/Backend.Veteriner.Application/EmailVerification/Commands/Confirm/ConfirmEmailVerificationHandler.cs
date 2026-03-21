using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using MediatR;

namespace Backend.Veteriner.Application.EmailVerification.Commands.Confirm;

public sealed class ConfirmEmailVerificationHandler : IRequestHandler<ConfirmEmailVerificationCommand, Unit>
{
    private readonly IVerificationTokenRepository _repo;
    private readonly ITokenHashService _hash;
    private readonly IUserRepository _users;

    public ConfirmEmailVerificationHandler(
        IVerificationTokenRepository repo,
        ITokenHashService hash,
        IUserRepository users)
    {
        _repo = repo;
        _hash = hash;
        _users = users;
    }

    public async Task<Unit> Handle(ConfirmEmailVerificationCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new UnauthorizedAccessException("Ge�ersiz token.");

        // ?? Token normalize (URL �zerinden geldi�i i�in encode/decode fark� olabilir)
        var raw = request.Token.Trim();

        // E�er linkte URL-encode edilmi�se (%, vs) decode etmeye �al��
        try
        {
            raw = Uri.UnescapeDataString(raw);
        }
        catch
        {
            // decode edilemezse oldu�u gibi devam et
        }

        // G�venli olmas� i�in bo�luklar� '+' yap (baz� client'lar + yerine space koyabiliyor)
        raw = raw.Replace(' ', '+');

        // Hash hesapla
        var tokenHash = _hash.ComputeSha256(raw);

        // Aktif, s�resi dolmam��, kullan�lmam�� do�rulama token��n� bul
        var vt = await _repo.GetActiveByHashAsync(tokenHash, VerificationPurpose.EmailVerify, ct)
                 ?? throw new UnauthorizedAccessException("Token bulunamad� veya s�resi dolmu�.");

        // �lgili kullan�c�y� al
        var user = vt.User ?? await _users.GetByIdAsync(vt.UserId, ct)
                   ?? throw new UnauthorizedAccessException("Kullan�c� bulunamad�.");

        // Domain method: e-posta do�ruland�
        user.ConfirmEmail();

        // Token�� kullan�lm�� i�aretle
        vt.MarkUsed();

        // Ayn� DbContext �zerinden tek SaveChanges
        await _repo.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
