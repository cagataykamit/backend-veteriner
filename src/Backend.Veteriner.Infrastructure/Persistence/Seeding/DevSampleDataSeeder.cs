using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Charges.Commands.Create;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence.Repositories;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// YALNIZCA geliştirme ortamı: tarayıcıda elle denemek için örnek randevu, ücret ve ödeme verisi.
/// Kayıtlar gerçek uygulama handler'larıyla (randevu/ücret/ödeme) oluşturulur; sayaçlar, tahsis kuralı, RequestId/hash ve
/// outbox olayları üretim yoluyla aynıdır. Yalnızca eksik olanı oluşturur (idempotent: ücret/ödeme sabit RequestId,
/// randevu "ÖRNEK ..." not etiketi ile). Kimlik bilgisi gerektirmez. Üretim/staging'de çalışmaz (ortam kontrolü) ve
/// normal <c>seed</c> hattına dahil değildir; ayrı <c>seed-dev-sample-data</c> komutuyla çalışır.
/// </summary>
public static class DevSampleDataSeeder
{
    public const string TenantName = DevTestVeterinarianSeeder.TenantName;
    public const string ClinicName = DevTestVeterinarianSeeder.ClinicName;

    private const string Prefix = "ÖRNEK";
    private const int SlotMinutes = 30;
    private const int SearchLimitMinutes = 12 * 60;

    private static readonly TimeZoneInfo ClinicTimeZone = ResolveClinicTimeZone();

    public sealed record Summary(int Appointments, int Charges, int Payments);

    private sealed record Persona(Client Client, Pet Pet);

    /// <returns>Oluşturulan kayıt sayıları; kiracı/klinik/kullanıcı/tür bulunamadıysa null (hiçbir şey yazılmaz).</returns>
    public static async Task<Summary?> SeedAsync(
        AppDbContext db,
        IOutboxBuffer outboxBuffer,
        IHostEnvironment environment,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Örnek veri seed'i yalnızca Development ortamında çalışır.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == TenantName, ct);
        var clinic = tenant is null
            ? null
            : await db.Clinics.FirstOrDefaultAsync(
                c => c.TenantId == tenant.Id && c.Name == ClinicName && c.IsActive, ct);
        var userId = tenant is null
            ? (Guid?)null
            : await db.UserTenants.Where(x => x.TenantId == tenant.Id).Select(x => (Guid?)x.UserId).FirstOrDefaultAsync(ct);
        var species = await db.Species.FirstOrDefaultAsync(ct);
        if (tenant is null || clinic is null || userId is null || species is null)
        {
            logger?.LogWarning(
                "Örnek veri seed atlandı: kiracı/klinik/kiracı kullanıcısı/hayvan türü bulunamadı ({Tenant} / {Clinic}).",
                TenantName, ClinicName);
            return null;
        }

        var handlers = new Handlers(db, outboxBuffer, tenant.Id, clinic.Id, userId.Value);

        var maksut = await ResolveAsync(db, tenant, species, "Maksut Kamit", "Cedi", $"{Prefix} Müşteri A", ct);
        var mehmet = await ResolveAsync(db, tenant, species, "Mehmet Çağatay Kamit", "Çomar", $"{Prefix} Müşteri B", ct);
        var yagmur = await ResolveAsync(db, tenant, species, "Yağmur Karayılmaz", "Köpük", $"{Prefix} Müşteri C", ct);
        var legacy = await ResolveAsync(db, tenant, species, $"{Prefix} Müşteri D (eski ödeme)", null, null, ct);

        var appointments = await SeedAppointmentsAsync(db, handlers, tenant.Id, clinic.Id, maksut, mehmet, yagmur, logger, ct);

        var charges = 0;
        var payments = 0;

        // (a) tam ödenmiş: 1.000 TL ücret, 1.000 TL ödeme
        var (c, p) = await SeedScenarioAsync(
            db, handlers, tenant.Id, clinic.Id, maksut, "tam",
            [($"{Prefix} muayene (tam ödenmiş)", 1, 1000m)], paymentAmount: 1000m, PaymentMethod.Cash, ct);
        charges += c;
        payments += p;

        // (b) kısmi ödenmiş: 1.000 TL ücret, 400 TL ödeme, 600 TL kalan
        (c, p) = await SeedScenarioAsync(
            db, handlers, tenant.Id, clinic.Id, mehmet, "kismi",
            [($"{Prefix} tedavi (kısmi ödenmiş)", 1, 1000m)], paymentAmount: 400m, PaymentMethod.Card, ct);
        charges += c;
        payments += p;

        // (c) hiç ödenmemiş: iki ücret satırı
        (c, p) = await SeedScenarioAsync(
            db, handlers, tenant.Id, clinic.Id, yagmur, "odenmemis",
            [($"{Prefix} aşı (ödenmemiş)", 1, 350m), ($"{Prefix} ilaç (ödenmemiş)", 2, 275m)], paymentAmount: null, PaymentMethod.Cash, ct);
        charges += c;
        payments += p;

        // (d) tahsissiz eski ödeme: handler tahsis zorunlu kıldığı için yalnızca domain kurucusuyla (AllocatedAmount = 0) yazılır.
        payments += await SeedLegacyPaymentAsync(db, outboxBuffer, tenant.Id, clinic, legacy, ct);

        var summary = new Summary(appointments, charges, payments);
        logger?.LogInformation(
            "Örnek veri seed bitti: {Appointments} randevu, {Charges} ücret, {Payments} ödeme oluşturuldu.",
            summary.Appointments, summary.Charges, summary.Payments);
        return summary;
    }

