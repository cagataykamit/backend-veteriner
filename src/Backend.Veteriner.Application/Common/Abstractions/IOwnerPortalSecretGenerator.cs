namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>Portal davet/yenileme jetonu ve tek kullanımlık kod üretimi (kriptografik rastgele).</summary>
public interface IOwnerPortalSecretGenerator
{
    /// <summary>URL güvenli, 256 bit jeton.</summary>
    string CreateToken();

    /// <summary>Verilen uzunlukta, eşit dağılımlı rakam dizisi.</summary>
    string CreateNumericCode(int length);
}
