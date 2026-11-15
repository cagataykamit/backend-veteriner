using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Public.TrialSignup;

/// <summary>
/// Deneme kaydının e-posta doğrulaması (TRIAL-001): kod gönderme, kodu doğrulama ve kayıtta kullanma. Düz kod yalnız e-postada,
/// düz jeton yalnız yanıttadır; özetler satır kimliğiyle tuzlanır (portal kodlarıyla aynı yöntem). Süre, deneme, bekleme ve
/// sınır kuralları <see cref="SignupEmailVerification"/>'dadır.
/// </summary>
public sealed class SignupEmailVerificationService
{
    public const string CodePurpose = "code";
    public const string TokenPurpose = "token";

    private readonly IRepository<SignupEmailVerification> _verifications;
    private readonly IEmailSender _email;
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly ITokenHashService _hash;
    private readonly ILogger<SignupEmailVerificationService> _logger;

    public SignupEmailVerificationService(
        IRepository<SignupEmailVerification> verifications,
        IEmailSender email,
        IOwnerPortalSecretGenerator secrets,
        ITokenHashService hash,
        ILogger<SignupEmailVerificationService> logger)
    {
        _verifications = verifications;
        _email = email;
        _secrets = secrets;
        _hash = hash;
        _logger = logger;
    }

    /// <summary>
    /// Sınırlar uygunsa yeni kod üretip e-postayla (outbox) gönderir; adresin açık kodları ve jetonları geçersiz olur. Kod ve
    /// e-posta tek kayıtta yazılır.
    /// </summary>
    /// <param name="ipAddress">İsteğin IP adresi; yalnız özeti saklanır. Bilinmiyorsa IP sınırı uygulanmaz.</param>
    public async Task<Result<SignupEmailCodeSentDto>> RequestCodeAsync(
        string normalizedEmail, string? ipAddress, DateTime utcNow, CancellationToken ct)
    {
        var since = utcNow - SignupEmailVerification.DailyWindow;
        var recent = await _verifications.ListAsync(new SignupEmailCodesForEmailSinceSpec(normalizedEmail, since), ct);
        var latest = recent.MaxBy(x => x.CreatedAtUtc);
        if (latest is not null && latest.IsInResendCooldown(utcNow))
            return Result<SignupEmailCodeSentDto>.Failure(TrialSignupErrors.ResendTooSoon(latest.ResendAvailableIn(utcNow)));

        if (recent.Count >= SignupEmailVerification.MaxCodesPerEmailPerDay)
            return Result<SignupEmailCodeSentDto>.Failure(TrialSignupErrors.DailyLimitReached);

        var ipHash = string.IsNullOrWhiteSpace(ipAddress) ? null : _hash.ComputeSha256($"signup-email-ip:{ipAddress.Trim()}");
        if (ipHash is not null
            && await _verifications.CountAsync(new SignupEmailCodesForIpSinceSpec(ipHash, since), ct)
            >= SignupEmailVerification.MaxCodesPerIpPerDay)
        {
            return Result<SignupEmailCodeSentDto>.Failure(TrialSignupErrors.DailyLimitReached);
        }

        if (await _verifications.CountAsync(new SignupEmailCodesSinceSpec(since), ct) >= SignupEmailVerification.MaxCodesPerDay)
        {
            _logger.LogWarning("Deneme kaydı e-posta kodu günlük genel sınırı ({Limit}) doldu.", SignupEmailVerification.MaxCodesPerDay);
            return Result<SignupEmailCodeSentDto>.Failure(TrialSignupErrors.DailyLimitReached);
        }

        foreach (var previous in recent)
            previous.Invalidate(utcNow);

        var id = Guid.NewGuid();
        var rawCode = _secrets.CreateNumericCode(SignupEmailVerification.CodeLength);
        await _email.SendAsync(normalizedEmail, CodeSubject, CodeBody(rawCode), ct);
        await _verifications.AddAsync(
            SignupEmailVerification.Issue(id, normalizedEmail, ComputeSecretHash(_hash, id, CodePurpose, rawCode), ipHash, utcNow), ct);
        await _verifications.SaveChangesAsync(ct);

        return Result<SignupEmailCodeSentDto>.Success(new SignupEmailCodeSentDto(
            (int)SignupEmailVerification.CodeLifetime.TotalSeconds,
            (int)SignupEmailVerification.ResendCooldown.TotalSeconds));
    }

