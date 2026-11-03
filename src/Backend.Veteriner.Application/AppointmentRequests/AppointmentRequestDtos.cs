using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.AppointmentRequests;

namespace Backend.Veteriner.Application.AppointmentRequests;

/// <summary>
/// Online akışta seçilebilen neden (aktif + online'da görünür). <see cref="Id"/> boşsa sistemin "Diğer / emin değilim"
/// seçeneği: not zorunlu (<see cref="RequiresNote"/>; ADR-042 ek md. 1-2).
/// </summary>
public sealed record OnlineBookingReasonDto(Guid? Id, string Name, int DurationMinutes, bool RequiresNote);

/// <summary>Bağlantı/portal talep sayfası: klinik iletişim bilgisi, talep sınırları ve nedenler.</summary>
public sealed record OnlineBookingPageDto(
    string ClinicName,
    string ClinicCity,
    string? ClinicPhone,
    string? ClinicAddress,
    int MinLeadMinutes,
    int MaxAdvanceDays,
    int MaxPendingRequests,
    IReadOnlyList<OnlineBookingReasonDto> Reasons);

/// <summary>Talep oluşturma yanıtı; talep kesin randevu değildir.</summary>
/// <remarks>Portal ve bağlantı yüzeyinde <c>status</c> metindir (portal sözleşmesi §1, §13.4).</remarks>
public sealed record AppointmentRequestCreatedDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentRequestStatus Status,
    DateTime RequestedStartUtc);

/// <summary>Portal "taleplerim" satırı.</summary>
public sealed record PortalAppointmentRequestDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentRequestStatus Status,
    string ReasonName,
    DateTime RequestedStartUtc,
    int DurationMinutes,
    Guid? PetId,
    string? PetName,
    DateTime? ApprovedStartUtc,
    bool WasRescheduled,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    bool CanCancel);

/// <summary>
/// Personel kuyruğu satırı; <see cref="PetName"/> kayıtlı hayvanın adı ya da sahibin yazdığı ad. <see cref="ReasonId"/> boşsa
/// "Diğer / emin değilim" talebi.
/// </summary>
public sealed record AppointmentRequestListItemDto(
    Guid Id,
    Guid ClinicId,
    AppointmentRequestStatus Status,
    AppointmentRequestSource Source,
    Guid? ReasonId,
    string ReasonName,
    DateTime RequestedStartUtc,
    int DurationMinutes,
    string ContactName,
    string ContactEmail,
    string? ContactPhone,
    Guid? ClientId,
    string? ClientName,
    Guid? PetId,
    string? PetName,
    DateTime CreatedAtUtc,
    Guid? AppointmentId);

public sealed record AppointmentRequestHistoryDto(
    AppointmentRequestStatus? FromStatus,
    AppointmentRequestStatus ToStatus,
    AppointmentRequestActorKind ActorKind,
    Guid? ActorUserId,
    string? ActorName,
    string? Note,
    DateTime AtUtc);

public sealed record ClientMatchPetDto(Guid Id, string Name);

/// <summary>Onayda eşleştirme önerisi: aynı e-posta ya da telefonlu müşteri.</summary>
public sealed record ClientMatchDto(Guid ClientId, string FullName, string? Phone, string? Email, IReadOnlyList<ClientMatchPetDto> Pets);

public sealed record AppointmentRequestDetailDto(
    AppointmentRequestListItemDto Summary,
    string? PetSpecies,
    string? PetBreed,
    string? OwnerNote,
    DateTime? ApprovedStartUtc,
    Guid? ApprovedVeterinarianUserId,
    bool WasRescheduled,
    string? RejectionReason,
    DateTime? ClosedAtUtc,
    IReadOnlyList<AppointmentRequestHistoryDto> History,
    IReadOnlyList<ClientMatchDto> ClientMatches,
    bool RequestedSlotFree,
    IReadOnlyList<Guid> FreeVeterinarianUserIds);

/// <summary>Onay/red ekranında gönderilecek e-postanın düzenlenmiş hali; boşsa şablon kullanılır.</summary>
public sealed record AppointmentRequestEmailOverride(string Subject, string Body);

public sealed record AppointmentRequestEmailPreviewDto(AppointmentRequestEmailKind Kind, string Subject, string Body);
