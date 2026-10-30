using Backend.Veteriner.Domain.Prescriptions;

namespace Backend.Veteriner.Application.Prescriptions;

/// <summary>Reçete durum metni (RECORD-008); liste, detay, muayene özeti ve timeline aynı değeri kullanır.</summary>
public static class PrescriptionStatusText
{
    public const string Active = "Active";
    public const string Cancelled = "Cancelled";

    public static string Of(Prescription p) => p.IsCancelled ? Cancelled : Active;
}
