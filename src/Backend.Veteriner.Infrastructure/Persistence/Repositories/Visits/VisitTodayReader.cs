using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Visits;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Visits;

/// <summary>
/// Bugün yüzeyini komut veritabanından okur: Visit satırları ile Visit'i olmayan planlı randevular.
/// Randevulu Visit tek satırdır (randevu ayrıca satır üretmez).
/// </summary>
public sealed class VisitTodayReader : IVisitTodayReader
{
    private readonly AppDbContext _db;

    public VisitTodayReader(AppDbContext db) => _db = db;

    public async Task<VisitTodayReadResult> GetAsync(VisitTodayReadRequest request, CancellationToken ct = default)
    {
        var visitRows = await ReadVisitRowsAsync(request, ct);
        var plannedRows = await ReadPlannedRowsAsync(request, ct);

        var allRows = visitRows.Concat(plannedRows).ToList();
        var paymentRecorded = await ReadPaymentRecordedKeysAsync(request, allRows, ct);

        var activeHospitalizations = await ReadActiveHospitalizationsAsync(request, ct);
        var hospitalizedPetIds = activeHospitalizations.Select(h => h.PetId).ToHashSet();

        var items = allRows
            .Select(r => r.ToDto(
                paymentRecorded.Contains(r.Key) ? TodayPaymentIndicator.PaymentRecorded : TodayPaymentIndicator.NoPaymentRecorded,
                hospitalizedPetIds.Contains(r.PetId)))
            .ToList();

        return new VisitTodayReadResult(items, activeHospitalizations);
    }

    private async Task<List<Row>> ReadVisitRowsAsync(VisitTodayReadRequest request, CancellationToken ct)
    {
        var start = request.DayStartUtc;
        var end = request.DayEndUtc;
        var includeCarriedOver = request.IncludeCarriedOver;

        // Devralınan: önceki günlerden kalan, tamamlanmamış gelişler (yalnızca bugünün görünümünde).
        var visits = _db.Visits.AsNoTracking()
            .Where(v => v.TenantId == request.TenantId
                        && v.ClinicId == request.ClinicId
                        && v.VoidedAtUtc == null
                        && ((v.ArrivedAtUtc >= start && v.ArrivedAtUtc < end)
                            || (includeCarriedOver
                                && v.ArrivedAtUtc < start
                                && v.CareStatus != VisitCareStatus.Completed)));

        return await (
                from v in visits
                join p in _db.Pets.AsNoTracking() on v.PetId equals p.Id
                join c in _db.Clients.AsNoTracking() on p.ClientId equals c.Id
                join a in _db.Appointments.AsNoTracking() on v.AppointmentId equals a.Id into appointments
                from a in appointments.DefaultIfEmpty()
                orderby v.ArrivedAtUtc, v.Id
                select new Row(
                    v.Id,
                    v.AppointmentId,
                    v.PetId,
                    p.Name,
                    p.Species != null ? p.Species.Name : null,
                    c.Id,
                    c.FullName,
                    c.Phone,
                    a != null ? a.ScheduledAtUtc : (DateTime?)null,
                    v.ArrivedAtUtc,
                    v.CareStatus,
                    a != null ? a.Status : (AppointmentStatus?)null,
                    v.ResponsibleVeterinarianUserId,
                    v.ArrivedAtUtc < start))
            .Take(request.MaxItems + 1)
            .ToListAsync(ct);
    }

