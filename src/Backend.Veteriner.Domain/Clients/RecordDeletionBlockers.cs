using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clients;

/// <summary>
/// Müşteri/hasta silmesini engelleyen açık işler (ADR-054 K5). Sayımı uygulama katmanı okur; kararı
/// <see cref="Client.Delete"/> ve <see cref="Pets.Pet.Delete"/> verir. Hata, nedenlerin tamamını tek seferde taşır.
/// </summary>
public sealed record RecordDeletionBlockers(
    bool HasOpenAccount,
    int ActiveHospitalizationCount,
    int FutureAppointmentCount,
    int OpenVisitCount = 0)
{
    public const string OpenAccount = "OpenAccount";
    public const string ActiveHospitalization = "ActiveHospitalization";
    public const string FutureAppointments = "FutureAppointments";
    public const string OpenVisit = "OpenVisit";

    public static readonly RecordDeletionBlockers None = new(false, 0, 0);

    public bool IsBlocking => HasOpenAccount || ActiveHospitalizationCount > 0 || OpenVisitCount > 0 || FutureAppointmentCount > 0;

    public IReadOnlyList<string> Reasons
    {
        get
        {
            var reasons = new List<string>(4);
            if (HasOpenAccount)
                reasons.Add(OpenAccount);
            if (ActiveHospitalizationCount > 0)
                reasons.Add(ActiveHospitalization);
            if (OpenVisitCount > 0)
                reasons.Add(OpenVisit);
            if (FutureAppointmentCount > 0)
                reasons.Add(FutureAppointments);
            return reasons;
        }
    }

    /// <summary><c>reasons</c>, <c>activeHospitalizationCount</c>, <c>openVisitCount</c> ve <c>futureAppointmentCount</c> yanıt eklerinde döner.</summary>
    public Error ToError(string code, string subject)
    {
        var parts = new List<string>(4);
        if (HasOpenAccount)
            parts.Add("açık hesabı var (borç, tahsis edilmemiş ödeme ya da kredi); önce hesabı kapatın");
        if (ActiveHospitalizationCount > 0)
            parts.Add("aktif yatışı var; önce taburcu edin ya da yatışı iptal edin");
        if (OpenVisitCount > 0)
            parts.Add("bugün açık gelişi var; önce gelişi tamamlayın ya da iptal edin");
        if (FutureAppointmentCount > 0)
            parts.Add($"{FutureAppointmentCount} gelecek randevusu var; önce randevuları iptal edin");

        return new Error(
            code,
            $"{subject} silinemez: {string.Join("; ", parts)}.",
            new Dictionary<string, object?>
            {
                ["reasons"] = Reasons,
                ["activeHospitalizationCount"] = ActiveHospitalizationCount,
                ["openVisitCount"] = OpenVisitCount,
                ["futureAppointmentCount"] = FutureAppointmentCount,
            });
    }
}
