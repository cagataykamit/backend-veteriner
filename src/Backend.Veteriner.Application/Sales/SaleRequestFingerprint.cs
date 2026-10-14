using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Sales.Commands.Create;

namespace Backend.Veteriner.Application.Sales;

/// <summary>
/// Satış isteğinin içerik özeti (SHA-256, 64 hex), istekte verilen değerlerden hesaplanır (varsayılanlar ve saat dahil değildir).
/// Aynı <c>requestId</c> aynı içerikle gelirse tekrar, farklı içerikle gelirse çakışma olarak ayırt edilir.
/// </summary>
internal static class SaleRequestFingerprint
{
    public static string Compute(CreateSaleCommand request, Guid clinicId)
    {
        var lines = (request.Lines ?? [])
            .Select(l => string.Join(':',
                l.ProductId.ToString("N"),
                l.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                l.UnitPrice?.ToString("0.00", CultureInfo.InvariantCulture),
                l.BatchId?.ToString("N")));

        var payment = request.Payment is null
            ? string.Empty
            : string.Join(':',
                request.Payment.Method?.Trim().ToUpperInvariant(),
                request.Payment.Amount?.ToString("0.00", CultureInfo.InvariantCulture),
                request.Payment.PaidAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                request.Payment.Notes?.Trim());

        var canonical = string.Join('|',
            clinicId.ToString("N"),
            request.ClientId?.ToString("N"),
            request.Notes?.Trim(),
            string.Join(',', lines),
            payment);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
