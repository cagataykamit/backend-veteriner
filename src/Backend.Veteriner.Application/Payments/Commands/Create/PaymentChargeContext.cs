using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Payments.Commands.Create;

/// <summary>
/// Ödemenin hayvan/muayene bağlamı tahsis edilen ücretlerden türetilir (ADR-012). Ücretlerin tamamı aynı
/// değeri taşıyorsa o değer, aksi halde (farklı ya da boş) null döner; birden fazla hayvan tek hayvana indirgenmez.
/// </summary>
internal static class PaymentChargeContext
{
    public static Guid? CommonPetId(IReadOnlyCollection<Charge> charges) => Common(charges, c => c.PetId);

    public static Guid? CommonExaminationId(IReadOnlyCollection<Charge> charges) => Common(charges, c => c.ExaminationId);

    private static Guid? Common(IReadOnlyCollection<Charge> charges, Func<Charge, Guid?> selector)
    {
        var values = charges.Select(selector).Distinct().ToArray();
        return values.Length == 1 ? values[0] : null;
    }
}