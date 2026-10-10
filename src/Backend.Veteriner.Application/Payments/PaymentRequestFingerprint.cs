using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Payments.Commands.Create;

namespace Backend.Veteriner.Application.Payments;

/// <summary>
/// Ödeme isteğinin içerik özeti (SHA-256, 64 hex). Aynı <c>requestId</c> aynı içerikle gelirse tekrar,
/// farklı içerikle gelirse çakışma olarak ayırt edilir.
/// </summary>
internal static class PaymentRequestFingerprint
{
    public static string Compute(CreatePaymentCommand request, Guid clinicId, DateTime paidAtUtc)
    {
        var allocations = (request.Allocations ?? [])
            .OrderBy(a => a.ChargeId)
            .Select(a => $"{a.ChargeId:N}:{a.Amount.ToString("0.00", CultureInfo.InvariantCulture)}");

        var canonical = string.Join('|',
            clinicId.ToString("N"),
            request.ClientId.ToString("N"),
            request.PetId?.ToString("N"),
            request.AppointmentId?.ToString("N"),
            request.ExaminationId?.ToString("N"),
            request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            request.Currency?.Trim().ToUpperInvariant(),
            (int)request.Method,
            paidAtUtc.ToString("O", CultureInfo.InvariantCulture),
            request.Notes?.Trim(),
            string.Join(',', allocations));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
