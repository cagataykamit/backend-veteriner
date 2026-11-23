using Ardalis.Specification;
using Backend.Veteriner.Application.ClientCredits;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Visits;

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
    private readonly IReadRepository<Visit> _visits;

    public RecordDeletionBlockerReader(
        IReadRepository<Charge> charges,
        IReadRepository<Payment> payments,
        IReadRepository<ClientCredit> credits,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Appointment> appointments,
        IReadRepository<Visit> visits)
    {
        _charges = charges;
        _payments = payments;
        _credits = credits;
        _hospitalizations = hospitalizations;
        _appointments = appointments;
        _visits = visits;
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

        var care = await CountCareAsync(tenantId, petIds, utcNow, ct);
        return new RecordDeletionBlockers(hasOpenAccount, care.ActiveHospitalizations, care.FutureAppointments, care.OpenVisits);
    }

    /// <summary>Tek hasta için engeller; hesap müşteri düzeyinde olduğundan açık hesap hasta silmeyi engellemez.</summary>
    public async Task<RecordDeletionBlockers> ForPetAsync(Guid tenantId, Guid petId, DateTime utcNow, CancellationToken ct)
    {
        var care = await CountCareAsync(tenantId, [petId], utcNow, ct);
        return new RecordDeletionBlockers(false, care.ActiveHospitalizations, care.FutureAppointments, care.OpenVisits);
    }

    private async Task<(int ActiveHospitalizations, int FutureAppointments, int OpenVisits)> CountCareAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> petIds,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (petIds.Count == 0)
            return (0, 0, 0);

        var hospitalizations = await _hospitalizations.CountAsync(new ActiveHospitalizationsOfPetsSpec(tenantId, petIds), ct);
        var appointments = await _appointments.CountAsync(new FutureScheduledAppointmentsOfPetsSpec(tenantId, petIds, utcNow), ct);
        var (dayStartUtc, dayEndUtc) = OperationDayBounds.ForUtcNow(utcNow);
        var visits = await _visits.CountAsync(new OpenVisitsOfPetsSpec(tenantId, petIds, dayStartUtc, dayEndUtc), ct);
        return (hospitalizations, appointments, visits);
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

/// <summary>
/// Bugün (İstanbul günü) kliniğe gelmiş, tamamlanmamış ve iptal edilmemiş gelişler (ADR-054 K5). Önceki günlerden açık kalanlar
/// "Açık işler"de izlenir, silmeyi engellemez.
/// </summary>
public sealed class OpenVisitsOfPetsSpec : Specification<Visit>
{
    public OpenVisitsOfPetsSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds, DateTime dayStartUtc, DateTime dayEndUtc)
    {
        var ids = petIds.ToArray();
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId
                         && ids.Contains(v.PetId)
                         && v.VoidedAtUtc == null
                         && v.CareStatus != VisitCareStatus.Completed
                         && v.ArrivedAtUtc >= dayStartUtc
                         && v.ArrivedAtUtc < dayEndUtc);
    }
}
