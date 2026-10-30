namespace Backend.Veteriner.Domain.AppointmentRequests;

/// <summary>Talep yaşam döngüsü (ADR-042 K4). Sayısal değerler stabildir.</summary>
public enum AppointmentRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,

    /// <summary>Sahip vazgeçti.</summary>
    Cancelled = 3,

    /// <summary>İstenen saat geçti; arka plan işi kapattı.</summary>
    Expired = 4,
}

/// <summary>Talebin giriş kapısı (ADR-042 K1).</summary>
public enum AppointmentRequestSource
{
    /// <summary>Oturum açmış hasta sahibi (portal).</summary>
    Portal = 0,

    /// <summary>Kliniğe özel bağlantı; e-posta koduyla doğrulanmış kimliksiz kişi.</summary>
    Link = 1,
}

/// <summary>Durum geçişini yapan.</summary>
public enum AppointmentRequestActorKind
{
    Owner = 0,
    Staff = 1,
    System = 2,
}
