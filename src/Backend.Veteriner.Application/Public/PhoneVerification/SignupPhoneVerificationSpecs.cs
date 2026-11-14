using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Public.PhoneVerification;

/// <summary>Numarayla deneme hesabı açılmış mı (kullanılmış doğrulama satırı).</summary>
public sealed class UsedSignupPhoneSpec : Specification<SignupPhoneVerification>
{
    public UsedSignupPhoneSpec(string normalizedPhone)
        => Query.Where(x => x.Phone == normalizedPhone && x.UsedAtUtc != null);
}

/// <summary>Numaraya son <see cref="SignupPhoneVerification.DailyWindow"/> içinde gönderilen kodlar (bekleme ve numara sınırı).</summary>
public sealed class SignupPhoneCodesForPhoneSinceSpec : Specification<SignupPhoneVerification>
{
    public SignupPhoneCodesForPhoneSinceSpec(string normalizedPhone, DateTime sinceUtc)
        => Query.Where(x => x.Phone == normalizedPhone && x.CreatedAtUtc >= sinceUtc);
}

public sealed class SignupPhoneCodesForIpSinceSpec : Specification<SignupPhoneVerification>
{
    public SignupPhoneCodesForIpSinceSpec(string requestIpHash, DateTime sinceUtc)
        => Query.Where(x => x.RequestIpHash == requestIpHash && x.CreatedAtUtc >= sinceUtc);
}

public sealed class SignupPhoneCodesSinceSpec : Specification<SignupPhoneVerification>
{
    public SignupPhoneCodesSinceSpec(DateTime sinceUtc)
        => Query.Where(x => x.CreatedAtUtc >= sinceUtc);
}

/// <summary>Numaranın son kodu; doğrulama ve kayıt yalnız son satırla yapılır.</summary>
public sealed class LatestSignupPhoneVerificationSpec : Specification<SignupPhoneVerification>
{
    public LatestSignupPhoneVerificationSpec(string normalizedPhone)
        => Query.Where(x => x.Phone == normalizedPhone)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(1);
}
