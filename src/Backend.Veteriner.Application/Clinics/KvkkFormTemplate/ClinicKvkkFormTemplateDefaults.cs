namespace Backend.Veteriner.Application.Clinics.KvkkFormTemplate;

/// <summary>
/// KVKK açık rıza formu şablonu varsayılanı ve yer tutucuları (ADR-021, ADR-024). Varsayılan metin ürün ekibinin yazdığı,
/// HUKUKİ ONAYDAN GEÇMEMİŞ bir taslaktır (kaynak: ürün deposunda legal/acik-riza-metni.md; onay listesi: roadmap/HUKUKI-ONAY-LISTESI.md);
/// klinik, Ayarlar'dan hukuk danışmanının onayladığı metinle değiştirir. Yalnız klinikte kayıt yokken döner; kayıtlı metni ezmez.
/// Yer tutucular istemcide (yazdırma görünümü) doldurulur. Başvuru kanalı gibi kliniğin doldurması gereken yerler köşeli parantezle
/// bırakılmıştır. "Kabul ediyorum / Kabul etmiyorum" kutuları ve imza bloğu yazdırma görünümünün sabit düzenidir, metne konmaz.
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
}
