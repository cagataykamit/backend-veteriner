using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Backend.Veteriner.Infrastructure.Notifications;

/// <summary>
/// FCM HTTP v1 kimliği: Firebase konsolundan indirilen hizmet hesabı JSON'u (MOBILE-003). Yapılandırma bölümü
/// <see cref="SectionName"/>; değerler user-secrets/ortam değişkeninde tutulur, depoya yazılmaz. Anahtarlar:
/// <c>ServiceAccountJson</c> (JSON içeriği) ya da <c>ServiceAccountFile</c> (dosya yolu); <c>ProjectId</c> isteğe bağlıdır (yoksa
/// JSON'daki <c>project_id</c>). Hiçbiri yoksa push yapılandırılmamış sayılır. Özel anahtar loglara sızmasın diye record değildir.
/// </summary>
public sealed class FcmServiceAccount
{
    public const string SectionName = "Push:Fcm";
    private const string DefaultTokenUri = "https://oauth2.googleapis.com/token";

    private FcmServiceAccount(string projectId, string clientEmail, string privateKeyPem, string tokenUri)
    {
        ProjectId = projectId;
        ClientEmail = clientEmail;
        PrivateKeyPem = privateKeyPem;
        TokenUri = tokenUri;
    }

    public string ProjectId { get; }
    public string ClientEmail { get; }
    public string PrivateKeyPem { get; }
    public string TokenUri { get; }

    /// <summary>
    /// Yapılandırmadan okur; anahtar yoksa <c>null</c>. Anahtar var ama geçersizse açılışta <see cref="InvalidOperationException"/>
    /// (yanlış yapılandırma sessizce no-op'a düşmez). Hata iletisi gizli değer içermez.
    /// </summary>
    public static FcmServiceAccount? Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var json = section["ServiceAccountJson"];
        var file = section["ServiceAccountFile"];
        if (string.IsNullOrWhiteSpace(json) && string.IsNullOrWhiteSpace(file))
            return null;

        if (string.IsNullOrWhiteSpace(json))
        {
            if (!File.Exists(file))
                throw new InvalidOperationException($"{SectionName}:ServiceAccountFile bulunamadı.");
            json = File.ReadAllText(file!);
        }

        return Parse(json, section["ProjectId"]);
    }

    public static FcmServiceAccount Parse(string json, string? projectIdOverride = null)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"{SectionName}: hizmet hesabı JSON'u okunamadı.");
        }

        using var _ = document;
        var root = document.RootElement;
        var projectId = string.IsNullOrWhiteSpace(projectIdOverride) ? Read(root, "project_id") : projectIdOverride.Trim();
        var clientEmail = Read(root, "client_email");
        var privateKey = Read(root, "private_key");
        var tokenUri = Read(root, "token_uri") ?? DefaultTokenUri;
        if (projectId is null || clientEmail is null || privateKey is null)
            throw new InvalidOperationException($"{SectionName}: hizmet hesabında project_id, client_email ve private_key zorunludur.");

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new InvalidOperationException($"{SectionName}: private_key geçerli bir RSA anahtarı değil.");
        }

        return new FcmServiceAccount(projectId, clientEmail, privateKey, tokenUri);
    }

    private static string? Read(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}