    /// <summary>Adresin son kodunu doğrular; yanlış deneme kalıcı sayılır. Başarıda kayıt jetonu döner.</summary>
    public async Task<Result<SignupEmailVerifiedDto>> VerifyCodeAsync(
        string normalizedEmail, string? submittedCode, DateTime utcNow, CancellationToken ct)
    {
        var latest = await _verifications.FirstOrDefaultAsync(new LatestSignupEmailVerificationSpec(normalizedEmail), ct);
        if (latest is null)
            return Result<SignupEmailVerifiedDto>.Failure(TrialSignupErrors.CodeInvalid);

        var code = submittedCode?.Trim() ?? string.Empty;
        var matches = code.Length == SignupEmailVerification.CodeLength
                      && HashEquals(ComputeSecretHash(_hash, latest.Id, CodePurpose, code), latest.CodeHash);

        var rawToken = _secrets.CreateToken();
        var verified = latest.TryVerify(matches, ComputeSecretHash(_hash, latest.Id, TokenPurpose, rawToken), utcNow);
        await _verifications.SaveChangesAsync(ct);

        return verified
            ? Result<SignupEmailVerifiedDto>.Success(new SignupEmailVerifiedDto(rawToken, latest.SignupTokenExpiresAtUtc!.Value))
            : Result<SignupEmailVerifiedDto>.Failure(TrialSignupErrors.CodeInvalid);
    }

    /// <summary>
    /// Kayıt için doğrulanmış satırı bulur: son satır doğrulanmış, jeton eşleşiyor ve süresi dolmamış olmalı. Satır izlenir;
    /// kullanımını (<see cref="SignupEmailVerification.MarkUsed"/>) kayıt aynı işlemde yazar.
    /// </summary>
    public async Task<Result<SignupEmailVerification>> FindForSignupAsync(
        string normalizedEmail, string? signupToken, DateTime utcNow, CancellationToken ct)
    {
        var latest = await _verifications.FirstOrDefaultAsync(new LatestSignupEmailVerificationSpec(normalizedEmail), ct);
        if (latest is null
            || string.IsNullOrWhiteSpace(signupToken)
            || !latest.CanBeUsedForSignup(utcNow)
            || !HashEquals(ComputeSecretHash(_hash, latest.Id, TokenPurpose, signupToken.Trim()), latest.SignupTokenHash!))
        {
            return Result<SignupEmailVerification>.Failure(TrialSignupErrors.VerificationRequired);
        }

        return Result<SignupEmailVerification>.Success(latest);
    }

    public const string CodeSubject = "Vetinity doğrulama kodunuz";

    public static string CodeBody(string rawCode)
        => $"""
           Merhaba,

           Vetinity deneme hesabınızı oluşturmak için doğrulama kodunuz: {rawCode}

           Kod {(int)SignupEmailVerification.CodeLifetime.TotalMinutes} dakika geçerlidir. Bu kodu kimseyle paylaşmayın.
           Kayıt isteğini siz yapmadıysanız bu e-postayı dikkate almayın.

           Vetinity
           """;

    /// <summary>Kod ve jeton özeti: satır kimliği ve amaçla tuzlanır (test kurulumu da bu yöntemi kullanır).</summary>
    public static string ComputeSecretHash(ITokenHashService hash, Guid verificationId, string purpose, string raw)
        => hash.ComputeSha256($"{verificationId:N}:{purpose}:{raw}");

    private static bool HashEquals(string computed, string stored)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(stored));
}
