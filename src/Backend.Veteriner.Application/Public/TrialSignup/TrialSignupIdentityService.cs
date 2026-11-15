using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Public.TrialSignup;

/// <summary>
/// Tek deneme kuralı (TRIAL-001): bir e-posta ve bir vergi numarası (VKN ya da TCKN) yalnız bir deneme hesabı açar. Kural
/// <see cref="TrialSignupIdentity"/>'de, vergi numarası zorunluluğu <see cref="TrialSignupOptions"/>'dadır.
/// </summary>
public sealed class TrialSignupIdentityService
{
    private readonly IRepository<TrialSignupIdentity> _identities;
    private readonly IReadRepository<User> _users;
    private readonly TrialSignupOptions _options;

    public TrialSignupIdentityService(
        IRepository<TrialSignupIdentity> identities,
        IReadRepository<User> users,
        IOptions<TrialSignupOptions> options)
    {
        _identities = identities;
        _users = users;
        _options = options.Value;
    }

    public bool TaxIdentityRequired => _options.RequireTaxIdentityNumber;

    /// <summary>Adresle (ya da aynı kutuya giden <c>+etiket</c>'li biçimiyle) daha önce deneme açılmamış ve kullanıcı yok.</summary>
    public async Task<Result> EnsureEmailAvailableAsync(string email, CancellationToken ct)
    {
        var key = TrialSignupIdentity.ToEmailKey(email);
        if (key is null)
            return Result.Failure(TrialSignupErrors.VerificationRequired);

        if (await _identities.AnyAsync(new TrialSignupIdentityByEmailKeySpec(key), ct)
            || await _users.AnyAsync(new UserExistsByEmailSpec(email.Trim()), ct))
        {
            return Result.Failure(TrialSignupErrors.DuplicateEmail);
        }

        return Result.Success();
    }

    /// <summary>
    /// Vergi numarasını denetler ve saklama biçimine çevirir: zorunluysa boş olamaz; doluysa geçerli VKN/TCKN olmalı ve daha önce
    /// deneme açmamış olmalı. Zorunlu değil ve boşsa <c>null</c> döner.
    /// </summary>
    public async Task<Result<string?>> NormalizeAvailableTaxIdentityAsync(string? taxIdentityNumber, CancellationToken ct)
    {
        var tax = TurkishTaxIdentity.Normalize(taxIdentityNumber);
        if (tax is null)
        {
            return _options.RequireTaxIdentityNumber
                ? Result<string?>.Failure(TrialSignupErrors.TaxIdentityRequired)
                : Result<string?>.Success(null);
        }

        if (!TurkishTaxIdentity.IsValidTaxpayerIdentity(tax))
            return Result<string?>.Failure(TrialSignupErrors.TaxIdentityRequired);

        if (await _identities.AnyAsync(new TrialSignupIdentityByTaxNumberSpec(tax), ct))
            return Result<string?>.Failure(TrialSignupErrors.DuplicateTaxIdentity);

        return Result<string?>.Success(tax);
    }

    /// <summary>Yeni kiracının tek deneme kaydını ekler; kayıtla aynı işlemde yazılır (benzersiz indeks yarışı da yakalar).</summary>
    public Task AddAsync(Guid tenantId, string email, string? taxIdentityNumber, DateTime utcNow, CancellationToken ct)
        => _identities.AddAsync(TrialSignupIdentity.Create(tenantId, email, taxIdentityNumber, utcNow), ct);
}
