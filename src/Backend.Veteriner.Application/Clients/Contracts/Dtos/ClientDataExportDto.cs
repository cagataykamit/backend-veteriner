using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>
/// GET /clients/{id}/data-export (ADR-032 madde 2): müşterinin KVKK bilgi talebi için salt okunur veri kopyası.
/// İç alanlar (RowVersion, TenantId, kullanıcı kimlikleri, denetim alanları) ve serbest metin iç notlar taşınmaz.
/// Her bölüm en fazla <see cref="MaxRowsPerSection"/> kayıt taşır; fazlası kesilir ve <c>isTruncated</c> true olur.
/// </summary>
public sealed record ClientDataExportDto(
    DateTime GeneratedAtUtc,
    Guid? ClinicId,
    int MaxRowsPerSection,
    ClientDataExportClientDto Client,
    ClientDataExportKvkkConsentDto KvkkConsent,
    ClientDataExportSectionDto<ClientDataExportPetDto> Pets,
    ClientDataExportSectionDto<ClientDataExportAppointmentDto> Appointments,
    ClientDataExportSectionDto<ClientDataExportExaminationDto> Examinations,
    ClientDataExportSectionDto<ClientDataExportVaccinationDto> Vaccinations,
    ClientDataExportSectionDto<ClientDataExportTreatmentDto> Treatments,
    ClientDataExportSectionDto<ClientDataExportPrescriptionDto> Prescriptions,
    ClientDataExportSectionDto<ClientDataExportChargeDto> Charges,
    ClientDataExportSectionDto<ClientDataExportPaymentDto> Payments,
    ClientDataExportBalanceDto Balance);

public sealed record ClientDataExportSectionDto<T>(IReadOnlyList<T> Items, bool IsTruncated);

public sealed record ClientDataExportClientDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    string? Address,
    DateTime CreatedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ContactChannel PreferredContactChannel,
    bool RemindersOptedOut,
    ClientInvoicingSummaryDto Invoicing);

public sealed record ClientDataExportKvkkConsentDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] KvkkConsentStatus Status,
    DateOnly? RecordedOn,
    string? RecordedByName);

public sealed record ClientDataExportPetDto(
    Guid Id,
    string Name,
    string SpeciesName,
    string? Breed,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetGender? Gender,
    DateOnly? BirthDate,
    bool IsNeutered,
    string? MicrochipNumber,
    string? PassportOrTagNumber,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status,
    DateOnly? DeceasedOn,
    string? HbsAnimalId);

public sealed record ClientDataExportAppointmentDto(
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    Guid PetId,
    string PetName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentType AppointmentType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentStatus Status);

/// <param name="Complaint">Başvuru nedeni / şikâyet.</param>
/// <param name="Assessment">Değerlendirme / ön tanı özeti.</param>
public sealed record ClientDataExportExaminationDto(
    DateTime ExaminedAtUtc,
    Guid PetId,
    string PetName,
    string Complaint,
    string? Assessment,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ExaminationStatus Status);

public sealed record ClientDataExportVaccinationDto(
    string VaccineName,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    Guid PetId,
    string PetName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VaccinationStatus Status);

public sealed record ClientDataExportTreatmentDto(
    DateTime TreatmentDateUtc,
    string Title,
    Guid PetId,
    string PetName);

/// <param name="ItemNames">Reçete kalemlerinin ürün/ilaç adları (sırasıyla).</param>
public sealed record ClientDataExportPrescriptionDto(
    DateTime PrescribedAtUtc,
    string Title,
    IReadOnlyList<string> ItemNames,
    Guid PetId,
    string PetName);

/// <param name="AllocatedAmount">Ödemeyle karşılanan (ödenen) kısım.</param>
/// <param name="OutstandingAmount">Kalan.</param>
/// <param name="IsVoided">İptal edilmiş ücret; tutarı bakiyeye girmez.</param>
public sealed record ClientDataExportChargeDto(
    DateTime CreatedAtUtc,
    string Description,
    Guid? PetId,
    string? PetName,
    decimal Amount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    string Currency,
    bool IsVoided,
    DateTime? VoidedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ChargeLineKind LineKind,
    decimal? VatRate);

/// <param name="IsVoided">İptal edilmiş ödeme; bakiyeye girmez.</param>
public sealed record ClientDataExportPaymentDto(
    DateTime PaidAtUtc,
    decimal Amount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    string Currency,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentMethod Method,
    Guid? PetId,
    string? PetName,
    bool IsVoided,
    DateTime? VoidedAtUtc);

/// <summary>GET /clients/{id}/balance toplamlarıyla aynı kural (yalnız TRY, iptal edilenler hariç).</summary>
public sealed record ClientDataExportBalanceDto(
    string Currency,
    decimal ChargedAmount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    decimal UnallocatedPaymentAmount);
