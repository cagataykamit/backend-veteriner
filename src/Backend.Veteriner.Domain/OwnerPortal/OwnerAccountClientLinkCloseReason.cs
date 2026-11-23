namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>Kapalı bağın kapanma gerekçesi (ADR-041 K4); aktif bağda yoktur.</summary>
public enum OwnerAccountClientLinkCloseReason
{
    /// <summary>Klinik personeli erişimi kapattı.</summary>
    ClinicClosed = 0,

    /// <summary>Hasta sahibi portal hesabını sildi.</summary>
    OwnerAccountDeleted = 1,

    /// <summary>Klinik müşteriyi sildi (ADR-054 K9); geri yükleme bağı açmaz.</summary>
    ClientDeleted = 2,
}
