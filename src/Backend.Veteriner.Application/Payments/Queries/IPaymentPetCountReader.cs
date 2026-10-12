namespace Backend.Veteriner.Application.Payments.Queries;

/// <summary>Ödemelerin tahsis edilen ücretlerindeki farklı hayvan sayısı (Command DB; sözleşme §4 <c>petCount</c>).</summary>
public interface IPaymentPetCountReader
{
    /// <summary>Yalnızca tahsisinde hayvanlı ücret olan ödemeler dönülür; olmayanlar sözlükte yer almaz.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountDistinctPetsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> paymentIds, CancellationToken ct);
}

public static class PaymentPetCount
{
    /// <summary>Tahsisten sayı yoksa (eski ödeme) ödemenin kendi hayvanı 1, o da yoksa 0 sayılır.</summary>
    public static int Resolve(Guid? paymentPetId, int allocatedPetCount)
        => Math.Max(allocatedPetCount, paymentPetId.HasValue ? 1 : 0);
}