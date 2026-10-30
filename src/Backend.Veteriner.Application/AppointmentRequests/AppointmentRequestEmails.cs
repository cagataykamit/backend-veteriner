using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.AppointmentRequests;

/// <summary>Şablonun doldurulacak değerleri.</summary>
public sealed record AppointmentRequestEmailValues(
    string OwnerName,
    string ClinicName,
    string? ClinicPhone,
    DateTime StartUtc,
    string Reason,
    string? PetName,
    string? RejectionReason);

/// <summary>
/// Talep e-postaları (ADR-042 K5; APPT-012/013): Vetinity varsayılan şablonları, yer tutucular ve doldurma — tek yer.
/// Metin minimum veri taşır (klinik, saat, neden, hayvan adı); sağlık bilgisi yoktur. Saat İstanbul duvar saatidir ve
/// "talep" ile "kesin randevu" ayrımı metinde açıkça söylenir.
/// </summary>
public static class AppointmentRequestEmails
{
    public const string OwnerNamePlaceholder = "{{ownerName}}";
    public const string ClinicNamePlaceholder = "{{clinicName}}";
    public const string ClinicPhonePlaceholder = "{{clinicPhone}}";
    public const string DatePlaceholder = "{{date}}";
    public const string TimePlaceholder = "{{time}}";
    public const string ReasonPlaceholder = "{{reason}}";
    public const string PetNamePlaceholder = "{{petName}}";
    public const string RejectionReasonPlaceholder = "{{rejectionReason}}";

    public static readonly IReadOnlyList<string> Placeholders =
    [
        OwnerNamePlaceholder, ClinicNamePlaceholder, ClinicPhonePlaceholder, DatePlaceholder, TimePlaceholder,
        ReasonPlaceholder, PetNamePlaceholder, RejectionReasonPlaceholder,
    ];

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static OwnerPortalEmails.Message Default(AppointmentRequestEmailKind kind) => kind switch
    {
        AppointmentRequestEmailKind.Received => new(
            "{{clinicName}} randevu talebinizi aldı",
            Lines(
                "Merhaba {{ownerName}},",
                "",
                "{{clinicName}} için {{date}} {{time}} randevu talebiniz alındı ({{reason}}, {{petName}}).",
                "",
                "Bu henüz kesin bir randevu değildir. Klinik talebinizi inceleyip onayladığında ya da farklı bir saat önerdiğinde size e-posta ile bilgi verilecek.",
                "",
                "Sorularınız için klinik: {{clinicPhone}}")),
        AppointmentRequestEmailKind.Approved => new(
            "{{clinicName}} randevunuzu onayladı",
            Lines(
                "Merhaba {{ownerName}},",
                "",
                "{{clinicName}} randevunuzu onayladı: {{date}} {{time}} ({{reason}}, {{petName}}).",
                "",
                "Gelemeyecekseniz lütfen kliniği arayın: {{clinicPhone}}")),
        AppointmentRequestEmailKind.Rescheduled => new(
            "{{clinicName}} randevunuzu farklı bir saate planladı",
            Lines(
                "Merhaba {{ownerName}},",
                "",
                "{{clinicName}} talebinizi farklı bir saatle onayladı. Randevunuz: {{date}} {{time}} ({{reason}}, {{petName}}).",
                "",
                "Bu saat size uymuyorsa kliniği arayabilirsiniz: {{clinicPhone}}")),
        AppointmentRequestEmailKind.Rejected => new(
            "{{clinicName}} randevu talebiniz hakkında",
            Lines(
                "Merhaba {{ownerName}},",
                "",
                "{{clinicName}} {{date}} {{time}} için yaptığınız randevu talebini karşılayamadı.",
                "",
                "Klinik açıklaması: {{rejectionReason}}",
                "",
                "Yeni bir talep oluşturabilir ya da kliniği arayabilirsiniz: {{clinicPhone}}")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Yer tutucuları doldurur; boş değerler "-" olur. Metin düz yazıdır (HTML değil).</summary>
    public static OwnerPortalEmails.Message Render(string subject, string body, AppointmentRequestEmailValues values)
    {
        var local = OperationDayBounds.ToLocal(values.StartUtc);
        var map = new Dictionary<string, string>
        {
            [OwnerNamePlaceholder] = values.OwnerName,
            [ClinicNamePlaceholder] = values.ClinicName,
            [ClinicPhonePlaceholder] = OrDash(values.ClinicPhone),
            [DatePlaceholder] = local.ToString("d MMMM yyyy dddd", Turkish),
            [TimePlaceholder] = local.ToString("HH:mm", Turkish),
            [ReasonPlaceholder] = values.Reason,
            [PetNamePlaceholder] = OrDash(values.PetName),
            [RejectionReasonPlaceholder] = OrDash(values.RejectionReason),
        };

        string Fill(string text) => map.Aggregate(text, (acc, kv) => acc.Replace(kv.Key, kv.Value, StringComparison.Ordinal));
        return new OwnerPortalEmails.Message(Fill(subject), Fill(body));
    }

    /// <summary>Bağlantıdan gelen kişinin e-posta doğrulama kodu.</summary>
    public static OwnerPortalEmails.Message VerificationCode(string clinicName, string code) => new(
        $"{clinicName} randevu talebi doğrulama kodunuz",
        new StringBuilder()
            .AppendLine("Merhaba,")
            .AppendLine()
            .AppendLine($"{clinicName} için randevu talebinizi tamamlamak üzere doğrulama kodunuz:")
            .AppendLine()
            .AppendLine(code)
            .AppendLine()
            .AppendLine($"Kod {(int)OwnerPortalCode.Lifetime.TotalMinutes} dakika geçerlidir ve yalnız bir kez kullanılabilir. Kodu kimseyle paylaşmayın.")
            .AppendLine()
            .AppendLine("Bu işlemi siz başlatmadıysanız e-postayı dikkate almayabilirsiniz.")
            .AppendLine()
            .AppendLine("Vetinity")
            .ToString());

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n\nVetinity";

    private static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
