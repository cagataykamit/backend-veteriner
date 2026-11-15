using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Public.TrialSignup;

/// <summary>Adrese son <see cref="SignupEmailVerification.DailyWindow"/> içinde gönderilen kodlar (bekleme ve adres sınırı).</summary>
public sealed class SignupEmailCodesForEmailSinceSpec : Specification<SignupEmailVerification>
{
    public SignupEmailCodesForEmailSinceSpec(string normalizedEmail, DateTime sinceUtc)
        => Query.Where(x => x.Email == normalizedEmail && x.CreatedAtUtc >= sinceUtc);
}

public sealed class SignupEmailCodesForIpSinceSpec : Specification<SignupEmailVerification>
{
    public SignupEmailCodesForIpSinceSpec(string requestIpHash, DateTime sinceUtc)
        => Query.Where(x => x.RequestIpHash == requestIpHash && x.CreatedAtUtc >= sinceUtc);
}

public sealed class SignupEmailCodesSinceSpec : Specification<SignupEmailVerification>
{
    public SignupEmailCodesSinceSpec(DateTime sinceUtc)
        => Query.Where(x => x.CreatedAtUtc >= sinceUtc);
}

/// <summary>Adresin son kodu; doğrulama ve kayıt yalnız son satırla yapılır.</summary>
public sealed class LatestSignupEmailVerificationSpec : Specification<SignupEmailVerification>
{
    public LatestSignupEmailVerificationSpec(string normalizedEmail)
        => Query.Where(x => x.Email == normalizedEmail)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(1);
}

public sealed class TrialSignupIdentityByEmailKeySpec : Specification<TrialSignupIdentity>
{
    public TrialSignupIdentityByEmailKeySpec(string emailKey)
        => Query.Where(x => x.EmailKey == emailKey);
}

public sealed class TrialSignupIdentityByTaxNumberSpec : Specification<TrialSignupIdentity>
{
    public TrialSignupIdentityByTaxNumberSpec(string taxIdentityNumber)
        => Query.Where(x => x.TaxIdentityNumber == taxIdentityNumber);
}