    private async Task<List<Row>> ReadPlannedRowsAsync(VisitTodayReadRequest request, CancellationToken ct)
    {
        var start = request.DayStartUtc;
        var end = request.DayEndUtc;

        // Visit'i olmayan, iptal edilmemiş randevular (muayeneyle Completed olmuş ama Visit'siz olanlar dahil).
        var appointments = _db.Appointments.AsNoTracking()
            .Where(a => a.TenantId == request.TenantId
                        && a.ClinicId == request.ClinicId
                        && a.ScheduledAtUtc >= start
                        && a.ScheduledAtUtc < end
                        && a.Status != AppointmentStatus.Cancelled
                        && !_db.Visits.Any(v => v.AppointmentId == a.Id && v.VoidedAtUtc == null));

        return await (
                from a in appointments
                join p in _db.Pets.AsNoTracking() on a.PetId equals p.Id
                join c in _db.Clients.AsNoTracking() on p.ClientId equals c.Id
                orderby a.ScheduledAtUtc, a.Id
                select new Row(
                    null,
                    a.Id,
                    a.PetId,
                    p.Name,
                    p.Species != null ? p.Species.Name : null,
                    c.Id,
                    c.FullName,
                    c.Phone,
                    a.ScheduledAtUtc,
                    null,
                    null,
                    a.Status,
                    null,
                    false))
            .Take(request.MaxItems + 1)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Ödeme kaydı olan satır anahtarları. Bir satıra ödeme şu yollarla bağlanır: ödemenin randevusu satırın randevusu,
    /// veya ödemenin muayenesi satırın Visit'ine ya da randevusuna bağlı muayene.
    /// </summary>
    private async Task<HashSet<string>> ReadPaymentRecordedKeysAsync(
        VisitTodayReadRequest request, List<Row> rows, CancellationToken ct)
    {
        var visitIds = rows.Where(r => r.VisitId.HasValue).Select(r => r.VisitId!.Value).ToList();
        var appointmentIds = rows.Where(r => r.AppointmentId.HasValue).Select(r => r.AppointmentId!.Value).ToList();
        if (visitIds.Count == 0 && appointmentIds.Count == 0)
            return [];

        var examinations = await _db.Examinations.AsNoTracking()
            .Where(e => e.TenantId == request.TenantId
                        && ((e.VisitId != null && visitIds.Contains(e.VisitId.Value))
                            || (e.AppointmentId != null && appointmentIds.Contains(e.AppointmentId.Value))))
            .Select(e => new { e.Id, e.VisitId, e.AppointmentId })
            .ToListAsync(ct);
        var examinationIds = examinations.Select(e => e.Id).ToList();

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.TenantId == request.TenantId
                        && ((p.AppointmentId != null && appointmentIds.Contains(p.AppointmentId.Value))
                            || (p.ExaminationId != null && examinationIds.Contains(p.ExaminationId.Value))))
            .Select(p => new { p.AppointmentId, p.ExaminationId })
            .ToListAsync(ct);

        var paidAppointmentIds = payments.Where(p => p.AppointmentId.HasValue)
            .Select(p => p.AppointmentId!.Value).ToHashSet();
        var paidExaminationIds = payments.Where(p => p.ExaminationId.HasValue)
            .Select(p => p.ExaminationId!.Value).ToHashSet();

        var paidVisitIds = examinations
            .Where(e => paidExaminationIds.Contains(e.Id) && e.VisitId.HasValue)
            .Select(e => e.VisitId!.Value).ToHashSet();
        paidAppointmentIds.UnionWith(examinations
            .Where(e => paidExaminationIds.Contains(e.Id) && e.AppointmentId.HasValue)
            .Select(e => e.AppointmentId!.Value));

        return rows
            .Where(r => (r.VisitId.HasValue && paidVisitIds.Contains(r.VisitId.Value))
                        || (r.AppointmentId.HasValue && paidAppointmentIds.Contains(r.AppointmentId.Value)))
            .Select(r => r.Key)
            .ToHashSet();
    }

    private async Task<List<TodayHospitalizationDto>> ReadActiveHospitalizationsAsync(
        VisitTodayReadRequest request, CancellationToken ct)
        => await (
                from h in _db.Hospitalizations.AsNoTracking()
                join p in _db.Pets.AsNoTracking() on h.PetId equals p.Id
                join c in _db.Clients.AsNoTracking() on p.ClientId equals c.Id
                where h.TenantId == request.TenantId
                      && h.ClinicId == request.ClinicId
                      && h.DischargedAtUtc == null
                orderby h.AdmittedAtUtc, h.Id
                select new TodayHospitalizationDto(
                    h.Id,
                    h.PetId,
                    p.Name,
                    c.FullName,
                    h.AdmittedAtUtc,
                    h.PlannedDischargeAtUtc))
            .ToListAsync(ct);

    private sealed record Row(
        Guid? VisitId,
        Guid? AppointmentId,
        Guid PetId,
        string PetName,
        string? SpeciesName,
        Guid ClientId,
        string ClientName,
        string? ClientPhone,
        DateTime? ScheduledAtUtc,
        DateTime? ArrivedAtUtc,
        VisitCareStatus? CareStatus,
        AppointmentStatus? AppointmentStatus,
        Guid? ResponsibleVeterinarianUserId,
        bool IsCarriedOver)
    {
        /// <summary>Satırı tekil tanımlar: Visit satırı Visit kimliğiyle, planlı satır randevu kimliğiyle.</summary>
        public string Key => VisitId.HasValue ? $"v:{VisitId}" : $"a:{AppointmentId}";

        public TodayItemDto ToDto(TodayPaymentIndicator payment, bool hasActiveHospitalization)
            => new(
                VisitId,
                AppointmentId,
                PetId,
                PetName,
                SpeciesName,
                ClientId,
                ClientName,
                ClientPhone,
                ScheduledAtUtc,
                ArrivedAtUtc,
                CareStatus,
                AppointmentStatus,
                ResponsibleVeterinarianUserId,
                IsCarriedOver,
                payment,
                hasActiveHospitalization);
    }
}
