using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.AppointmentRequests;

/// <summary>
/// Online randevu talebi (ADR-042 K1/K4; APPT-009). İki giriş kapısı tek model: portal (kayıtlı sahip, kendi hayvanı ya da
/// serbest metin hayvan) ve kliniğe özel bağlantı (e-postası doğrulanmış kimliksiz kişi). Talep bir iletişim anlığı taşır;
/// bağlantıdan gelen kişi için müşteri/hasta/hesap oluşmaz. Durum geçişleri yalnız buradan yapılır ve her geçiş kim/ne zaman
/// bilgisiyle geçmişe yazılır. Bekleyen talep saati bloke etmez; çakışma onayda denetlenir.
/// </summary>
public sealed class AppointmentRequest : AggregateRoot, ITenantOwned, IClinicScoped
{
    public const int ContactNameMaxLength = 150;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 30;
    public const int PetNameMaxLength = 100;
    public const int PetSpeciesMaxLength = 60;
    public const int PetBreedMaxLength = 100;
    public const int NoteMaxLength = 500;
    public const int RejectionReasonMinLength = 3;
    public const int RejectionReasonMaxLength = 500;

    /// <summary>Aynı e-posta + klinik için en fazla bekleyen talep (ADR-042 K6).</summary>
    public const int MaxPendingPerEmailAndClinic = 3;

    private readonly List<AppointmentRequestStatusChange> _history = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public AppointmentRequestSource Source { get; private set; }
    public AppointmentRequestStatus Status { get; private set; }

    public Guid ReasonId { get; private set; }

    /// <summary>Talep anındaki neden adı (neden sonradan değişse de talep okunur kalır).</summary>
    public string ReasonName { get; private set; } = default!;

    public DateTime RequestedStartUtc { get; private set; }
    public int DurationMinutes { get; private set; }

    public Guid? OwnerAccountId { get; private set; }
    public Guid? ClientId { get; private set; }
    public Guid? PetId { get; private set; }

    public string ContactName { get; private set; } = default!;

    /// <summary>Küçük harfe indirilmiş e-posta; bekleyen talep sınırı ve bildirimler bu adresle.</summary>
    public string ContactEmail { get; private set; } = default!;

    public string? ContactPhone { get; private set; }
    public string? PetName { get; private set; }
    public string? PetSpecies { get; private set; }
    public string? PetBreed { get; private set; }
    public string? OwnerNote { get; private set; }

    public Guid? AppointmentId { get; private set; }
    public DateTime? ApprovedStartUtc { get; private set; }
    public Guid? ApprovedVeterinarianUserId { get; private set; }

    /// <summary>Klinik farklı saatle onayladı (sahibe "saat değiştirildi" bildirimi gider).</summary>
    public bool WasRescheduled { get; private set; }

    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>Eşzamanlı karar koruması (iki personel aynı talebi onaylayamaz).</summary>
    public byte[] RowVersion { get; private set; } = default!;

    public IReadOnlyCollection<AppointmentRequestStatusChange> History => _history.AsReadOnly();

    public bool IsPending => Status == AppointmentRequestStatus.Pending;

    /// <summary>Hayvan metni: kayıtlı hayvan yoksa sahibin yazdığı ad (tür/ırk).</summary>
    public bool HasFreeTextPet => PetId is null;

    private AppointmentRequest() { }

    /// <summary>Portal talebi: bağın müşterisi; hayvan kayıtlıysa <paramref name="petId"/>, değilse serbest metin.</summary>
    public static Result<AppointmentRequest> CreateFromPortal(
        Guid tenantId,
        Guid clinicId,
        Guid ownerAccountId,
        Guid clientId,
        Guid? petId,
        PetText? petText,
        AppointmentReason reason,
        DateTime requestedStartUtc,
        string contactName,
        string contactEmail,
        string? contactPhone,
        string? note,
        DateTime utcNow)
    {
        if (ownerAccountId == Guid.Empty || clientId == Guid.Empty)
            return Invalid("Portal hesabı ve müşteri zorunludur.");

        return Create(
            tenantId, clinicId, AppointmentRequestSource.Portal, ownerAccountId, clientId, petId, petText, reason,
            requestedStartUtc, contactName, contactEmail, contactPhone, note, utcNow, AppointmentRequestActorKind.Owner);
    }

