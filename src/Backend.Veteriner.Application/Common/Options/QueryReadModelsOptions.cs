namespace Backend.Veteriner.Application.Common.Options;

public sealed class QueryReadModelsOptions
{
    public const string SectionName = "QueryReadModels";

    public bool AppointmentsEnabled { get; set; }

    public bool DashboardAppointmentsEnabled { get; set; }

    public bool ClientsEnabled { get; set; }

    public bool PetsEnabled { get; set; }

    /// <summary>
    /// Paylaşılan client/pet metin araması lookup'ını Query DB read-model'e yönlendirir (12D-3+).
    /// Liste endpoint bayraklarından (<see cref="ClientsEnabled"/>, <see cref="PetsEnabled"/>) bağımsızdır.
    /// </summary>
    public bool SharedSearchLookupEnabled { get; set; }

    /// <summary>
    /// Ödeme listesi Strateji B search resolution'ını Query DB read-model'e yönlendirir (12D-7+).
    /// <see cref="SharedSearchLookupEnabled"/> ve finansal rapor/export yüzeylerinden bağımsızdır.
    /// </summary>
    public bool PaymentsSearchLookupEnabled { get; set; }

    /// <summary>
    /// Dashboard finance özetini Query DB <c>ClinicDailyPaymentStatsReadModel</c> üzerinden okur (13E+).
    /// Kapalıyken Command DB aggregate yolu korunur. Recent payments + isim hydration her iki yolda Command DB'den kalır.
    /// </summary>
    public bool DashboardFinanceReadEnabled { get; set; }

    /// <summary>
    /// Ödeme listesini Query DB <c>PaymentReadModels</c> reader üzerinden okur (14E+).
    /// Yalnızca arama (search) boş/null ve klinik kapsamı tek kliniğe çözülebiliyorken Query DB yolu kullanılır;
    /// arama dolu ise bilinçli olarak Command DB yolunda kalınır (search parity ayrı fazda ele alınır).
    /// Query DB yolu seçildiğinde Command DB'ye fallback yapılmaz; Query DB boşsa boş liste döner.
    /// </summary>
    public bool PaymentsListReadEnabled { get; set; }
}
