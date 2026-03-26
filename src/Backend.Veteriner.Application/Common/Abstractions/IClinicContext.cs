namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// İstek kapsamında çözümlenmiş klinik. Öncelik: JWT <c>clinic_id</c>, yoksa header <c>X-Clinic-Id</c> / sorgu <c>clinicId</c> (geçiş).
/// </summary>
public interface IClinicContext
{
    /// <summary>Çözümlenmiş klinik; yoksa <c>null</c> (handler doğrulamasına düşer).</summary>
    Guid? ClinicId { get; }
}

