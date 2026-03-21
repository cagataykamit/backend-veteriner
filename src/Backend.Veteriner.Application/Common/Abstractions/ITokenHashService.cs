namespace Backend.Veteriner.Application.Common.Abstractions;

public interface ITokenHashService
{
    /// <summary>Raw refresh token'� SHA-256 ile hash'ler (Base64Url).</summary>
    string ComputeSha256(string rawToken);
}
