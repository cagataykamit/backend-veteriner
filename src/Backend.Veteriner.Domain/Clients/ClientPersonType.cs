namespace Backend.Veteriner.Domain.Clients;

/// <summary>Fatura alıcısı türü (ADR-036); müşteride <c>null</c> = belirtilmemiş.</summary>
public enum ClientPersonType
{
    /// <summary>Bireysel: kimlik numarası TCKN.</summary>
    Individual = 1,

    /// <summary>Kurumsal: VKN, vergi dairesi ve unvan.</summary>
    Corporate = 2
}
