using Ardalis.Specification;
using Backend.Veteriner.Application.ClientCredits;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Clients.Deletion;

/// <summary>
/// Silmeyi engelleyen açık işleri okur (ADR-054 K5); kararı <see cref="Client.Delete"/> ve
/// <see cref="Domain.Pets.Pet.Delete"/> verir. Hesap kiracı genelinde değerlendirilir (müşteri/hasta kiracı kaydıdır);
/// iptal edilen ücret/ödeme sorgu filtresiyle zaten dışarıdadır.
/// </summary>
public sealed class RecordDeletionBlockerReader
{
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<ClientCredit> _credits;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Appointment> _appointments;

    public RecordDeletionBlockerReader(
        IReadRepository<Charge> charges,
        IReadRepository<Payment> payments,
        IReadRepository<ClientCredit> credits,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Appointment> appointments)
    {
        _charges = charges;
        _payments = payments;
        _credits = credits;
        _hospitalizations = hospitalizations;
        _appointments = appointments;
    }

    /// <summary>Müşteri ve silinmemiş tüm hastaları (<paramref name="petIds"/>) için engeller.</summary>
    public async Task<RecordDeletionBlockers> ForClientAsync(
        Guid tenantId,
        Guid clientId,
        IReadOnlyCollection<Guid> petIds,
        DateTime utcNow,
        CancellationToken ct)
    {
        var hasOpenAccount =
            await _charges.AnyAsync(new OutstandingChargesOfClientSpec(tenantId, clientId), ct)
            || await _payments.AnyAsync(new UnallocatedPaymentsForClientSpec(tenantId, clientId, null, null), ct)
            || await _credits.AnyAsync(new ClientCreditsForClientSpec(tenantId, clientId, null, null, openOnly: true), ct);

        var (activeHospitalizations, futureAppointments) = await CountCareAsync(tenantId, petIds, utcNow, ct);
        return new RecordDeletionBlockers(hasOpenAccount, activeHospitalizations, futureAppointments);
    }

    /// <summary>Tek hasta için engeller; hesap müşteri düzeyinde olduğundan açık hesap hasta silmeyi engellemez.</summary>
    public async Task<RecordDeletionBlockers> ForPetAsync(Guid tenantId, Guid petId, DateTime utcNow, CancellationToken ct)
    {
        var (activeHospitalizations, futureAppointments) = await CountCareAsync(tenantId, [petId], utcNow, ct);
        return new RecordDeletionBlockers(false, activeHospitalizations, futureAppointments);
    }

    private async Task<(int ActiveHospitalizations, int FutureAppointments)> CountCareAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> petIds,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (petIds.Count == 0)
            return (0, 0);

        var hospitalizations = await _hospitalizations.CountAsync(new ActiveHospitalizationsOfPetsSpec(tenantId, petIds), ct);
        var appointments = await _appointments.CountAsync(new FutureScheduledAppointmentsOfPetsSpec(tenantId, petIds, utcNow), ct);
        return (hospitalizations, appointments);
    }
}

/// <summary>Müşterinin kalanı olan (borçlu) ücretleri; devir borcu dahil.</summary>
public sealed class OutstandingChargesOfClientSpec : Specification<Charge>
{
    public OutstandingChargesOfClientSpec(Guid tenantId, Guid clientId)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClientId == clientId && c.AllocatedAmount < c.Amount);
    }
}

public sealed class ActiveHospitalizationsOfPetsSpec : Specification<Hospitalization>
{
    public ActiveHospitalizationsOfPetsSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds)
    {
        var ids = petIds.ToArray();
        Query.AsNoTracking();
        Query.Where(h => h.TenantId == tenantId && ids.Contains(h.PetId));
        Query.Where(Hospitalization.IsActiveExpression);
    }
}

/// <summary>Zamanı gelmemiş planlı randevular (ADR-054 K5: engellenir, otomatik iptal edilmez).</summary>
public sealed class FutureScheduledAppointmentsOfPetsSpec : Specification<Appointment>
{
    public FutureScheduledAppointmentsOfPetsSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds, DateTime utcNow)
    {
        var ids = petIds.ToArray();
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId
                         && ids.Contains(a.PetId)
                         && a.Status == AppointmentStatus.Scheduled
                         && a.ScheduledAtUtc > utcNow);
    }
}
