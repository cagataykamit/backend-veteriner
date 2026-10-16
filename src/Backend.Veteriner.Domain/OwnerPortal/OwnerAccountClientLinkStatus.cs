namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>Portal hesabı ile müşteri kaydı arasındaki bağın durumu.</summary>
public enum OwnerAccountClientLinkStatus
{
    Active = 0,

    /// <summary>Klinik erişimi kapattı; bağ silinmez, portal bu kayda erişemez.</summary>
    Closed = 1,
}
