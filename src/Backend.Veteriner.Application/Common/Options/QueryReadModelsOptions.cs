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
    /// Kapalıyken Command DB aggregate yolu korunur. Recent payments routing için
    /// <see cref="DashboardRecentPaymentsReadEnabled"/> kullanılır.
    /// </summary>
    public bool DashboardFinanceReadEnabled { get; set; }

    /// <summary>
    /// Ödeme listesini Query DB <c>PaymentReadModels</c> reader üzerinden okur (14E+).
    /// Yalnızca arama (search) boş/null ve klinik kapsamı tek kliniğe çözülebiliyorken Query DB yolu kullanılır;
    /// arama dolu ise bilinçli olarak Command DB yolunda kalınır (search parity ayrı fazda ele alınır).
    /// Query DB yolu seçildiğinde Command DB'ye fallback yapılmaz; Query DB boşsa boş liste döner.
    /// </summary>
    public bool PaymentsListReadEnabled { get; set; }

    /// <summary>
    /// Dashboard finance özetindeki recent payments bölümünü Query DB <c>PaymentReadModels</c> üzerinden okur (15B+).
    /// Yalnızca klinik kapsamı tek kliniğe (<see cref="Clinics.Access.ClinicReadScope.SingleClinicId"/>) çözülebiliyorken Query DB yolu kullanılır;
    /// tenant-wide veya multi-clinic scope'ta bilinçli olarak Command DB yolunda kalınır.
    /// Query DB yolu seçildiğinde Command DB'ye fallback yapılmaz; Query DB boşsa recent payments boş döner.
    /// <see cref="DashboardFinanceReadEnabled"/> totals/trend routing'inden bağımsızdır.
    /// </summary>
    public bool DashboardRecentPaymentsReadEnabled { get; set; }

    /// <summary>
    /// Client payment summary (GET /clients/{id}/payment-summary) aggregate + recent payments bölümünü
    /// Query DB <c>PaymentReadModels</c> üzerinden okur (15E+).
    /// Klinik kapsamı tek kliniğe (<see cref="Clinics.Access.ClinicReadScope.SingleClinicId"/>) ya da tenant-wide'a
    /// (Admin/Owner için clinic filtresi yok) çözülebiliyorken Query DB yolu kullanılır; multi-clinic (ClinicAdmin,
    /// aktif klinik yok) scope represent edilemediğinden bilinçli olarak Command DB yolunda kalınır.
    /// Query DB yolu seçildiğinde Command DB'ye fallback yapılmaz; Query DB boşsa count 0 / totals boş / recent boş döner.
    /// </summary>
    public bool ClientPaymentSummaryReadEnabled { get; set; }

    /// <summary>
    /// Payment report JSON yüzeyini (GET /api/v1/reports/payments) Query DB <c>PaymentReadModels</c> üzerinden okur (15G).
    /// Yalnızca arama (search) boş/null ve klinik kapsamı tek kliniğe (<see cref="Clinics.Access.ClinicReadScope.SingleClinicId"/>)
    /// ya da tenant-wide'a (Admin/Owner için clinic filtresi yok) çözülebiliyorken Query DB yolu kullanılır; arama dolu veya
    /// multi-clinic (ClinicAdmin, aktif klinik yok) scope'ta bilinçli olarak Command DB yolunda kalınır (search parity ayrı fazda).
    /// Query DB yolu seçildiğinde Command DB'ye fallback yapılmaz; Query DB boşsa boş/zero rapor döner.
    /// Export CSV/XLSX yüzeyleri bu bayraktan etkilenmez; export için
    /// <see cref="PaymentsReportExportReadEnabled"/> kullanılır.
    /// </summary>
    public bool PaymentsReportReadEnabled { get; set; }

    /// <summary>
    /// Payment export CSV/XLSX yüzeylerini Query DB <c>PaymentReadModels</c> üzerinden okur (15J).
    /// Yalnızca arama (search) boş/null ve klinik kapsamı tek kliniğe (<see cref="Clinics.Access.ClinicReadScope.SingleClinicId"/>)
    /// ya da tenant-wide'a (Admin/Owner için clinic filtresi yok) çözülebiliyorken Query DB yolu kullanılır; arama dolu veya
    /// multi-clinic (ClinicAdmin, aktif klinik yok) scope'ta bilinçli olarak Command DB yolunda kalınır (search parity ayrı fazda).
    /// Query DB yolu seçildiğinde Command DB'ye fallback yapılmaz; Query DB boşsa boş export döner.
    /// JSON report yüzeyi (<see cref="PaymentsReportReadEnabled"/>) bu bayraktan etkilenmez.
    /// </summary>
    public bool PaymentsReportExportReadEnabled { get; set; }
}
