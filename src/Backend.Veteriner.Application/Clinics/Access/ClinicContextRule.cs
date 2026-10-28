using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Clinics.Access;

/// <summary>
/// İstekteki (ya da kaydın) clinicId değeri aktif clinic bağlamıyla (JWT <c>clinic_id</c> / <c>X-Clinic-Id</c>) çelişemez; tek kural.
/// Hata kodları sözleşmede modül önekiyle (<c>{Modül}.ClinicContextMismatch</c>) yerleşik olduğu için önek korunur.
/// </summary>
internal static class ClinicContextRule
{
    public const string MismatchMessage = "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.";

    /// <summary>İki değer de varsa ve farklıysa <c>true</c>; biri yoksa çelişki yoktur.</summary>
    public static bool Conflicts(Guid? requestedClinicId, Guid? contextClinicId)
        => requestedClinicId.HasValue && contextClinicId.HasValue && requestedClinicId.Value != contextClinicId.Value;

    public static Error Mismatch(string module, string message = MismatchMessage)
        => new($"{module}.ClinicContextMismatch", message);
}