    private static async Task<int> SeedAppointmentsAsync(
        AppDbContext db,
        Handlers handlers,
        Guid tenantId,
        Guid clinicId,
        Persona past1,
        Persona past2,
        Persona future,
        ILogger? logger,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ClinicTimeZone);
        var today = localNow.Date;
        var floor = today.AddMinutes(Math.Floor(localNow.TimeOfDay.TotalMinutes / SlotMinutes) * SlotMinutes);

        // Geçmiş saatler: önce ~2 saat öncesinden geriye, bulunamazsa şimdiye yaklaşarak; kuralları handler doğrular.
        var pastOffsets = Enumerable.Range(0, (SearchLimitMinutes - 120) / SlotMinutes + 1)
            .Select(i => 120 + (i * SlotMinutes))
            .Concat([90, 60, 30, 0])
            .Select(minutes => floor.AddMinutes(-minutes))
            .Where(local => local.Date == today)
            .ToList();
        var futureSlots = Enumerable.Range(0, SearchLimitMinutes / SlotMinutes)
            .Select(i => floor.AddMinutes(60 + (i * SlotMinutes)))
            .Append(floor.AddMinutes(SlotMinutes))
            .Where(local => local.Date == today)
            .ToList();

        var created = 0;
        var usedPast = new List<DateTime>();
        // Önce şimdiye yakın olan (geçmiş-2), sonra daha eski (geçmiş-1) saat seçilir.
        created += await EnsureAppointmentAsync(
            db, handlers, tenantId, clinicId, past2, "geçmiş-2", today, pastOffsets, usedPast, AppointmentType.Checkup, logger, ct);
        created += await EnsureAppointmentAsync(
            db, handlers, tenantId, clinicId, past1, "geçmiş-1", today, pastOffsets, usedPast, AppointmentType.Examination, logger, ct);
        created += await EnsureAppointmentAsync(
            db, handlers, tenantId, clinicId, future, "gelecek", today, futureSlots, [], AppointmentType.Vaccination, logger, ct);
        return created;
    }

    private static async Task<int> EnsureAppointmentAsync(
        AppDbContext db,
        Handlers handlers,
        Guid tenantId,
        Guid clinicId,
        Persona persona,
        string kind,
        DateTime today,
        IReadOnlyList<DateTime> candidateLocalTimes,
        List<DateTime> usedLocalTimes,
        AppointmentType type,
        ILogger? logger,
        CancellationToken ct)
    {
        var tag = $"{Prefix} randevu {kind} {today:yyyy-MM-dd}";
        if (await db.Appointments.AnyAsync(
                a => a.TenantId == tenantId && a.ClinicId == clinicId && a.Notes != null && a.Notes.StartsWith(tag), ct))
        {
            return 0;
        }

        string? lastError = null;
        foreach (var local in candidateLocalTimes.Where(l => !usedLocalTimes.Contains(l)))
        {
            var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ClinicTimeZone);
            var result = await handlers.CreateAppointment.Handle(
                new CreateAppointmentCommand(clinicId, persona.Pet.Id, scheduledUtc, type, null, tag), ct);
            if (result.IsSuccess)
            {
                usedLocalTimes.Add(local);
                return 1;
            }

            lastError = result.Error.Code;
        }

        logger?.LogWarning(
            "Örnek randevu ({Kind}) oluşturulamadı: bugün için uygun saat bulunamadı (son hata: {Error}).", kind, lastError);
        return 0;
    }

    private static async Task<(int Charges, int Payments)> SeedScenarioAsync(
        AppDbContext db,
        Handlers handlers,
        Guid tenantId,
        Guid clinicId,
        Persona persona,
        string scenario,
        IReadOnlyList<(string Description, decimal Quantity, decimal UnitPrice)> lines,
        decimal? paymentAmount,
        PaymentMethod method,
        CancellationToken ct)
    {
        var created = 0;
        var chargeIds = new List<(Guid Id, decimal Amount)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var requestId = StableId(tenantId, $"charge:{scenario}:{i}");
            var existed = await db.Charges.AnyAsync(x => x.TenantId == tenantId && x.RequestId == requestId, ct);
            var result = await handlers.CreateCharge.Handle(
                new CreateChargeCommand(
                    clinicId, persona.Client.Id, persona.Pet.Id, null, null,
                    lines[i].Description, lines[i].Quantity, lines[i].UnitPrice, requestId), ct);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"Örnek ücret oluşturulamadı ({scenario}): {result.Error.Code}");

            if (!existed)
                created++;

            chargeIds.Add((result.Value!.Charge.Id, result.Value.Charge.Amount));
        }

        if (paymentAmount is not { } amount)
            return (created, 0);

        var paymentRequestId = StableId(tenantId, $"payment:{scenario}");
        if (await db.Payments.AnyAsync(x => x.TenantId == tenantId && x.RequestId == paymentRequestId, ct))
            return (created, 0);

        // Tek ücrete tahsis: ödeme tutarı ücretin açık tutarını aşmaz (senaryolar bunu garanti eder).
        var allocations = new List<PaymentAllocationLineDto> { new(chargeIds[0].Id, amount) };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var paid = await handlers.CreatePayment.Handle(
            new CreatePaymentCommand(
                clinicId, persona.Client.Id, persona.Pet.Id, null, null, amount, Charge.DefaultCurrency, method,
                DateTime.UtcNow, $"{Prefix} ödeme ({scenario})", paymentRequestId, allocations), ct);
        if (!paid.IsSuccess)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException($"Örnek ödeme oluşturulamadı ({scenario}): {paid.Error.Code}");
        }

        await tx.CommitAsync(ct);
        return (created, 1);
    }

    private static async Task<int> SeedLegacyPaymentAsync(
        AppDbContext db,
        IOutboxBuffer outboxBuffer,
        Guid tenantId,
        Clinic clinic,
        Persona persona,
        CancellationToken ct)
    {
        var notes = $"{Prefix} eski ödeme (tahsissiz)";
        if (await db.Payments.AnyAsync(x => x.TenantId == tenantId && x.ClientId == persona.Client.Id && x.Notes == notes, ct))
            return 0;

        var payment = new Payment(
            tenantId, clinic.Id, persona.Client.Id, null, null, null,
            500m, Charge.DefaultCurrency, PaymentMethod.Cash, DateTime.UtcNow, notes);
        await db.Payments.AddAsync(payment, ct);

        var outbox = new PaymentIntegrationEventOutbox(outboxBuffer);
        await outbox.EnqueueAsync(
            PaymentIntegrationEventTypes.Created,
            new PaymentCreatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                PaymentProjectionSnapshotFactory.Create(payment, persona.Client.FullName, clinic.Name, null)),
            ct);
        await db.SaveChangesAsync(ct);
        return 1;
    }

    /// <summary>Mevcut müşteri/hayvanı kullanır; yoksa ayırt edilebilir "ÖRNEK" müşteri ve hayvanı oluşturur.</summary>
    private static async Task<Persona> ResolveAsync(
        AppDbContext db,
        Tenant tenant,
        Species species,
        string clientName,
        string? petName,
        string? fallbackClientName,
        CancellationToken ct)
    {
        if (petName is not null)
        {
            var existing = await (
                from p in db.Pets
                join c in db.Clients on p.ClientId equals c.Id
                where p.TenantId == tenant.Id && c.TenantId == tenant.Id && c.FullName == clientName && p.Name == petName
                select new { Client = c, Pet = p }).FirstOrDefaultAsync(ct);
            if (existing is not null)
                return new Persona(existing.Client, existing.Pet);
        }

        var name = fallbackClientName ?? clientName;
        var client = await db.Clients.FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.FullName == name, ct);
        var utcNow = TimeProvider.System.GetUtcNow().UtcDateTime;
        if (client is null)
        {
            client = new Client(tenant.Id, name, utcNow);
            await db.Clients.AddAsync(client, ct);
            await db.SaveChangesAsync(ct);
        }

        var samplePetName = $"{Prefix} Hayvan";
        var pet = await db.Pets.FirstOrDefaultAsync(
            p => p.TenantId == tenant.Id && p.ClientId == client.Id && p.Name == samplePetName, ct);
        if (pet is null)
        {
            pet = new Pet(tenant.Id, client.Id, samplePetName, species.Id);
            await db.Pets.AddAsync(pet, ct);
            await db.SaveChangesAsync(ct);
        }

        return new Persona(client, pet);
    }

    /// <summary>Kiracıya ve senaryoya özgü sabit RequestId (aynı komut tekrar çalışınca aynı anahtar).</summary>
    private static Guid StableId(Guid tenantId, string key)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"vetinity-dev-sample:{tenantId:N}:{key}")));

    private static TimeZoneInfo ResolveClinicTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }

    /// <summary>Üretim handler'ları, HTTP bağlamı yerine sabit kiracı/klinik/kullanıcı bağlamıyla kurulur.</summary>
    private sealed class Handlers
    {
        public CreateAppointmentCommandHandler CreateAppointment { get; }
        public CreateChargeCommandHandler CreateCharge { get; }
        public CreatePaymentCommandHandler CreatePayment { get; }

        public Handlers(AppDbContext db, IOutboxBuffer outboxBuffer, Guid tenantId, Guid clinicId, Guid userId)
        {
            var tenantContext = new FixedTenantContext(tenantId);
            var clinicContext = new FixedClinicContext(clinicId);
            var clientContext = new FixedClientContext(userId);
            var scope = new FixedClinicScopeResolver();

            var tenants = new EfReadRepository<Tenant>(db);
            var clinics = new EfReadRepository<Clinic>(db);
            var clients = new EfReadRepository<Client>(db);
            var pets = new EfReadRepository<Pet>(db);
            var appointmentsRead = new EfReadRepository<Appointment>(db);
            var payments = new EfReadRepository<Payment>(db);
            var charges = new EfReadRepository<Charge>(db);

            CreateAppointment = new CreateAppointmentCommandHandler(
                tenantContext,
                clinicContext,
                scope,
                tenants,
                clinics,
                pets,
                appointmentsRead,
                new EfReadRepository<ClinicAppointmentSettings>(db),
                new EfReadRepository<ClinicWorkingHour>(db),
                new EfRepository<Appointment>(db),
                new AppointmentProjectionSnapshotFactory(db),
                new AppointmentIntegrationEventOutbox(outboxBuffer));

            CreateCharge = new CreateChargeCommandHandler(
                tenantContext,
                clinicContext,
                clientContext,
                scope,
                tenants,
                clients,
                pets,
                new EfReadRepository<Domain.Visits.Visit>(db),
                new EfReadRepository<Domain.Examinations.Examination>(db),
                charges,
                new EfRepository<Charge>(db),
                TimeProvider.System);

            CreatePayment = new CreatePaymentCommandHandler(
                tenantContext,
                clinicContext,
                clientContext,
                scope,
                tenants,
                clinics,
                clients,
                pets,
                appointmentsRead,
                new EfReadRepository<Domain.Examinations.Examination>(db),
                payments,
                new EfRepository<Payment>(db),
                new EfRepository<Charge>(db),
                new EfRepository<PaymentAllocation>(db),
                TimeProvider.System,
                new PaymentIntegrationEventOutbox(outboxBuffer));
        }
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private sealed class FixedClinicContext(Guid clinicId) : IClinicContext
    {
        public Guid? ClinicId { get; } = clinicId;
    }

    private sealed class FixedClientContext(Guid userId) : IClientContext
    {
        public Guid? UserId { get; } = userId;
        public string? IpAddress => null;
        public string? UserAgent => null;
        public string? Path => null;
        public string? Method => null;
        public string? CorrelationId => null;
    }

    /// <summary>Seed sistem adına çalışır: istenen klinik kiracıya ait olduğu için kapsam doğrudan verilir.</summary>
    private sealed class FixedClinicScopeResolver : IClinicReadScopeResolver
    {
        public Task<Result<ClinicReadScope>> ResolveAsync(Guid tenantId, Guid? requestClinicId, CancellationToken ct)
            => Task.FromResult(Result<ClinicReadScope>.Success(new ClinicReadScope(requestClinicId, null)));
    }
}
