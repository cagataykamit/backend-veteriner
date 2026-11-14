using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Public.PhoneVerification;

/// <summary>
/// Deneme kaydının telefon doğrulaması (TRIAL-001): kod gönderme, kodu doğrulama ve kayıtta kullanma. Düz kod yalnız SMS'te,
/// düz jeton yalnız yanıttadır; özetler satır kimliğiyle tuzlanır. Süre, deneme, bekleme ve sınır kuralları
/// <see cref="SignupPhoneVerification"/>'dadır.
/// </summary>
public sealed class SignupPhoneVerificationService
{
    private readonly IRepository<SignupPhoneVerification> _verifications;
    private readonly ISmsSender _sms;
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly ITokenHashService _hash;
    private readonly ILogger<SignupPhoneVerificationService> _logger;

    public SignupPhoneVerificationService(
        IRepository<SignupPhoneVerification> verifications,
        ISmsSender sms,
        IOwnerPortalSecretGenerator secrets,
        ITokenHashService hash,
        ILogger<SignupPhoneVerificationService> logger)
    {
        _verifications = verifications;
        _sms = sms;
        _secrets = secrets;
        _hash = hash;
        _logger = logger;
    }

    /// <summary>
    /// Sınırlar uygunsa yeni kod üretip SMS ile gönderir; numaranın açık kodları ve jetonları geçersiz olur. Satır yalnız
    /// gönderim başarılıysa yazılır (başarısız gönderim sınırlardan düşmez).
    /// </summary>
    /// <param name="ipAddress">İsteğin IP adresi; yalnız özeti saklanır. Bilinmiyorsa IP sınırı uygulanmaz.</param>
    public async Task<Result<SignupPhoneCodeSentDto>> RequestCodeAsync(
        string normalizedPhone, string? ipAddress, DateTime utcNow, CancellationToken ct)
    {
        if (!_sms.IsConfigured)
        {
            _logger.LogError("Deneme kaydı telefon kodu istendi ama SMS sağlayıcısı yapılandırılmamış (Sms:Provider).");
            return Result<SignupPhoneCodeSentDto>.Failure(SignupPhoneErrors.SmsNotConfigured);
        }

        if (await _verifications.AnyAsync(new UsedSignupPhoneSpec(normalizedPhone), ct))
            return Result<SignupPhoneCodeSentDto>.Failure(SignupPhoneErrors.DuplicateTrial);

        var since = utcNow - SignupPhoneVerification.DailyWindow;
        var recent = await _verifications.ListAsync(new SignupPhoneCodesForPhoneSinceSpec(normalizedPhone, since), ct);
        var latest = recent.MaxBy(x => x.CreatedAtUtc);
        if (latest is not null && latest.IsInResendCooldown(utcNow))
            return Result<SignupPhoneCodeSentDto>.Failure(SignupPhoneErrors.ResendTooSoon(latest.ResendAvailableIn(utcNow)));

        if (recent.Count >= SignupPhoneVerification.MaxCodesPerPhonePerDay)
            return Result<SignupPhoneCodeSentDto>.Failure(SignupPhoneErrors.DailyLimitReached);

        var ipHash = string.IsNullOrWhiteSpace(ipAddress) ? null : _hash.ComputeSha256($"signup-phone-ip:{ipAddress.Trim()}");
        if (ipHash is not null
            && await _verifications.CountAsync(new SignupPhoneCodesForIpSinceSpec(ipHash, since), ct)
            >= SignupPhoneVerification.MaxCodesPerIpPerDay)
        {
            return Result<SignupPhoneCodeSentDto>.Failure(SignupPhoneErrors.DailyLimitReached);
        }

        if (await _verifications.CountAsync(new SignupPhoneCodesSinceSpec(since), ct) >= SignupPhoneVerification.MaxCodesPerDay)
        {
            _logger.LogWarning("Deneme kaydı telefon kodu günlük genel sınırı ({Limit}) doldu.", SignupPhoneVerification.MaxCodesPerDay);
            return Result<SignupPhoneCodeSentDto>.Failure(SignupPhoneErrors.DailyLimitReached);
        }

        var id = Guid.NewGuid();
        var rawCode = _secrets.CreateNumericCode(SignupPhoneVerification.CodeLength);
        var sent = await _sms.SendOneTimeCodeAsync(normalizedPhone, CodeMessage(rawCode), ct);
        if (!sent.Succeeded)
        {
            _logger.LogWarning(
                "Deneme kaydı telefon kodu gönderilemedi. Phone={Phone} Reason={Reason}",
                TurkishMobilePhone.Mask(normalizedPhone), sent.FailureReason);
            return Result<SignupPhoneCodeSentDto>.Failure(SignupPhoneErrors.SmsSendFailed);
        }

        foreach (var previous in recent)
            previous.Invalidate(utcNow);

        await _verifications.AddAsync(
            SignupPhoneVerification.Issue(id, normalizedPhone, ComputeHash(id, CodePurpose, rawCode), ipHash, utcNow), ct);
        await _verifications.SaveChangesAsync(ct);

        return Result<SignupPhoneCodeSentDto>.Success(new SignupPhoneCodeSentDto(
            (int)SignupPhoneVerification.CodeLifetime.TotalSeconds,
            (int)SignupPhoneVerification.ResendCooldown.TotalSeconds));
    }

