using System.Globalization;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Reading.EVet;

/// <summary>
/// E-vet dönüştürmesinin okuma notları (IMPORT_API_CONTRACT.md §7.2.1): her not türünün kalıcı kodu, türü (karar / dikkat / bilgi /
/// aktarılmayacak), tam cümlesi ve satır alanları tek yerde. Dönüştürücü yalnız sayıları verir.
/// </summary>
internal static class EVetNotes
{
    private const string Prefix = "Imports.EVet.";

    public const string OwnerLinkCode = Prefix + "OwnerLink";
    public const string OwnerReportMissingCode = Prefix + "OwnerReportMissing";
    public const string ChipToNoteCode = Prefix + "ChipToNote";
    public const string FormerRecordsMergedCode = Prefix + "FormerRecordsMerged";
    public const string SameNameRenamedCode = Prefix + "SameNameRenamed";
    public const string DuplicateProtocolCode = Prefix + "DuplicateProtocol";
    public const string SameOwnerSameNameCode = Prefix + "SameOwnerSameName";
    public const string TaskConversionCode = Prefix + "TaskConversion";
    public const string TasksToRecordsCode = Prefix + "TasksToRecords";
    public const string EarlyCompletedCode = Prefix + "EarlyCompleted";
    public const string TasksSkippedCode = Prefix + "TasksSkipped";
    public const string VaccineReportCode = Prefix + "VaccineReport";
    public const string PassiveProductsCode = Prefix + "PassiveProducts";

    private const string PetReport = "Hasta Listesi raporu";
    private const string VaccineReportSource = "Aşı Bilgisi raporu";
    private const string Backup = "E-vet yedeği";
    private const string TaskList = "Randevu Listesi";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static ImportReadWarning OwnerLink(int byReport, int byName)
        => new(OwnerLinkCode, ImportTable.Pets,
            $"{byReport} hasta sahibine Hasta Listesi raporundaki protokolle, {byName} hasta sahip adıyla bağlanacak.")
        {
            Severity = ImportNoteSeverity.Info,
            Count = byReport + byName,
            Unit = "hasta",
            Title = "sahibine bağlanacak",
            Source = PetReport,
            Parts = [new("Hasta Listesi raporundaki protokolle", byReport), new("Sahip adıyla", byName)],
        };

    public static ImportReadWarning OwnerReportMissing(int pets)
        => new(OwnerReportMissingCode, ImportTable.Pets,
            "Hasta Listesi raporu yüklenmedi: hastalar sahibine yedekteki sahip adıyla bağlanacak; aynı adlı müşterilerin hastaları hata alır.")
        {
            Severity = ImportNoteSeverity.Attention,
            Count = pets,
            Unit = "hasta",
            Title = "sahibine yalnız adla bağlanacak",
            Source = Backup,
            Outcome = "Hasta Listesi raporu yüklenmedi. Aynı adlı müşterilerin hastaları hata alır; raporla yeniden yüklerseniz protokolle bağlanır.",
        };

    public static ImportReadWarning ChipToNote(int pets)
        => new(ChipToNoteCode, ImportTable.Pets,
            $"{pets} hastanın mikroçipi rakam dışı karakter içeriyor ya da başka hastada da kayıtlı; numara hastanın notuna yazılacak.")
        {
            Severity = ImportNoteSeverity.Attention,
            Count = pets,
            Unit = "hasta",
            Title = "mikroçip numarası nota taşınacak",
            Source = Backup,
            Outcome = "Numara rakam dışı karakter içeriyor ya da başka hastada da kayıtlı. Mikroçip alanı boş kalır, numara hastanın notuna yazılır.",
        };

    public static ImportReadWarning FormerRecordsMerged(int byProtocol, int byChip, int byCopy)
        => new(FormerRecordsMergedCode, ImportTable.Pets,
            $"Aynı hayvanın {byProtocol + byChip + byCopy} ek kaydı tek hastada birleşecek (sahibi değişen hastanın eski "
            + $"sahipteki kaydı: {byProtocol} aynı protokolle, {byChip} aynı mikroçiple; aynı sahipte {byCopy} kopya kayıt): "
            + "birleşen kayıt vefat olarak gelmez, aşı ve muayene geçmişi hastaya yazılır, önceki sahip ve kopya hastanın notunda kalır.")
        {
            Severity = ImportNoteSeverity.Info,
            Count = byProtocol + byChip + byCopy,
            Unit = "ek kayıt",
            Title = "aynı hayvanın kaydıyla birleşecek",
            Source = Backup,
            Parts =
            [
                new("Eski sahipteki kaydı, aynı protokolle", byProtocol),
                new("Eski sahipteki kaydı, aynı mikroçiple", byChip),
                new("Aynı sahipte kopya kayıt", byCopy),
            ],
            Outcome = "Birleşen kayıt vefat olarak gelmez. Aşı ve muayene geçmişi hastaya yazılır; önceki sahip ve kopya hastanın notunda kalır.",
        };

