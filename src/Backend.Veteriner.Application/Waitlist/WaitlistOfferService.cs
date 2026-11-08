using System.Globalization;
using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Waitlist;

namespace Backend.Veteriner.Application.Waitlist;

/// <summary>Kayda neden teklif gidemeyeceği (TIER-018). Klinik listede ve iptal ekranında uyarılır.</summary>
public enum WaitlistOfferBlocker
{
    None = 0,

    /// <summary>Müşterinin e-postası yok; teklif gitmez.</summary>
    NoEmail = 1,

    /// <summary>Müşteri hatırlatma iletisi istemiyor (personel ya da portal tercihi).</summary>
    RemindersOff = 2,
}

/// <summary>Boşalan saat için sıradaki uygun kayıt; <see cref="Blocker"/> doluysa teklif gitmez ama sayılır.</summary>
public sealed record WaitlistCandidate(WaitlistEntry Entry, Client Client, Pet Pet, WaitlistOfferBlocker Blocker);

public static class WaitlistErrors
{
    public const string NotFoundCode = "Waitlist.NotFound";
    public const string RecipientUnreachableCode = "WaitlistOffers.RecipientUnreachable";
    public const string SlotNotReleasedCode = "WaitlistOffers.SlotNotReleased";
    public const string OfferAlreadyOpenCode = "WaitlistOffers.OfferAlreadyOpen";
    public const string SlotTakenCode = "WaitlistOffers.SlotTaken";
    public const string ConcurrencyConflictCode = "WaitlistOffers.ConcurrencyConflict";
}

/// <summary>
/// Bekleme listesi teklifi — uygun kayıtların sırası ve teklif gönderimi (TIER-018). Otomatik işleyici ve personelin elle gönderimi aynı
/// yoldan geçer. İleti işlem/hizmet iletisidir, ticari değildir (ADR-046); hatırlatma tercihine uyar
/// (<see cref="OwnerNotificationChannelPolicy"/>, tür <see cref="OwnerNotificationType.WaitlistOffer"/>). Kayıtlar izlenir; kaydı
/// çağıran yapar.
/// </summary>
public sealed class WaitlistOfferService
{
    private readonly IReadRepository<WaitlistEntry> _entries;
    private readonly IReadRepository<WaitlistOffer> _offersRead;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<ClinicWorkingHour> _workingHours;
    private readonly IReadRepository<ClinicAppointmentSettings> _settings;
    private readonly AppointmentAssignment _assignment;
    private readonly AppointmentOwnerLinkIssuer _issuer;
    private readonly IEmailSender _email;
    private readonly IRepository<WaitlistOffer> _offers;
    private readonly IRepository<AppointmentOwnerLink> _links;
    private readonly IRepository<WaitlistEntryEvent> _events;

    public WaitlistOfferService(
        IReadRepository<WaitlistEntry> entries,
        IReadRepository<WaitlistOffer> offersRead,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<ClinicWorkingHour> workingHours,
        IReadRepository<ClinicAppointmentSettings> settings,
        AppointmentAssignment assignment,
        AppointmentOwnerLinkIssuer issuer,
        IEmailSender email,
        IRepository<WaitlistOffer> offers,
        IRepository<AppointmentOwnerLink> links,
        IRepository<WaitlistEntryEvent> events)
    {
        _entries = entries;
        _offersRead = offersRead;
        _clients = clients;
        _pets = pets;
        _appointments = appointments;
        _workingHours = workingHours;
        _settings = settings;
        _assignment = assignment;
        _issuer = issuer;
        _email = email;
        _offers = offers;
        _links = links;
        _events = events;
    }

    /// <summary>Teklif gidebilir mi — e-posta ve hatırlatma tercihi; kuralın tek yeri.</summary>
    public static WaitlistOfferBlocker BlockerFor(Client client)
    {
        if (string.IsNullOrWhiteSpace(client.Email))
            return WaitlistOfferBlocker.NoEmail;

        return OwnerNotificationChannelPolicy.ShouldSend(
            OwnerNotificationType.WaitlistOffer, OwnerNotificationChannel.Email, pushConfigured: false, client.IsAcceptingReminders)
            ? WaitlistOfferBlocker.None
            : WaitlistOfferBlocker.RemindersOff;
    }

