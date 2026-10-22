using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Files;

/// <summary>
/// HMAC-SHA256 imzası (<c>fileId</c> + <c>expires</c>), Base64Url. Anahtar <c>FileStorage:AccessUrlSigningKey</c> (Base64, ≥ 32 bayt);
/// verilmezse süreç başına rastgele anahtar üretilir (yeniden başlatmada açık URL'ler geçersizleşir, tek örnek için yeterli).
/// Tekil (singleton) kaydedilir.
/// </summary>
public sealed class HmacFileAccessSigner : IFileAccessSigner
{
    private const int MinKeyBytes = 32;

    private readonly byte[] _key;

    public HmacFileAccessSigner(IOptions<FileStorageOptions> options, ILogger<HmacFileAccessSigner> logger)
    {
        var configured = options.Value.AccessUrlSigningKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var bytes = new byte[configured.Length];
            if (!Convert.TryFromBase64String(configured.Trim(), bytes, out var written) || written < MinKeyBytes)
            {
                throw new InvalidOperationException(
                    $"FileStorage:AccessUrlSigningKey en az {MinKeyBytes} baytlık geçerli Base64 olmalıdır.");
            }

            _key = bytes[..written];
            return;
        }

        _key = RandomNumberGenerator.GetBytes(MinKeyBytes);
        logger.LogInformation("FileStorage:AccessUrlSigningKey not configured; using a per-process random key.");
    }

    public string Sign(Guid fileId, long expiresUnixSeconds)
        => Base64Url(Compute(fileId, expiresUnixSeconds));

    public bool IsValid(Guid fileId, long expiresUnixSeconds, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || !TryDecodeBase64Url(signature, out var given))
            return false;

        return CryptographicOperations.FixedTimeEquals(Compute(fileId, expiresUnixSeconds), given);
    }

    private byte[] Compute(Guid fileId, long expiresUnixSeconds)
        => HMACSHA256.HashData(
            _key,
            Encoding.ASCII.GetBytes($"{fileId:N}.{expiresUnixSeconds.ToString(CultureInfo.InvariantCulture)}"));

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        bytes = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, bytes, out var written))
            return false;
        bytes = bytes[..written];
        return true;
    }
}