    /// <summary>Ayrı aktarılan (yeniden adlandırılan) hasta varsa dikkat; yalnız birleşen tekrar kayıt varsa bilgi.</summary>
    public static ImportReadWarning SameNameRenamed(int renamed, int merged)
        => new(SameNameRenamedCode, ImportTable.Pets,
            $"Aynı sahipte aynı ad ve türle birden çok hasta var: bilgileri farklı {renamed} hasta ayrı aktarılacak ve adına E-vet "
            + $"protokolü eklenecek (ör. Pamuk (E-vet 1234)); bilgileri aynı {merged} tekrar kayıt ilk kayıtla birleşecek. Muayene ve "
            + "görevler o tarihte kayıtlı ve hayattaki tek hastaya bağlanır; ayırt edilemeyen satır hata alır (yanlış hastaya yazılmaz).")
        {
            Severity = renamed > 0 ? ImportNoteSeverity.Attention : ImportNoteSeverity.Info,
            Count = renamed > 0 ? renamed : merged,
            Unit = renamed > 0 ? "hasta" : "tekrar kayıt",
            Title = renamed > 0 ? "adına E-vet protokolü eklenecek" : "ilk kayıtla birleşecek",
            Source = Backup,
            Parts =
            [
                new("Bilgileri farklı: ayrı aktarılacak", renamed, ImportNoteSeverity.Attention),
                new("Bilgileri aynı: ilk kayıtla birleşecek", merged),
            ],
            Example = renamed > 0 ? "Pamuk (E-vet 1234)" : null,
            Outcome = "Aynı sahipte aynı ad ve türle birden çok hasta var. Muayene ve görevler o tarihte kayıtlı ve hayattaki tek hastaya "
                      + "bağlanır; ayırt edilemeyen satır hata alır, yanlış hastaya yazılmaz.",
        };

    public static ImportReadWarning DuplicateProtocol(int customers, int pets)
        => new(DuplicateProtocolCode, ImportTable.Pets,
            $"Yedekte {customers} müşteri ve {pets} hasta protokol numarası birden çok kayıtta geçiyor; "
            + "bu kayıtların numarasına satır numarası eklendi (ör. 1234#56); tek aktif kaydın sahibi Hasta Listesi raporundan, diğerlerinin sahibi adla bağlanacak.")
        {
            Severity = ImportNoteSeverity.Attention,
            Count = customers + pets,
            Unit = "protokol numarası",
            Title = "birden çok kayıtta geçiyor",
            Source = Backup,
            Parts = [new("Müşteri", customers), new("Hasta", pets)],
            Example = "1234#56",
            Outcome = "Bu kayıtların numarasına satır numarası eklendi. Tek aktif kaydın sahibi Hasta Listesi raporundan, diğerlerinin sahibi adla bağlanacak.",
        };

    /// <summary>Hastası seçilecek satır kalırsa karar (Doğrulama adımında "Sizden beklenen"); kalmazsa bilgi.</summary>
    public static ImportReadWarning SameOwnerSameName(int resolved, int unresolved)
    {
        var decision = unresolved > 0;
        return new(SameOwnerSameNameCode, ImportTable.Pets,
            $"Aynı sahipte aynı adlı birden çok hasta: {resolved} muayene / görev satırı tarihten (doğum, E-vet kaydı, vefat), "
            + "türden (aşı adı, Aşı Bilgisi raporu), mikroçipten, kilodan ya da tek aktif kayıttan doğru hastaya kendiliğinden bağlandı"
            + (decision ? $"; {unresolved} satırın hangi hastaya ait olduğu aktarma ekranında sorulacak." : "."))
        {
            Severity = decision ? ImportNoteSeverity.Decision : ImportNoteSeverity.Info,
            Count = decision ? unresolved : resolved,
            Unit = "satır",
            Title = decision ? "hangi hastaya ait, sizden sorulacak" : "doğru hastaya kendiliğinden bağlandı",
            Source = "Randevu ve Muayene Listesi",
            Parts = decision
                ? [new("Hastası size sorulacak", unresolved, ImportNoteSeverity.Decision), new("Doğru hastaya kendiliğinden bağlandı", resolved)]
                : null,
            Outcome = "Aynı sahipte aynı adlı birden çok hasta var. Kendiliğinden bağlananlar tarihten (doğum, E-vet kaydı, vefat), türden, "
                      + "mikroçipten, kilodan ya da tek aktif kayıttan eşleşti.",
            Target = decision ? new ImportNoteTarget(ImportNoteTarget.Required) : null,
        };
    }

    public static ImportReadWarning TaskConversion(int vaccinations, int appointments, int dueDates)
        => new(TaskConversionCode, ImportTable.Vaccinations,
            $"Randevu Listesi: {vaccinations} tamamlanmış aşı aşı geçmişine, {appointments} gelecek görev randevuya dönüşecek; "
            + $"{dueDates} aşının sonraki aşı tarihi E-vet'teki yapılacak aşı görevinden alındı.")
        {
            Severity = ImportNoteSeverity.Info,
            Count = vaccinations + appointments,
            Unit = "görev",
            Title = "aşı geçmişine ve randevuya dönüşecek",
            Source = TaskList,
            Parts = [new("Tamamlanmış aşı → aşı geçmişi", vaccinations), new("Gelecek görev → randevu", appointments)],
            Outcome = dueDates > 0 ? $"{N(dueDates)} aşının sonraki aşı tarihi E-vet'teki yapılacak aşı görevinden alındı." : null,
        };