    /// <summary>Saat boşalmış sayılır: randevu iptal edildi ve saati gelmedi (gelmedi işaretli randevu saat boşaltmaz).</summary>
    public static bool IsReleased(Appointment appointment, DateTime utcNow)
        => appointment.Status == AppointmentStatus.Cancelled && appointment.ScheduledAtUtc > utcNow;

    /// <summary>
    /// Boşalan saate uyan kayıtlar, sırayla (sıra anahtarı, sonra eklenme). Bu saat için daha önce teklif almış kayıt, tarih aralığı
    /// geçmiş kayıt ve bakım dışı hayvan dışlanır; e-postası ya da tercihi olmayan kayıt <see cref="WaitlistCandidate.Blocker"/> ile döner.
    /// </summary>
    public async Task<IReadOnlyList<WaitlistCandidate>> ListCandidatesAsync(Appointment source, DateTime utcNow, CancellationToken ct)
    {
        var waiting = await _entries.ListAsync(new WaitingEntriesByClinicSpec(source.TenantId, source.ClinicId), ct);
        if (waiting.Count == 0)
            return [];

        var alreadyOffered = (await _offersRead.ListAsync(new WaitlistOffersBySourceSpec(source.TenantId, source.Id), ct))
            .Select(o => o.WaitlistEntryId)
            .ToHashSet();
        var localStart = OperationDayBounds.ToLocal(source.ScheduledAtUtc);
        var today = OperationDayBounds.ToLocalDate(utcNow);

        var matching = waiting
            .Where(e => !alreadyOffered.Contains(e.Id)
                        && !e.IsPastRange(today)
                        && e.Matches(localStart, source.DurationMinutes, source.VeterinarianUserId))
            .ToList();
        if (matching.Count == 0)
            return [];

        var clients = (await _clients.ListAsync(new WaitlistClientsByIdsSpec(source.TenantId, matching.Select(e => e.ClientId).Distinct().ToArray()), ct))
            .ToDictionary(c => c.Id);
        var pets = (await _pets.ListAsync(new WaitlistPetsByIdsSpec(source.TenantId, matching.Select(e => e.PetId).Distinct().ToArray()), ct))
            .ToDictionary(p => p.Id);

        var result = new List<WaitlistCandidate>(matching.Count);
        foreach (var entry in matching)
        {
            if (!clients.TryGetValue(entry.ClientId, out var client) || !pets.TryGetValue(entry.PetId, out var pet) || !pet.IsReceivingCare)
                continue;
            result.Add(new WaitlistCandidate(entry, client, pet, BlockerFor(client)));
        }

        return result;
    }

