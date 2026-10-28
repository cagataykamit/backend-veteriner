using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.OwnerPortal.Sessions;

/// <summary>
/// Tek kullanımlık e-posta kodunun üretimi ve doğrulaması (giriş, parola sıfırlama ve hesap silme). Düz kod yalnız e-postada bulunur;
/// özet kod satırının kimliğiyle tuzlanır. Süre/deneme/bekleme kuralları <see cref="OwnerPortalCode"/>'dadır.
/// </summary>
public sealed class OwnerPortalCodeService
{
    private readonly IRepository<OwnerPortalCode> _codes;
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly ITokenHashService _hash;
    private readonly IEmailSender _email;

    public OwnerPortalCodeService(
        IRepository<OwnerPortalCode> codes,
        IOwnerPortalSecretGenerator secrets,
        ITokenHashService hash,
        IEmailSender email)
    {
        _codes = codes;
        _secrets = secrets;
        _hash = hash;
        _email = email;
    }

    /// <summary>
    /// Yeni kod üretip e-postayla gönderir; aynı amaçtaki açık kodlar geçersiz olur. Son kod bekleme süresindeyse
    /// hiçbir şey yapmaz (yanıt aynı kalır). Kod ve e-posta (outbox) tek kayıtta yazılır.
    /// </summary>
    public async Task IssueAndSendAsync(OwnerAccount account, OwnerPortalCodePurpose purpose, DateTime utcNow, CancellationToken ct)
    {
        var email = account.Email ?? throw new InvalidOperationException("Silinmiş hesaba kod gönderilmez.");
        var open = await _codes.ListAsync(new OpenOwnerPortalCodesSpec(account.Id, purpose, utcNow), ct);
        if (open.Any(c => c.IsInResendCooldown(utcNow)))
            return;

        foreach (var previous in open)
            previous.Invalidate(utcNow);

        var id = Guid.NewGuid();
        var rawCode = _secrets.CreateNumericCode(OwnerPortalCode.Length);
        var code = OwnerPortalCode.Issue(id, account.Id, purpose, ComputeHash(id, rawCode), utcNow);

        var message = OwnerPortalEmails.Code(purpose, rawCode);
        await _email.SendAsync(email, message.Subject, message.Body, ct);
        await _codes.AddAsync(code, ct);
        await _codes.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Amaç için son kodu doğrular ve tüketir. Yanlış deneme kodda sayılır; hesap sayacını çağıran günceller.
    /// Değişiklikler izlenir, kaydı çağıran yapar.
    /// </summary>
    public async Task<bool> TryConsumeAsync(
        OwnerAccount account,
        OwnerPortalCodePurpose purpose,
        string? submittedCode,
        DateTime utcNow,
        CancellationToken ct)
    {
        var code = await _codes.FirstOrDefaultAsync(new LatestOwnerPortalCodeSpec(account.Id, purpose), ct);
        if (code is null)
            return false;

        var normalized = submittedCode?.Trim() ?? string.Empty;
        var matches = normalized.Length == OwnerPortalCode.Length
                      && FixedTimeEquals(ComputeHash(code.Id, normalized), code.CodeHash);
        return code.TryConsume(matches, utcNow);
    }

    private string ComputeHash(Guid codeId, string rawCode) => _hash.ComputeSha256($"{codeId:N}:{rawCode}");

    private static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
