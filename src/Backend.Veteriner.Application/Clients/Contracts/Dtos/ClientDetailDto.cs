using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <param name="PreferredContactChannel">Tercih edilen kanal (yalnız bilgi); <c>None</c> = tercih yok.</param>
/// <param name="RemindersOptedOut">true ise otomatik aşı/randevu hatırlatması gönderilmez.</param>
/// <param name="KvkkConsentStatus">KVKK açık rıza formu sonucu (ADR-021); <c>None</c> = form alınmadı.</param>
/// <param name="KvkkConsentRecordedOn">İmzalı formun tarihi (yyyy-MM-dd); yalnız Kabul/Ret iken dolu.</param>
/// <param name="KvkkConsentRecordedByUserId">Rıza durumunu işaretleyen kullanıcı.</param>
/// <param name="KvkkConsentRecordedByName">İşaretleyen kullanıcının görünen adı (bulunamazsa null).</param>
/// <param name="KvkkConsentTemplateVersion">İşaretleme anındaki açık rıza metni sürümü (ADR-032; 0 = varsayılan metin, null = bilinmiyor).</param>
/// <param name="KvkkConsentTemplateHash">İşaretleme anındaki açık rıza metninin SHA-256 özeti (64 karakter hex) veya null.</param>
/// <param name="RowVersion">Base64 kayıt sürümü; <c>PUT /clients/{id}/kvkk-consent</c> için zorunlu.</param>
/// <param name="Invoicing">Fatura kimliği, TCKN/VKN maskeli (ADR-036).</param>
public sealed record ClientDetailDto(
    Guid Id,
    Guid TenantId,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string FullName,
    string? Email,
    string? Phone,
    string? Address,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ContactChannel PreferredContactChannel,
    bool RemindersOptedOut,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] KvkkConsentStatus KvkkConsentStatus,
    DateOnly? KvkkConsentRecordedOn,
    Guid? KvkkConsentRecordedByUserId,
    string? KvkkConsentRecordedByName,
    int? KvkkConsentTemplateVersion,
    string? KvkkConsentTemplateHash,
    string RowVersion,
    ClientInvoicingSummaryDto Invoicing);