    /// <summary>
    /// Teklifi oluşturur, kabul bağlantısını üretir ve e-postayı kuyruğa alır (hepsi izlenir, aynı kayıtta yazılır). Saat boşalmış
    /// olmalı, saat için ve kayıt için açık teklif olmamalı, saat bu kaydın hayvanı için randevuya uygun olmalı
    /// (<see cref="AppointmentSlotRules"/>). Saat için kabul edilmiş ya da "saat doldu" kapanmış teklif varsa yeni teklif açılmaz.
    /// </summary>
    public async Task<Result<WaitlistOffer>> SendAsync(
        WaitlistCandidate candidate,
        Appointment source,
        Clinic clinic,
        bool isAutomatic,
        Guid? actorUserId,
        DateTime utcNow,
        CancellationToken ct)
    {
        var entry = candidate.Entry;
        if (candidate.Blocker != WaitlistOfferBlocker.None)
        {
            return Fail(
                WaitlistErrors.RecipientUnreachableCode,
                candidate.Blocker == WaitlistOfferBlocker.NoEmail
                    ? "Müşterinin e-postası yok; teklif gönderilemez. Müşteriyi arayabilirsiniz."
                    : "Müşteri hatırlatma iletisi almak istemiyor; teklif gönderilemez. Müşteriyi arayabilirsiniz.");
        }

        if (!IsReleased(source, utcNow) || source.ClinicId != entry.ClinicId)
            return Fail(WaitlistErrors.SlotNotReleasedCode, "Bu saat boşalmış bir randevu saati değil.");

        var slotOffers = await _offersRead.ListAsync(new WaitlistOffersBySourceSpec(source.TenantId, source.Id), ct);
        if (slotOffers.Any(o => o.Status is WaitlistOfferStatus.Accepted or WaitlistOfferStatus.SlotTaken))
            return Fail(WaitlistErrors.SlotTakenCode, "Bu saat için teklif süreci bitti; saat doldu.");
        if (slotOffers.Any(o => o.IsOpen))
            return Fail(WaitlistErrors.OfferAlreadyOpenCode, "Bu saat için açık bir teklif var; yanıt ya da süre dolumu beklenir.");
        if (await _offersRead.AnyAsync(new OpenWaitlistOfferByEntrySpec(entry.TenantId, entry.Id), ct))
            return Fail(WaitlistErrors.OfferAlreadyOpenCode, "Bu kaydın açık bir teklifi var.");

        var created = WaitlistOffer.Create(
            entry,
            source.Id,
            source.ScheduledAtUtc,
            entry.EffectiveDurationMinutes(source.DurationMinutes),
            source.VeterinarianUserId,
            isAutomatic,
            actorUserId,
            utcNow);
        if (!created.IsSuccess)
            return created;
        var offer = created.Value!;

        var settings = await _settings.FirstOrDefaultAsync(new ClinicAppointmentSettingsByClinicSpec(entry.TenantId, entry.ClinicId), ct);
        var bookable = await AppointmentSlotRules.EnsureBookableAsync(
            _appointments, _workingHours, _assignment, settings, entry.TenantId, entry.ClinicId, entry.PetId,
            offer.VeterinarianUserId, offer.SlotStartUtc, offer.DurationMinutes, ct);
        if (!bookable.IsSuccess)
            return Result<WaitlistOffer>.Failure(bookable.Error);

        var issued = _issuer.IssueWaitlistOffer(entry.TenantId, entry.ClinicId, source.Id, offer.Id, offer.ExpiresAtUtc, utcNow);
        await _email.SendAsync(
            candidate.Client.Email!.Trim(),
            WaitlistOfferEmail.Subject,
            WaitlistOfferEmail.Body(candidate.Client.FullName, candidate.Pet.Name, clinic.Name, offer, issued.PageUrl),
            ct);

        await _offers.AddAsync(offer, ct);
        await _links.AddAsync(issued.Link, ct);
        await _events.AddAsync(WaitlistEntryEvent.Create(entry, WaitlistEntryEventType.OfferSent, offer.Id, actorUserId, utcNow), ct);
        return Result<WaitlistOffer>.Success(offer);
    }

    private static Result<WaitlistOffer> Fail(string code, string message) => Result<WaitlistOffer>.Failure(code, message);
}

/// <summary>
/// Teklif e-postası (TIER-018). Hizmet iletisidir, ticari ileti değildir (ADR-046): tanıtım içermez. Asgari veri: klinik adı, hayvan
/// adı, saat ve bitiş; teşhis ve not yoktur.
/// </summary>
public static class WaitlistOfferEmail
{
    public const string Subject = "Boşalan randevu saati";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Body(string? recipientName, string? petName, string clinicName, WaitlistOffer offer, string pageUrl)
    {
        var name = string.IsNullOrWhiteSpace(recipientName) ? "Müşterimiz" : recipientName.Trim();
        var pet = string.IsNullOrWhiteSpace(petName) ? "dostunuz" : petName.Trim();
        return
            $"Sayın {name},\n" +
            $"{clinicName} kliniğinde {Format(offer.SlotStartUtc)} saatinde bir randevu yeri boşaldı. {pet} için bekleme listesinde " +
            "olduğunuzdan bu saati size öneriyoruz.\n\n" +
            $"Randevuyu almak ya da istemediğinizi bildirmek için:\n{pageUrl}\n\n" +
            $"Teklif {Format(offer.ExpiresAtUtc)} saatine kadar geçerlidir ve yalnız bir kez kullanılabilir. Bu sürede yanıt " +
            "vermezseniz sıradaki kişiye geçer; bekleme listesindeki yeriniz korunur.\n";
    }

    private static string Format(DateTime utc) => OperationDayBounds.ToLocal(utc).ToString("d MMMM yyyy dddd HH:mm", Turkish);
}
