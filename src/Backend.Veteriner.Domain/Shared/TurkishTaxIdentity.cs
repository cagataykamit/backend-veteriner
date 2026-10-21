namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// T.C. kimlik numarası (TCKN) ve vergi kimlik numarası (VKN) doğrulama ve maskeleme kuralları (ADR-036).
/// Algoritmalar resmî sağlama kurallarıdır; kural yalnızca burada yaşar.
/// </summary>
public static class TurkishTaxIdentity
{
    public const int NationalIdLength = 11;
    public const int TaxNumberLength = 10;

    /// <summary>Mükellef kimliği (VKN veya TCKN) için saklama uzunluğu.</summary>
    public const int TaxpayerIdentityMaxLength = NationalIdLength;

    private const int MaskVisiblePrefix = 3;
    private const int MaskVisibleSuffix = 2;

    /// <summary>
    /// TCKN: 11 rakam, ilk hane 0 değil; 10. hane = ((tek sıradakiler toplamı × 7) − çift sıradakiler toplamı) mod 10
    /// (ilk 9 hane), 11. hane = ilk 10 hanenin toplamı mod 10.
    /// </summary>
    public static bool IsValidNationalId(string? value)
    {
        if (!TryGetDigits(value, NationalIdLength, out var d) || d[0] == 0)
            return false;

        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var tenth = ((odd * 7 - even) % 10 + 10) % 10;
        if (d[9] != tenth)
            return false;

        var sum = 0;
        for (var i = 0; i < 10; i++)
            sum += d[i];
        return d[10] == sum % 10;
    }

    /// <summary>
    /// VKN: 10 rakam; i = 1..9 için t = (dᵢ + 10 − i) mod 10; t = 9 ise 9, değilse (t × 2^(10 − i)) mod 9 toplanır;
    /// kontrol hanesi (10 − toplam mod 10) mod 10.
    /// </summary>
    public static bool IsValidTaxNumber(string? value)
    {
        if (!TryGetDigits(value, TaxNumberLength, out var d))
            return false;

        var sum = 0;
        for (var i = 1; i <= 9; i++)
        {
            var t = (d[i - 1] + 10 - i) % 10;
            sum += t == 9 ? 9 : t * (1 << (10 - i)) % 9;
        }

        return d[9] == (10 - sum % 10) % 10;
    }

    /// <summary>Uzunluğa göre: 10 hane geçerli VKN veya 11 hane geçerli TCKN (mükellef kimliği).</summary>
    public static bool IsValidTaxpayerIdentity(string? value)
        => IsValidTaxNumber(value) || IsValidNationalId(value);

    /// <summary>
    /// Ekranda ve kayıtta gösterim: ilk 3 ve son 2 karakter görünür, aradakiler <c>*</c>; 5 karakter ve altı tamamen <c>*</c>.
    /// Boş değer <c>null</c> döner.
    /// </summary>
    public static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (text.Length <= MaskVisiblePrefix + MaskVisibleSuffix)
            return new string('*', text.Length);

        return string.Concat(
            text.AsSpan(0, MaskVisiblePrefix),
            new string('*', text.Length - MaskVisiblePrefix - MaskVisibleSuffix),
            text.AsSpan(text.Length - MaskVisibleSuffix));
    }

    /// <summary>Saklama biçimi: kırpılmış değer; boşsa <c>null</c>. Biçim doğrulaması çağıranındır.</summary>
    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryGetDigits(string? value, int length, out int[] digits)
    {
        digits = [];
        var text = value?.Trim();
        if (text is null || text.Length != length)
            return false;

        var result = new int[length];
        for (var i = 0; i < length; i++)
        {
            var ch = text[i];
            if (ch is < '0' or > '9')
                return false;
            result[i] = ch - '0';
        }

        digits = result;
        return true;
    }
}