    public static ImportReadWarning TasksToRecords(int records, int missed, int treatments)
        => new(TasksToRecordsCode, ImportTable.ClinicalRecords,
            $"Randevu Listesi: geçmişte kalan {records} aşı dışı görev ({records - missed} tamamlanmış, {missed} gelinmemiş) "
            + "hastanın eski program kayıtlarına eklenecek; kayıt türü görev tipidir."
            + (treatments > 0 ? $" Aşı paketine yazılmış {treatments} tedavi (ör. uyuz tedavisi) aşı değil, tedavi olarak bunlara dahildir." : ""))
        {
            Severity = ImportNoteSeverity.Info,
            Count = records,
            Unit = "görev",
            Title = "eski program kayıtlarına eklenecek",
            Source = TaskList,
            Parts = [new("Tamamlanmış", records - missed), new("Gelinmemiş", missed)],
            Outcome = "Geçmişte kalan aşı dışı görevler; kayıt türü görev tipidir."
                      + (treatments > 0 ? $" Aşı paketine yazılmış {N(treatments)} tedavi (ör. uyuz tedavisi) aşı değil, tedavi olarak bunlara dahildir." : ""),
        };

    public static ImportReadWarning EarlyCompleted(int tasks)
        => new(EarlyCompletedCode, ImportTable.ClinicalRecords,
            $"Randevu Listesi: {tasks} görev planlanan tarihinden önce tamamlanmış (E-vet'te tarihi gelecekte kalmış); aktarma gününün "
            + "tarihiyle yazılacak, planlanan tarih notta kalacak.")
        {
            Severity = ImportNoteSeverity.Attention,
            Count = tasks,
            Unit = "görev",
            Title = "aktarma gününün tarihiyle yazılacak",
            Source = TaskList,
            Outcome = "Planlanan tarihinden önce tamamlanmış, E-vet'te tarihi gelecekte kalmış. Planlanan tarih notta kalır.",
        };

    /// <param name="reasons">Neden → görev sayısı, çoktan aza (neden metni küçük harfle başlar).</param>
    public static ImportReadWarning TasksSkipped(IReadOnlyList<(string Reason, int Count)> reasons)
    {
        var total = reasons.Sum(r => r.Count);
        return new(TasksSkippedCode, ImportTable.Appointments,
            $"Randevu Listesi: {total} görev aktarılmayacak ({string.Join(", ", reasons.Select(r => $"{r.Reason}: {r.Count}"))}).")
        {
            Severity = ImportNoteSeverity.Skipped,
            Count = total,
            Unit = "görev",
            Title = "aktarılmayacak",
            Source = TaskList,
            Parts = reasons.Select(r => new ImportNotePart(Capitalize(r.Reason), r.Count)).ToList(),
        };
    }

    /// <summary>Eşleşmeyen rapor satırı varsa dikkat; hepsi eşleştiyse bilgi.</summary>
    public static ImportReadWarning VaccineReport(int matched, int unmatched)
        => new(VaccineReportCode, ImportTable.Vaccinations,
            $"Aşı Bilgisi raporu: {matched} aşıya ürün adı ve seri numarası eklendi; {unmatched} rapor satırı yedekteki "
            + "tamamlanmış aşılarla eşleşmedi (o gün tamamlanmış aşı yok ya da ürün adından ayırt edilemedi).")
        {
            Severity = unmatched > 0 ? ImportNoteSeverity.Attention : ImportNoteSeverity.Info,
            Count = unmatched > 0 ? unmatched : matched,
            Unit = unmatched > 0 ? "rapor satırı" : "aşı",
            Title = unmatched > 0 ? "hiçbir aşıyla eşleşmedi" : "ürün adı ve seri numarası aldı",
            Source = VaccineReportSource,
            Parts = unmatched > 0
                ? [new("Eşleşmedi: ürün ve seri numarası eklenmeyecek", unmatched, ImportNoteSeverity.Attention), new("Aşıya ürün adı ve seri numarası eklendi", matched)]
                : null,
            Outcome = unmatched > 0 ? "O gün tamamlanmış aşı yok ya da ürün adından ayırt edilemedi." : null,
        };

    public static ImportReadWarning PassiveProducts(int products)
        => new(PassiveProductsCode, ImportTable.Products, $"E-vet ürün listesi: {products} pasif ürün ([-]) aktarılmayacak.")
        {
            Severity = ImportNoteSeverity.Skipped,
            Count = products,
            Unit = "pasif ürün",
            Title = "aktarılmayacak",
            Source = "E-vet ürün listesi",
            Outcome = "E-vet ürün listesinde [-] ile işaretli ürünler.",
        };

    private static string N(int value) => value.ToString("N0", Tr);

    private static string Capitalize(string text)
        => text.Length == 0 ? text : char.ToUpper(text[0], Tr) + text[1..];
}
