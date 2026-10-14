namespace Backend.Veteriner.Application.Clinics.KvkkFormTemplate;

/// <summary>
/// KVKK açık rıza formu şablonu varsayılanı ve yer tutucuları (ADR-021). Varsayılan metin hukuki metin DEĞİLDİR;
/// klinik, hukuk danışmanının onayladığı metinle değiştirir. Yer tutucular istemcide (yazdırma görünümü) doldurulur.
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
        + "Kullanılabilecek yer tutucular (yazdırırken doldurulur):\n"
        + "{{clinicName}} — klinik adı\n"
        + "{{clientName}} — müşteri adı soyadı\n"
        + "{{date}} — form tarihi";
}
