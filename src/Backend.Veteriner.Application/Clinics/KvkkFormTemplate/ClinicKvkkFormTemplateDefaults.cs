using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.KvkkFormTemplate;

/// <summary>
/// KVKK şablonlarının (açık rıza formu ve aydınlatma metni) varsayılanları ve yer tutucuları (ADR-021, ADR-024). Varsayılan metinler
/// ürün ekibinin yazdığı, HUKUKİ ONAYDAN GEÇMEMİŞ taslaklardır (kaynak: ürün deposunda legal/acik-riza-metni.md ve
/// legal/aydinlatma-metni.md; onay listesi: roadmap/HUKUKI-ONAY-LISTESI.md); klinik, Ayarlar'dan hukuk danışmanının onayladığı
/// metinle değiştirir. Yalnız klinikte o türde kayıt yokken döner; kayıtlı metni ezmez. Aydınlatma varsayılanı legal/aydinlatma-metni.md
/// v0.2 ile aynıdır (ADR-036: fatura/e-belge amacı ve TCKN/VKN).
/// Yer tutucular istemcide (yazdırma görünümü) doldurulur. Adres, başvuru kanalı gibi kliniğin doldurması gereken yerler köşeli
/// parantezle bırakılmıştır. Açık rıza formunda "Kabul ediyorum / Kabul etmiyorum" kutuları ve imza bloğu yazdırma görünümünün
/// sabit düzenidir, metne konmaz.
/// </summary>
public static class ClinicKvkkFormTemplateDefaults
{
    public const string ClinicNamePlaceholder = "{{clinicName}}";
    public const string ClientNamePlaceholder = "{{clientName}}";
    public const string DatePlaceholder = "{{date}}";

    public static readonly IReadOnlyList<string> Placeholders =
        [ClinicNamePlaceholder, ClientNamePlaceholder, DatePlaceholder];

    public static string TextFor(ClinicKvkkFormKind kind) => kind switch
    {
        ClinicKvkkFormKind.Consent => Text,
        ClinicKvkkFormKind.Disclosure => DisclosureText,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Şablon türü geçersiz."),
    };

    /// <summary>Açık rıza formu varsayılanı.</summary>
    public const string Text =
        "Bu metin hukuk danışmanınızca onaylanmış açık rıza metniyle değiştirilmelidir.\n\n"
        + "{{clinicName}} — KİŞİSEL VERİLERİN İŞLENMESİNE İLİŞKİN AÇIK RIZA BEYANI\n\n"
        + "{{clinicName}} (\"Klinik\") tarafından 6698 sayılı Kişisel Verilerin Korunması Kanunu kapsamında tarafıma verilen "
        + "Aydınlatma Metnini okudum ve anladım. Bu beyan, hizmetin sunulması için zorunlu olmayan aşağıdaki konulara ilişkindir "
        + "ve vermek zorunda olmadığım bir rızadır.\n\n"
        + "1. Hatırlatma ve bilgilendirme iletişimi\n"
        + "Aşı, randevu, kontrol ve tedavi takibi gibi hayvanıma ilişkin hatırlatma ve bilgilendirmelerin; ad, soyad ve telefon/e-posta "
        + "bilgilerim kullanılarak telefon araması, SMS, e-posta veya uygulama bildirimi yoluyla tarafıma iletilmesine izin veriyorum.\n\n"
        + "2. Kapsam dışında bırakılanlar\n"
        + "Bu rıza kampanya, reklam veya ticari nitelikte ileti göndermeyi kapsamaz. Bu tür iletiler için ayrıca ayrı bir izin alınır.\n\n"
        + "3. Rızanın serbestliği ve geri alınması\n"
        + "Rıza vermememin veya sonradan geri almamın, Kliniğin bana veteriner hizmeti sunmasını etkilemeyeceğini biliyorum. Rızamı "
        + "dilediğim zaman, [başvuru kanalı: yazılı başvuru adresi veya e-posta, klinik doldurur] üzerinden Kliniğe bildirerek geri "
        + "alabilirim. Geri alma, geri almadan önce yapılmış işlemlerin hukuka uygunluğunu etkilemez.\n\n"
        + "4. Haklarım\n"
        + "Kanun'un 11. maddesi kapsamındaki haklarımın ve bunları nasıl kullanabileceğimin Aydınlatma Metninde belirtildiğini biliyorum.";

