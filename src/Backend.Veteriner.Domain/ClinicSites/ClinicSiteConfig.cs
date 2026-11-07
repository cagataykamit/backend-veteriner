using System.Text;
using System.Text.Json;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClinicSites;

/// <summary>Yapılandırma denetiminde bulunan sorun: JSON yolu ve makine okur neden.</summary>
public sealed record ClinicSiteConfigIssue(string Path, string Reason);

/// <summary>
/// Site yapılandırması (ADR-045 K3/K8/K14): sihirbazın ürettiği, motorun çizdiği JSON belge. Backend tasarım alanlarını
/// yorumlamaz; yalnız güvenlik sınırlarını ağaç üzerinde genel kurallarla denetler, böylece yeni tasarım paketi backend
/// değişikliği istemez:
/// <list type="bullet">
/// <item>Kök nesne, <c>schemaVersion</c> (≥ 1) taşır, boyutu <see cref="MaxBytes"/> ile sınırlıdır.</item>
/// <item>Her metin değerinde <c>javascript:</c>, <c>vbscript:</c>, <c>data:</c> başlangıcı ve izinsiz <c>şema://</c> reddedilir.</item>
/// <item>Bağlantı alanında (ad kuralı: <see cref="IsLinkKey"/>) yalnız <c>http</c>, <c>https</c>, <c>tel</c>, <c>mailto</c> ya da şemasız değer.</item>
/// <item>Görsel alanında (ad kuralı: <see cref="IsImageKey"/>) yalnız <c>site-image:{kimlik}</c> ya da <c>stock:{anahtar}</c>; üçüncü taraf adresi yok (K7).</item>
/// <item>Klinik kaydından okunan alanlar ve adres yapılandırmada bulunamaz (K8/K10).</item>
/// </list>
/// Görselin bu siteye ait olduğunu uygulama katmanı <see cref="ImageIds"/> üzerinden denetler.
/// </summary>
public sealed class ClinicSiteConfig
{
    public const int MaxBytes = 1_000_000;
    public const int MaxDepth = 32;
    public const int MaxReportedIssues = 20;
    public const string ValidationCode = "ClinicSites.Validation";
    public const string SchemaVersionKey = "schemaVersion";
    public const string ImageReferencePrefix = "site-image:";
    public const string StockReferencePrefix = "stock:";

    public static class Reasons
    {
        public const string InvalidJson = "invalidJson";
        public const string TooLarge = "tooLarge";
        public const string SchemaVersionRequired = "schemaVersionRequired";
        public const string LinkSchemeNotAllowed = "linkSchemeNotAllowed";
        public const string ImageReferenceNotAllowed = "imageReferenceNotAllowed";
        public const string ClinicRecordField = "clinicRecordField";
        public const string ImageNotInSite = "imageNotInSite";
    }

    private static readonly string[] AlwaysRejectedSchemes = ["javascript:", "vbscript:", "data:"];
    private static readonly string[] AllowedLinkSchemes = ["http", "https", "tel", "mailto"];
    private static readonly string[] AllowedAbsoluteSchemes = ["http", "https"];

    private static readonly string[] LinkKeys =
        ["href", "url", "link", "website", "instagram", "facebook", "twitter", "youtube", "linkedin", "tiktok"];
    private static readonly string[] LinkKeySuffixes = ["Url", "Href", "Link"];
    private static readonly string[] ImageKeys = ["src", "logo", "favicon", "image"];
    private const string ImageKeySuffix = "Image";

    /// <summary>Klinik kaydında ya da site kaydında yaşayan alanlar (K8, K10): yapılandırmaya kopyalanmaz.</summary>
    private static readonly (string Section, string? Child, string[] Keys)[] RecordOwnedFields =
    [
        ("content", "clinic", ["name", "phone", "email", "address", "district", "city", "workingDays", "workingHours"]),
        ("seo", null, ["slug"]),
    ];

    private ClinicSiteConfig(string json, int schemaVersion, IReadOnlySet<Guid> imageIds)
    {
        Json = json;
        SchemaVersion = schemaVersion;
        ImageIds = imageIds;
    }

    public string Json { get; }

    public int SchemaVersion { get; }

    /// <summary>Belgede geçen tüm <c>site-image:</c> başvuruları.</summary>
    public IReadOnlySet<Guid> ImageIds { get; }

    public static string ImageReference(Guid imageId) => $"{ImageReferencePrefix}{imageId:D}";