    /// <summary>Bağlantı talebi: kimliksiz kişi, hayvan yalnız serbest metin; e-posta doğrulaması uygulama katmanında yapılmıştır.</summary>
    public static Result<AppointmentRequest> CreateFromLink(
        Guid tenantId,
        Guid clinicId,
        PetText petText,
        AppointmentReason reason,
        DateTime requestedStartUtc,
        string contactName,
        string contactEmail,
        string contactPhone,
        string? note,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(contactPhone))
            return Invalid("Telefon zorunludur.");

        return Create(
            tenantId, clinicId, AppointmentRequestSource.Link, null, null, null, petText, reason,
            requestedStartUtc, contactName, contactEmail, contactPhone, note, utcNow, AppointmentRequestActorKind.Owner);
    }

    /// <summary>
    /// Onay: randevu oluşturuldu ve talebe bağlanır. Saat istenenden farklıysa yeniden planlanmış sayılır. Müşteri/hayvan
    /// eşleştirmesi onayda kesinleşir.
    /// </summary>
    public Result Approve(
        Guid appointmentId,
        Guid clientId,
        Guid petId,
        DateTime startUtc,
        Guid? veterinarianUserId,
        Guid userId,
        DateTime utcNow)
    {
        var pending = EnsurePending();
        if (!pending.IsSuccess)
            return pending;
        if (appointmentId == Guid.Empty || clientId == Guid.Empty || petId == Guid.Empty || userId == Guid.Empty)
            return Result.Failure("AppointmentRequests.Validation", "Randevu, müşteri ve hayvan zorunludur.");

        AppointmentId = appointmentId;
        ClientId = clientId;
        PetId = petId;
        ApprovedStartUtc = ToUtc(startUtc);
        ApprovedVeterinarianUserId = veterinarianUserId;
        WasRescheduled = ApprovedStartUtc != RequestedStartUtc;
        DecidedByUserId = userId;
        Transition(AppointmentRequestStatus.Approved, AppointmentRequestActorKind.Staff, userId, WasRescheduled ? "Farklı saatle onaylandı" : null, utcNow);
        return Result.Success();
    }

    /// <summary>Red: gerekçe zorunlu ve sahibe gider.</summary>
    public Result Reject(string? reason, Guid userId, DateTime utcNow)
    {
        var pending = EnsurePending();
        if (!pending.IsSuccess)
            return pending;

        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length < RejectionReasonMinLength || trimmed.Length > RejectionReasonMaxLength)
        {
            return Result.Failure(
                "AppointmentRequests.Validation",
                $"Red gerekçesi {RejectionReasonMinLength}-{RejectionReasonMaxLength} karakter olmalıdır.");
        }

        RejectionReason = trimmed;
        DecidedByUserId = userId;
        Transition(AppointmentRequestStatus.Rejected, AppointmentRequestActorKind.Staff, userId, trimmed, utcNow);
        return Result.Success();
    }

    /// <summary>Sahip vazgeçti; yalnız bekleyen talep.</summary>
    public Result CancelByOwner(DateTime utcNow)
    {
        var pending = EnsurePending();
        if (!pending.IsSuccess)
            return pending;

        Transition(AppointmentRequestStatus.Cancelled, AppointmentRequestActorKind.Owner, null, null, utcNow);
        return Result.Success();
    }

    /// <summary>İstenen saat geçmiş bekleyen talebi kapatır; aksi halde değişiklik yok (<c>false</c>).</summary>
    public bool ExpireIfDue(DateTime utcNow)
    {
        if (!IsPending || RequestedStartUtc > ToUtc(utcNow))
            return false;

        Transition(AppointmentRequestStatus.Expired, AppointmentRequestActorKind.System, null, null, utcNow);
        return true;
    }

    public Result EnsurePending()
        => IsPending
            ? Result.Success()
            : Result.Failure("AppointmentRequests.NotPending", "Talep artık beklemede değil.");

    private static Result<AppointmentRequest> Create(
        Guid tenantId,
        Guid clinicId,
        AppointmentRequestSource source,
        Guid? ownerAccountId,
        Guid? clientId,
        Guid? petId,
        PetText? petText,
        AppointmentReason reason,
        DateTime requestedStartUtc,
        string contactName,
        string contactEmail,
        string? contactPhone,
        string? note,
        DateTime utcNow,
        AppointmentRequestActorKind actor)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty)
            return Invalid("Kiracı ve klinik zorunludur.");
        if (reason is null || reason.ClinicId != clinicId || reason.TenantId != tenantId)
            return Invalid("Randevu nedeni bu kliniğe ait değil.");
        if (petId == Guid.Empty)
            return Invalid("Hayvan geçersiz.");

        var name = contactName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > ContactNameMaxLength)
            return Invalid($"Ad soyad 1-{ContactNameMaxLength} karakter olmalıdır.");

        var email = NormalizeEmail(contactEmail);
        if (email is null || email.Length > EmailMaxLength)
            return Invalid("E-posta geçersiz.");

        // Telefon müşteri kaydıyla aynı kuralla saklanır (Türkiye cep telefonu); onayda müşteri eşleştirmesi bu değerle yapılır.
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(contactPhone))
        {
            if (!TurkishMobilePhone.TryNormalize(contactPhone, out var normalizedPhone) || normalizedPhone is null)
                return Invalid("Geçersiz Türkiye cep telefonu.");
            phone = normalizedPhone;
        }

        var trimmedNote = Optional(note);
        if (trimmedNote is { Length: > NoteMaxLength })
            return Invalid($"Not en fazla {NoteMaxLength} karakter olabilir.");

        string? petName = null, petSpecies = null, petBreed = null;
        if (petId is null)
        {
            petName = Optional(petText?.Name);
            petSpecies = Optional(petText?.Species);
            petBreed = Optional(petText?.Breed);
            if (petName is null)
                return Invalid("Hayvan seçilmeli ya da hayvan adı yazılmalıdır.");
            if (petName.Length > PetNameMaxLength || petSpecies is { Length: > PetSpeciesMaxLength } || petBreed is { Length: > PetBreedMaxLength })
                return Invalid("Hayvan bilgisi çok uzun.");
        }

        var request = new AppointmentRequest
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Source = source,
            Status = AppointmentRequestStatus.Pending,
            ReasonId = reason.Id,
            ReasonName = reason.Name,
            RequestedStartUtc = ToUtc(requestedStartUtc),
            DurationMinutes = reason.DefaultDurationMinutes,
            OwnerAccountId = ownerAccountId,
            ClientId = clientId,
            PetId = petId,
            ContactName = name,
            ContactEmail = email,
            ContactPhone = phone,
            PetName = petName,
            PetSpecies = petSpecies,
            PetBreed = petBreed,
            OwnerNote = trimmedNote,
            CreatedAtUtc = ToUtc(utcNow),
        };
        request._history.Add(new AppointmentRequestStatusChange(null, AppointmentRequestStatus.Pending, actor, null, null, ToUtc(utcNow)));
        return Result<AppointmentRequest>.Success(request);
    }

    private void Transition(
        AppointmentRequestStatus to,
        AppointmentRequestActorKind actor,
        Guid? actorUserId,
        string? note,
        DateTime utcNow)
    {
        var at = ToUtc(utcNow);
        _history.Add(new AppointmentRequestStatusChange(Status, to, actor, actorUserId, note, at));
        Status = to;
        ClosedAtUtc = at;
    }

    public static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim();
        return string.IsNullOrEmpty(trimmed) || !trimmed.Contains('@') ? null : trimmed.ToLowerInvariant();
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<AppointmentRequest> Invalid(string message)
        => Result<AppointmentRequest>.Failure("AppointmentRequests.Validation", message);

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

/// <summary>Kayıtlı olmayan hayvan için sahibin yazdığı bilgi (APPT-004); klinik kaydı onayda oluşturur.</summary>
public sealed record PetText(string? Name, string? Species, string? Breed);

/// <summary>Talep durum geçişi (APPT-024 asgarisi): önceki/yeni durum, kim, ne zaman, not.</summary>
public sealed class AppointmentRequestStatusChange
{
    public AppointmentRequestStatus? FromStatus { get; private set; }
    public AppointmentRequestStatus ToStatus { get; private set; }
    public AppointmentRequestActorKind ActorKind { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string? Note { get; private set; }
    public DateTime AtUtc { get; private set; }

    private AppointmentRequestStatusChange() { }

    internal AppointmentRequestStatusChange(
        AppointmentRequestStatus? fromStatus,
        AppointmentRequestStatus toStatus,
        AppointmentRequestActorKind actorKind,
        Guid? actorUserId,
        string? note,
        DateTime atUtc)
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorKind = actorKind;
        ActorUserId = actorUserId;
        Note = note;
        AtUtc = atUtc;
    }
}