    /// <summary>Aydınlatma metni varsayılanı (bilgilendirme; rıza beyanı değildir).</summary>
    public const string DisclosureText =
        "Bu metin hukuk danışmanınızca onaylanmış aydınlatma metniyle değiştirilmelidir.\n\n"
        + "{{clinicName}} — KİŞİSEL VERİLERİN İŞLENMESİNE İLİŞKİN AYDINLATMA METNİ\n\n"
        + "6698 sayılı Kişisel Verilerin Korunması Kanunu'nun (\"Kanun\") 10. maddesi ve Aydınlatma Yükümlülüğünün Yerine Getirilmesinde "
        + "Uyulacak Usul ve Esaslar Hakkında Tebliğ uyarınca, kişisel verileriniz hakkında sizi bilgilendiririz. Bu metin bir rıza beyanı "
        + "değildir; bilgilendirme amacıyla verilir.\n\n"
        + "1. Veri sorumlusunun kimliği\n"
        + "Veri sorumlusu {{clinicName}}'dır (Adres: [klinik adresi]; Telefon: [klinik telefonu]; E-posta: [klinik e-postası]; "
        + "Vergi/ticaret sicil bilgisi: [varsa]).\n\n"
        + "2. İşlenen kişisel veriler\n"
        + "- Kimlik: ad, soyad; yalnız fatura/e-belge için gerektiğinde T.C. kimlik numarası, kurumsal alıcıda vergi kimlik numarası, "
        + "vergi dairesi ve unvan.\n"
        + "- İletişim: telefon, e-posta, adres.\n"
        + "- Müşteri işlem: hayvanınıza ilişkin hasta kaydı, randevu, muayene, tedavi, aşı ve uygulama kayıtları (bu kayıtlar sizinle "
        + "ilişkilendirilir), reçete ve belge bilgileri.\n"
        + "- Finans: ücret, ödeme, bakiye, makbuz ve fatura bilgileri.\n"
        + "- İşlem güvenliği: sistem kullanım ve erişim kayıtları (yalnız kliniğin çalışanları için).\n\n"
        + "3. Kişisel verilerin işlenme amaçları\n"
        + "Veteriner hizmetinin sunulması ve takibi; randevu ve geliş süreçlerinin yürütülmesi; aşı, tedavi ve kontrol takibi; "
        + "ücretlendirme, tahsilat ve faturalandırma; fatura/e-belge (e-Arşiv fatura, e-Fatura, e-Serbest Meslek Makbuzu) düzenlenmesi "
        + "ve resmî bildirim yükümlülüklerinin yerine getirilmesi (kimlik/vergi numarası yalnız gerektiğinde); mevzuattan doğan kayıt, "
        + "defter ve raporlama yükümlülüklerinin yerine getirilmesi; "
        + "sizinle hizmete ilişkin iletişim kurulması; hak taleplerinin ve uyuşmazlıkların yönetilmesi; bilgi güvenliğinin sağlanması.\n\n"
        + "4. Hukuki sebepler\n"
        + "Kanun'un 5/2 maddesindeki şu sebeplere dayanılır: (c) hizmet sözleşmesinin kurulması ve ifası için gerekli olması; "
        + "(ç) veri sorumlusunun hukuki yükümlülüğünü yerine getirmesi için zorunlu olması; (e) bir hakkın tesisi, kullanılması veya "
        + "korunması için zorunlu olması; (f) ilgili kişinin temel hak ve özgürlüklerine zarar vermemek kaydıyla veri sorumlusunun meşru "
        + "menfaatleri için zorunlu olması. Aşı ve randevu hatırlatmalarını telefon, SMS veya e-posta ile iletmemiz ayrıca açık rızanıza "
        + "bağlıdır; rıza vermemeniz hizmeti almanıza engel olmaz.\n\n"
        + "5. Kişisel verilerin toplanma yöntemi\n"
        + "Verileriniz sizden sözlü, yazılı veya elektronik ortamda (kayıt formu, telefon, e-posta, yüz yüze görüşme) ve hizmetin "
        + "yürütülmesi sırasında oluşan kayıtlardan elde edilir.\n\n"
        + "6. Kişisel verilerin aktarılması\n"
        + "Verileriniz, hizmetin gereği ve mevzuatın izin verdiği ölçüde şu alıcı gruplarına aktarılabilir: mevzuatın zorunlu kıldığı "
        + "hallerde yetkili kamu kurum ve kuruluşları (örneğin Tarım ve Orman Bakanlığı ve bağlı birimleri, vergi ve adli makamlar); "
        + "kliniğin yetkili çalışanları; hizmet aldığımız yazılım, barındırma, ödeme, e-belge (özel entegratör) ve bildirim (SMS/e-posta) "
        + "sağlayıcıları (veri işleyen "
        + "olarak, yalnız bizim talimatımızla); laboratuvarlar ve sevk edilen sağlık kuruluşları; hukuk, mali müşavirlik ve denetim "
        + "danışmanlarımız. [Yurt dışına aktarım: yok / varsa Kanun'un 9. maddesine uygun mekanizma — klinik doldurur.]\n\n"
        + "7. Saklama süresi\n"
        + "Verileriniz, işleme amacının gerektirdiği ve ilgili mevzuatta öngörülen süre boyunca saklanır; süre sonunda silinir, yok "
        + "edilir veya anonim hale getirilir ([saklama süreleri: klinik saklama ve imha politikasına bakınız]).\n\n"
        + "8. Haklarınız (Kanun m.11)\n"
        + "Kişisel verilerinizin işlenip işlenmediğini öğrenme; işlenmişse buna ilişkin bilgi talep etme; işlenme amacını ve amacına "
        + "uygun kullanılıp kullanılmadığını öğrenme; yurt içinde veya yurt dışında aktarıldığı üçüncü kişileri bilme; eksik veya yanlış "
        + "işlenmişse düzeltilmesini isteme; Kanun'da öngörülen şartlar çerçevesinde silinmesini veya yok edilmesini isteme ve bu "
        + "işlemlerin aktarıldığı üçüncü kişilere bildirilmesini isteme; işlenen verilerin münhasıran otomatik sistemler ile analiz "
        + "edilmesi suretiyle aleyhinize bir sonuç ortaya çıkmasına itiraz etme; kanuna aykırı işleme sebebiyle zarara uğramanız halinde "
        + "zararın giderilmesini talep etme haklarına sahipsiniz.\n\n"
        + "9. Başvuru yolu\n"
        + "Haklarınızı kullanmak için talebinizi yazılı olarak [başvuru adresi] adresine elden/noter aracılığıyla ya da [başvuru e-postası] "
        + "adresine iletebilirsiniz. Başvurunuz, talebin niteliğine göre en geç otuz gün içinde ücretsiz sonuçlandırılır; işlemin ayrıca "
        + "maliyet gerektirmesi halinde Kişisel Verileri Koruma Kurulu'nca belirlenen tarife üzerinden ücret alınabilir. Başvurunuza "
        + "verilen cevabı yetersiz bulmanız veya süresinde cevap verilmemesi halinde Kanun'un 14. maddesi uyarınca Kişisel Verileri "
        + "Koruma Kurulu'na şikâyet hakkınız saklıdır.\n\n"
        + "Düzenleme tarihi: {{date}}\n"
        + "Bilgilendirilen: {{clientName}}";
}