    public static Result<ClinicSiteConfig> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail([new ClinicSiteConfigIssue("$", Reasons.InvalidJson)]);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
            return Fail([new ClinicSiteConfigIssue("$", Reasons.TooLarge)]);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            return Fail([new ClinicSiteConfigIssue("$", Reasons.InvalidJson)]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail([new ClinicSiteConfigIssue("$", Reasons.InvalidJson)]);

            var issues = new List<ClinicSiteConfigIssue>();
            var imageIds = new HashSet<Guid>();

            var schemaVersion = ReadSchemaVersion(root);
            if (schemaVersion < 1)
                issues.Add(new ClinicSiteConfigIssue(SchemaVersionKey, Reasons.SchemaVersionRequired));

            CheckRecordOwnedFields(root, issues);
            Visit(root, "$", null, issues, imageIds);

            return issues.Count == 0
                ? Result<ClinicSiteConfig>.Success(new ClinicSiteConfig(json, schemaVersion, imageIds))
                : Fail(issues);
        }
    }

    /// <summary>Anlaşılır doğrulama hatası: <c>issues</c> uzantısı yol ve nedeni taşır.</summary>
    public static Error ValidationError(IReadOnlyCollection<ClinicSiteConfigIssue> issues)
    {
        var reported = issues.Take(MaxReportedIssues).ToArray();
        var first = reported[0];
        return new Error(
            ValidationCode,
            $"Site yapılandırması geçersiz: {Describe(first.Reason)} ({first.Path}).",
            new Dictionary<string, object?> { ["issues"] = reported });
    }

    private static Result<ClinicSiteConfig> Fail(IReadOnlyCollection<ClinicSiteConfigIssue> issues)
        => Result<ClinicSiteConfig>.Failure(ValidationError(issues));

    private static string Describe(string reason) => reason switch
    {
        Reasons.InvalidJson => "geçerli bir JSON nesnesi değil",
        Reasons.TooLarge => $"boyut sınırı aşıldı (en çok {MaxBytes / 1000} KB)",
        Reasons.SchemaVersionRequired => "schemaVersion zorunludur ve 1 ya da üstü olmalıdır",
        Reasons.LinkSchemeNotAllowed => "bağlantı yalnız http, https, tel ya da mailto olabilir",
        Reasons.ImageReferenceNotAllowed => "görsel yalnız bu sitenin görseli ya da hazır fotoğraf olabilir",
        Reasons.ClinicRecordField => "bu alan klinik kaydından okunur, yapılandırmaya yazılmaz",
        Reasons.ImageNotInSite => "görsel bu siteye ait değil",
        _ => reason,
    };

    private static int ReadSchemaVersion(JsonElement root)
        => root.TryGetProperty(SchemaVersionKey, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var version)
            ? version
            : 0;

    private static void CheckRecordOwnedFields(JsonElement root, List<ClinicSiteConfigIssue> issues)
    {
        foreach (var (section, child, keys) in RecordOwnedFields)
        {
            if (!root.TryGetProperty(section, out var parent) || parent.ValueKind != JsonValueKind.Object)
                continue;

            var path = $"$.{section}";
            if (child is not null)
            {
                if (!parent.TryGetProperty(child, out parent) || parent.ValueKind != JsonValueKind.Object)
                    continue;
                path = $"{path}.{child}";
            }

            foreach (var key in keys)
            {
                if (parent.TryGetProperty(key, out _))
                    issues.Add(new ClinicSiteConfigIssue($"{path}.{key}", Reasons.ClinicRecordField));
            }
        }
    }

    private static void Visit(JsonElement element, string path, string? key, List<ClinicSiteConfigIssue> issues, HashSet<Guid> imageIds)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Visit(property.Value, $"{path}.{property.Name}", property.Name, issues, imageIds);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Visit(item, $"{path}[{index++}]", key, issues, imageIds);
                break;
            case JsonValueKind.String:
                CheckText(element.GetString()!, path, key, issues, imageIds);
                break;
        }
    }

    private static void CheckText(string value, string path, string? key, List<ClinicSiteConfigIssue> issues, HashSet<Guid> imageIds)
    {
        var compact = Compact(value);
        if (compact.Length == 0)
            return;

        if (compact.StartsWith(ImageReferencePrefix, StringComparison.Ordinal))
        {
            if (Guid.TryParse(compact[ImageReferencePrefix.Length..], out var imageId) && imageId != Guid.Empty)
                imageIds.Add(imageId);
            else
                issues.Add(new ClinicSiteConfigIssue(path, Reasons.ImageReferenceNotAllowed));
            return;
        }

        if (AlwaysRejectedSchemes.Any(s => compact.StartsWith(s, StringComparison.Ordinal)) || HasDisallowedAbsoluteScheme(compact))
        {
            issues.Add(new ClinicSiteConfigIssue(path, Reasons.LinkSchemeNotAllowed));
            return;
        }

        if (key is null)
            return;

        if (IsImageKey(key))
        {
            if (!IsStockReference(compact))
                issues.Add(new ClinicSiteConfigIssue(path, Reasons.ImageReferenceNotAllowed));
        }
        else if (IsLinkKey(key) && ReadScheme(compact) is { } scheme && !AllowedLinkSchemes.Contains(scheme))
        {
            issues.Add(new ClinicSiteConfigIssue(path, Reasons.LinkSchemeNotAllowed));
        }
    }

    private static bool IsLinkKey(string key)
        => LinkKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
           || LinkKeySuffixes.Any(s => key.EndsWith(s, StringComparison.Ordinal));

    private static bool IsImageKey(string key)
        => ImageKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
           || key.EndsWith(ImageKeySuffix, StringComparison.Ordinal);

    /// <summary>Kaçış ve gizleme denemelerine karşı: tarayıcının yok saydığı boşluk ve denetim karakterleri atılır, küçük harfe iner.</summary>
    private static string Compact(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch) && !char.IsControl(ch))
                result.Append(char.ToLowerInvariant(ch));
        }

        return result.ToString();
    }

    private static bool HasDisallowedAbsoluteScheme(string compact)
        => compact.Contains("://", StringComparison.Ordinal)
           && ReadScheme(compact) is { } scheme
           && compact.StartsWith($"{scheme}://", StringComparison.Ordinal)
           && !AllowedAbsoluteSchemes.Contains(scheme);

    private static string? ReadScheme(string compact)
    {
        var colon = compact.IndexOf(':');
        if (colon <= 0 || compact[0] is < 'a' or > 'z')
            return null;

        for (var i = 1; i < colon; i++)
        {
            if (compact[i] is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '.' or '-'))
                return null;
        }

        return compact[..colon];
    }

    private static bool IsStockReference(string compact)
    {
        if (!compact.StartsWith(StockReferencePrefix, StringComparison.Ordinal))
            return false;

        var stockKey = compact[StockReferencePrefix.Length..];
        return stockKey.Length > 0
               && !stockKey.Contains("..", StringComparison.Ordinal)
               && stockKey.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '/' or '.');
    }
}
