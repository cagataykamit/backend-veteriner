using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Charges;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments;

/// <summary>
/// Ödeme oluşturma birim testleri için ortak tahsis kurulumu: müşteriye ait, geniş açık tutarlı tek ücret ve kullanıcı bağlamı.
/// </summary>
internal static class PaymentAllocationTestSupport
{
    public static readonly Guid ChargeId = Guid.NewGuid();

    public static IReadOnlyList<PaymentAllocationLineDto> Lines(decimal amount) => [new(ChargeId, amount)];

    public static Charge NewCharge(Guid tenantId, Guid clinicId, Guid clientId, decimal unitPrice = 1_000_000m)
    {
        var charge = Charge.Create(
                tenantId, clinicId, clientId, null, null, null, "Muayene", 1m, unitPrice,
                Guid.NewGuid(), DateTime.UtcNow, null)
            .Value!;
        typeof(Charge).GetProperty(nameof(Charge.Id))!.SetValue(charge, ChargeId);
        return charge;
    }

    public static void Setup(
        Mock<IRepository<Charge>> chargesWrite,
        Mock<IClientContext> clientContext,
        Guid tenantId,
        Guid clinicId,
        Guid clientId)
    {
        clientContext.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        chargesWrite
            .Setup(r => r.ListAsync(It.IsAny<ChargesByIdsForUpdateSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCharge(tenantId, clinicId, clientId)]);
    }
}
