namespace Backend.Veteriner.Application.Clinics.KvkkFormTemplate;

/// <summary>
/// KVKK açık rıza formu şablonu varsayılanı ve yer tutucuları (ADR-021). Varsayılan metin HUKUKİ ONAYDAN GEÇMEMİŞ bir taslaktır
/// (gerekçesi ve onay listesi: ürün deposunda roadmap/HUKUKI-ONAY-LISTESI.md); klinik, hukuk danışmanının onayladığı metinle değiştirir.
/// Yer tutucular istemcide (yazdırma görünümü) doldurulur. Saklama süresi ve başvuru kanalı gibi klinik/hukuk kararı gerektiren
/// yerler köşeli parantezle bırakılmıştır.
/// </summary>
public static class ClinicKvkkFormTemplateDefaults
{
    public const string ClinicNamePlaceholder = "{{clinicName}}";
    public const string ClientNamePlaceholder = "{{clientName}}";
    public const string DatePlaceholder = "{{date}}";

    public static readonly IReadOnlyList<string> Placeholders =
        [ClinicNamePlaceholder, ClientNamePlaceholder, DatePlaceholder];

    public const string Text =
        "Bu metin hukuk danışmanınızca onaylanmış açık rıza metniyle değiştirilmelidir.\n\n"
        + "{{clinicName}} KİŞİSEL VERİLERİN KORUNMASI KANUNU KAPSAMINDA AÇIK RIZA BEYANI\n\n"
        + "6698 sayılı Kişisel Verilerin Korunması Kanunu (\"Kanun\") kapsamında veri sorumlusu {{clinicName}} (\"Klinik\") tarafından "
        + "tarafıma, kişisel verilerimin hangi amaçlarla işleneceği, kimlere ve hangi amaçla aktarılabileceği, toplanma yöntemi ve hukuki "
        + "sebebi ile Kanun'un 11. maddesinde sayılan haklarım konusunda aydınlatma yapıldığını ve aydınlatma metnini okuyup anladığımı beyan ederim.\n\n"
        + "1. İşlenen kişisel veriler\n"
        + "Kimlik bilgilerim (ad, soyad), iletişim bilgilerim (telefon, e-posta, adres), hayvanıma ilişkin kayıtlarım (hasta kaydı, randevu, "
        + "muayene, tedavi, aşı ve uygulama bilgileri) ile ücret, ödeme ve bakiye bilgilerim Klinik tarafından işlenebilir.\n\n"
        + "2. İşleme amaçları\n"
        + "Bu veriler; veteriner hizmetinin sunulması ve takibi, randevu ve geliş süreçlerinin yürütülmesi, aşı ve randevu hatırlatmalarının "
        + "yapılması, ücretlendirme, tahsilat ve faturalandırma, kayıt ve defter tutma gibi yasal yükümlülüklerin yerine getirilmesi ve "
        + "tarafımla iletişim kurulması amaçlarıyla işlenebilir.\n\n"
        + "3. Verilerin aktarılması\n"
        + "Verilerim; mevzuatın zorunlu kıldığı hallerde yetkili kamu kurum ve kuruluşlarına, hizmetin sunulması için gerekli olduğu ölçüde "
        + "Kliniğin çalışanlarına ve hizmet aldığı iş ortaklarına (örneğin laboratuvar, yazılım ve barındırma hizmeti sağlayıcıları) "
        + "ilgili mevzuata uygun olarak aktarılabilir.\n\n"
        + "4. Hatırlatma ve iletişim\n"
        + "Aşı, randevu ve benzeri hatırlatmaların telefon, SMS veya e-posta yoluyla tarafıma iletilmesine izin veriyorum. Bu izni dilediğim "
        + "zaman Kliniğe başvurarak geri alabileceğimi biliyorum.\n\n"
        + "5. Saklama süresi\n"
        + "Verilerim, işlendikleri amacın gerektirdiği ve ilgili mevzuatta öngörülen süre boyunca [saklama süresi: hukuk danışmanı belirler] "
        + "saklanır, sürenin sonunda silinir, yok edilir veya anonim hale getirilir.\n\n"
        + "6. Haklarım ve başvuru\n"
        + "Kanun'un 11. maddesi kapsamında; verilerimin işlenip işlenmediğini öğrenme, işlenmişse bilgi talep etme, işlenme amacını ve amacına uygun "
        + "kullanılıp kullanılmadığını öğrenme, yurt içinde veya yurt dışında aktarıldığı üçüncü kişileri bilme, eksik veya yanlış işlenmişse "
        + "düzeltilmesini isteme, Kanun'da öngörülen şartlar çerçevesinde silinmesini veya yok edilmesini isteme, bu işlemlerin aktarıldığı "
        + "üçüncü kişilere bildirilmesini isteme, işlenen verilerin otomatik sistemlerle analiz edilmesi sonucu aleyhime bir sonuç ortaya "
        + "çıkmasına itiraz etme ve kanuna aykırı işlenmesi sebebiyle zarara uğramam halinde zararın giderilmesini talep etme haklarına sahip "
        + "olduğumu biliyorum. Bu haklarımı kullanmak için [başvuru kanalı: yazılı başvuru adresi veya e-posta, klinik doldurur] üzerinden Kliniğe "
        + "başvurabileceğimi kabul ederim.\n\n"
        + "7. Güncelliği\n"
        + "Paylaştığım kişisel verilerin doğru ve güncel olduğunu, bilgilerimde değişiklik olması halinde bunu Kliniğe bildireceğimi kabul ederim.\n\n"
        + "Yukarıdaki metni okudum, anladım. Kişisel verilerimin belirtilen amaçlarla ve koşullarda işlenmesine ve aktarılmasına açık rızam "
        + "olduğunu beyan ederim.";
}
