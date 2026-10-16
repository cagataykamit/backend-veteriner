using Ardalis.Specification;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.OwnerPortal.Specs;

// Dilim B okuma sorguları (docs/PORTAL_API_CONTRACT.md §6). Her sorgu bağdan çözülen kiracı + müşteri (gerekirse klinik)
// filtresiyle çalışır; projeksiyonlar yalnız portal sözleşmesindeki alanları okur.

public sealed class PortalPetsByClientSpec : Specification<Pet, PortalPetDto>
{
    public PortalPetsByClientSpec(Guid tenantId, Guid clientId)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId)
            .OrderBy(p => p.Status)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id);
        Query.Select(p => new PortalPetDto(
            p.Id,
            p.Name,
            p.Species != null ? p.Species.Name : null,
            p.BreedRef != null ? p.BreedRef.Name : p.Breed,
            p.Gender,
            p.BirthDate,
            p.MicrochipNumber,
            p.Weight,
            p.Status));
    }
}

public sealed class PortalPetDetailSpec : Specification<Pet, PortalPetDetailDto>
{
    public PortalPetDetailSpec(Guid tenantId, Guid clientId, Guid petId)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId && p.Id == petId);
        Query.Select(p => new PortalPetDetailDto(
            p.Id,
            p.Name,
            p.Species != null ? p.Species.Name : null,
            p.BreedRef != null ? p.BreedRef.Name : p.Breed,
            p.Gender,
            p.BirthDate,
            p.MicrochipNumber,
            p.Weight,
            p.Status,
            p.IsNeutered,
            p.ColorRef != null ? p.ColorRef.Name : null));
    }
}

/// <summary>Bağın müşterisine ait hayvan (sahiplik + bakım durumu için).</summary>
public sealed class PortalPetOfClientSpec : Specification<Pet>
{
    public PortalPetOfClientSpec(Guid tenantId, Guid clientId, Guid petId)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId && p.Id == petId);
    }
}

public sealed record PortalPetNameRow(Guid Id, string Name);

public sealed class PortalPetNamesByClientSpec : Specification<Pet, PortalPetNameRow>
{
    public PortalPetNamesByClientSpec(Guid tenantId, Guid clientId)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId);
        Query.Select(p => new PortalPetNameRow(p.Id, p.Name));
    }
}

public sealed record PortalVaccinationRow(
    Guid Id,
    string VaccineName,
    VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    DateTime CreatedAtUtc);

public sealed class PortalVaccinationsForPetSpec : Specification<Vaccination, PortalVaccinationRow>
{
    public PortalVaccinationsForPetSpec(Guid tenantId, Guid petId)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId && v.PetId == petId);
        Query.Select(v => new PortalVaccinationRow(v.Id, v.VaccineName, v.Status, v.AppliedAtUtc, v.DueAtUtc, v.CreatedAtUtc));
    }
}

public sealed record PortalAppointmentRow(
    Guid Id,
    Guid PetId,
    Guid ClinicId,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    AppointmentType AppointmentType,
    AppointmentStatus Status);

/// <summary>
/// Müşterinin hayvanlarının randevuları, <c>[fromUtc, toUtcExclusive)</c> aralığında. Sayım için
/// <paramref name="page"/> verilmez; sayfa verilirse sıralama ve sayfalama uygulanır.
/// </summary>
public sealed class PortalAppointmentsSpec : Specification<Appointment, PortalAppointmentRow>
{
    public PortalAppointmentsSpec(
        Guid tenantId,
        IReadOnlyCollection<Guid> petIds,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        bool ascending,
        int? page = null,
        int pageSize = 0)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && petIds.Contains(a.PetId));
        if (fromUtc.HasValue)
            Query.Where(a => a.ScheduledAtUtc >= fromUtc.Value);
        if (toUtcExclusive.HasValue)
            Query.Where(a => a.ScheduledAtUtc < toUtcExclusive.Value);

        if (page.HasValue)
        {
            if (ascending)
                Query.OrderBy(a => a.ScheduledAtUtc).ThenBy(a => a.Id);
            else
                Query.OrderByDescending(a => a.ScheduledAtUtc).ThenByDescending(a => a.Id);
            Query.Skip((page.Value - 1) * pageSize).Take(pageSize);
        }

        Query.Select(a => new PortalAppointmentRow(
            a.Id, a.PetId, a.ClinicId, a.ScheduledAtUtc, a.DurationMinutes, a.AppointmentType, a.Status));
    }
}

/// <summary>Bağın kliniğindeki ödemeler (iptal edilenler sorgu filtresiyle dışarıda), en yeni önce. Sayım için sayfa verilmez.</summary>
public sealed class PortalPaymentsSpec : Specification<Payment, PortalPaymentDto>
{
    public PortalPaymentsSpec(Guid tenantId, Guid clinicId, Guid clientId, int? page = null, int pageSize = 0)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.ClinicId == clinicId && p.ClientId == clientId);
        if (page.HasValue)
        {
            Query.OrderByDescending(p => p.PaidAtUtc).ThenByDescending(p => p.Id)
                .Skip((page.Value - 1) * pageSize)
                .Take(pageSize);
        }

        Query.Select(p => new PortalPaymentDto(p.PaidAtUtc, p.Amount, p.Currency, p.Method));
    }
}