    /// <summary>Numaranın son kodunu doğrular; yanlış deneme kalıcı sayılır. Başarıda kayıt jetonu döner.</summary>
    public async Task<Result<SignupPhoneVerifiedDto>> VerifyCodeAsync(
        string normalizedPhone, string? submittedCode, DateTime utcNow, CancellationToken ct)
    {
        var latest = await _verifications.FirstOrDefaultAsync(new LatestSignupPhoneVerificationSpec(normalizedPhone), ct);
        if (latest is null)
            return Result<SignupPhoneVerifiedDto>.Failure(SignupPhoneErrors.CodeInvalid);

        var code = submittedCode?.Trim() ?? string.Empty;
        var matches = code.Length == SignupPhoneVerification.CodeLength
                      && HashEquals(ComputeHash(latest.Id, CodePurpose, code), latest.CodeHash);

        var rawToken = _secrets.CreateToken();
        var verified = latest.TryVerify(matches, ComputeHash(latest.Id, TokenPurpose, rawToken), utcNow);
        await _verifications.SaveChangesAsync(ct);

        return verified
            ? Result<SignupPhoneVerifiedDto>.Success(new SignupPhoneVerifiedDto(rawToken, latest.SignupTokenExpiresAtUtc!.Value))
            : Result<SignupPhoneVerifiedDto>.Failure(SignupPhoneErrors.CodeInvalid);
    }

    /// <summary>
    /// Kayıt için doğrulanmış satırı bulur: numarayla daha önce deneme açılmamış, son satır doğrulanmış, jeton eşleşiyor ve süresi
    /// dolmamış olmalı. Satır izlenir; kullanımı <see cref="UseForSignup"/> kayıtla aynı işlemde yazar.
    /// </summary>
    public async Task<Result<SignupPhoneVerification>> FindForSignupAsync(
        string normalizedPhone, string? signupToken, DateTime utcNow, CancellationToken ct)
    {
        if (await _verifications.AnyAsync(new UsedSignupPhoneSpec(normalizedPhone), ct))
            return Result<SignupPhoneVerification>.Failure(SignupPhoneErrors.DuplicateTrial);

        var latest = await _verifications.FirstOrDefaultAsync(new LatestSignupPhoneVerificationSpec(normalizedPhone), ct);
        if (latest is null
            || string.IsNullOrWhiteSpace(signupToken)
            || !latest.CanBeUsedForSignup(utcNow)
            || !HashEquals(ComputeHash(latest.Id, TokenPurpose, signupToken.Trim()), latest.SignupTokenHash!))
        {
            return Result<SignupPhoneVerification>.Failure(SignupPhoneErrors.VerificationRequired);
        }

        return Result<SignupPhoneVerification>.Success(latest);
    }

    /// <summary>Satırı yeni kiracıyla kullanır ve numarayı kullanıcıda doğrulanmış olarak saklar.</summary>
    public static void UseForSignup(SignupPhoneVerification verification, Guid tenantId, User owner, DateTime utcNow)
    {
        verification.MarkUsed(tenantId, utcNow);
        owner.SetVerifiedPhone(verification.Phone, verification.VerifiedAtUtc!.Value, utcNow);
    }

    /// <summary>Netgsm OTP kuralı: tek SMS boyu, Türkçe karaktersiz.</summary>
    public static string CodeMessage(string rawCode)
        => $"Vetinity dogrulama kodunuz: {rawCode}. Kod {(int)SignupPhoneVerification.CodeLifetime.TotalMinutes} dakika gecerlidir. Kimseyle paylasmayin.";

    public const string CodePurpose = "code";
    public const string TokenPurpose = "token";

    /// <summary>Kod ve jeton özeti: satır kimliği ve amaçla tuzlanır (test kurulumu da bu yöntemi kullanır).</summary>
    public static string ComputeSecretHash(ITokenHashService hash, Guid verificationId, string purpose, string raw)
        => hash.ComputeSha256($"{verificationId:N}:{purpose}:{raw}");

    private string ComputeHash(Guid id, string purpose, string raw) => ComputeSecretHash(_hash, id, purpose, raw);

    private static bool HashEquals(string computed, string stored)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(stored));
}
