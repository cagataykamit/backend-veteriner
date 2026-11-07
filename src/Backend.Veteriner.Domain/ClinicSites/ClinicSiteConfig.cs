using System.Globalization;
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
/// <item>Kök nesne, <c>schemaVersion</c> (≥ 1) taşır; boyut, derinlik, düğüm sayısı ve alan adı uzunluğu sınırlıdır.</item>
/// <item>Her metin değerinde <c>javascript:</c>, <c>vbscript:</c>, <c>data:</c> başlangıcı ve izinsiz <c>şema://</c> reddedilir.</item>
/// <item>Bağlantı alanında (ad kuralı: <see cref="IsLinkKey"/>) yalnız <c>http</c>, <c>https</c>, <c>tel</c>, <c>mailto</c> ya da şemasız değer; biçim karakteri yok.</item>
/// <item>Görsel alanında (ad kuralı: <see cref="IsImageKey"/>) yalnız <c>site-image:{kimlik}</c> ya da <c>stock:{anahtar}</c>; üçüncü taraf adresi yok (K7).</item>
/// <item>Klinik kaydından okunan alanlar ve adres yapılandırmada bulunamaz (K8/K10).</item>
/// </list>
/// Görselin bu siteye ait olduğunu uygulama katmanı <see cref="ImageIds"/> üzerinden denetler. Denetim belgeden çok bellek
/// üretmesin diye yol metni yalnız hata anında kurulur ve sorun sınırına gelince tarama durur.
/// </summary>
public sealed class ClinicSiteConfig
{
    public const int MaxBytes = 1_000_000;
    public const int MaxDepth = 32;
    public const int MaxNodes = 100_000;
    public const int MaxPropertyNameLength = 200;
    public const int MaxReportedIssues = 20;
    public const string ValidationCode = "ClinicSites.Validation";
    public const string SchemaVersionKey = "schemaVersion";
    public const string ImageReferencePrefix = "site-image:";
    public const string StockReferencePrefix = "stock:";

    public static class Reasons
    {
        public const string InvalidJson = "invalidJson";
        public const string TooLarge = "tooLarge";
        public const string TooComplex = "tooComplex";
        public const string SchemaVersionRequired = "schemaVersionRequired";
        public const string LinkSchemeNotAllowed = "linkSchemeNotAllowed";
        public const string ImageReferenceNotAllowed = "imageReferenceNotAllowed";
        public const string ClinicRecordField = "clinicRecordField";
        public const string ImageNotInSite = "imageNotInSite";
    }

    private const string RootPath = "$";

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

    /// <summary>Belgede değeri <c>site-image:</c> ile başlayan tüm başvurular.</summary>
    public IReadOnlySet<Guid> ImageIds { get; }

    public static string ImageReference(Guid imageId) => $"{ImageReferencePrefix}{imageId:D}";

    public static Result<ClinicSiteConfig> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail(RootPath, Reasons.InvalidJson);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
            return Fail(RootPath, Reasons.TooLarge);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            return Fail(RootPath, Reasons.InvalidJson);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail(RootPath, Reasons.InvalidJson);

            var walker = new Walker();
            int schemaVersion;
            try
            {
                schemaVersion = ReadSchemaVersion(root);
                if (schemaVersion < 1)
                    walker.Report(new ClinicSiteConfigIssue(SchemaVersionKey, Reasons.SchemaVersionRequired));

                CheckRecordOwnedFields(root, walker);
                walker.Visit(root, null);
            }
            catch (InvalidOperationException)
            {
                // JSON sözdizimi geçerli ama metne çözülemeyen kaçış (ör. eşsiz vekil "\uD800").
                return Fail(RootPath, Reasons.InvalidJson);
            }

            return walker.Issues.Count == 0
                ? Result<ClinicSiteConfig>.Success(new ClinicSiteConfig(json, schemaVersion, walker.ImageIds))
                : Result<ClinicSiteConfig>.Failure(ValidationError(walker.Issues));
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

    private static Result<ClinicSiteConfig> Fail(string path, string reason)
        => Result<ClinicSiteConfig>.Failure(ValidationError([new ClinicSiteConfigIssue(path, reason)]));

    private static string Describe(string reason) => reason switch
    {
        Reasons.InvalidJson => "geçerli bir JSON nesnesi değil",
        Reasons.TooLarge => $"boyut sınırı aşıldı (en çok {MaxBytes / 1000} KB)",
        Reasons.TooComplex => "belge çok karmaşık (alan sayısı ya da alan adı uzunluğu sınırı aşıldı)",
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

    private static void CheckRecordOwnedFields(JsonElement root, Walker walker)
    {
        foreach (var (section, child, keys) in RecordOwnedFields)
        {
            if (!root.TryGetProperty(section, out var parent) || parent.ValueKind != JsonValueKind.Object)
                continue;

            var path = $"{RootPath}.{section}";
            if (child is not null)
            {
                if (!parent.TryGetProperty(child, out parent) || parent.ValueKind != JsonValueKind.Object)
                    continue;
                path = $"{path}.{child}";
            }

            foreach (var key in keys)
            {
                if (parent.TryGetProperty(key, out _))
                    walker.Report(new ClinicSiteConfigIssue($"{path}.{key}", Reasons.ClinicRecordField));
            }
        }
    }

    private static bool IsLinkKey(string key)
        => LinkKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
           || LinkKeySuffixes.Any(s => key.EndsWith(s, StringComparison.Ordinal));

    private static bool IsImageKey(string key)
        => ImageKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
           || key.EndsWith(ImageKeySuffix, StringComparison.Ordinal);

    /// <summary>
    /// Kaçış ve gizleme denemelerine karşı karşılaştırma biçimi: boşluk, denetim ve biçim (Unicode Cf, ör. U+FEFF, U+200B)
    /// karakterleri atılır, küçük harfe iner.
    /// </summary>
    private static string Compact(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune) && !Rune.IsControl(rune) && !IsFormat(rune))
                result.Append(Rune.ToLowerInvariant(rune).ToString());
        }

        return result.ToString();
    }

    private static bool IsFormat(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format;

    private static bool HasFormatCharacter(string value) => value.EnumerateRunes().Any(IsFormat);

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

    /// <summary>Ağaç taraması: yol parçaları yığında tutulur, metne yalnız sorun anında çevrilir.</summary>
    private sealed class Walker
    {
        private readonly List<(string? Name, int Index)> _path = [];
        private int _nodes;
        private bool _stopped;

        public List<ClinicSiteConfigIssue> Issues { get; } = [];

        public HashSet<Guid> ImageIds { get; } = [];

        public void Report(ClinicSiteConfigIssue issue)
        {
            Issues.Add(issue);
            if (Issues.Count >= MaxReportedIssues)
                _stopped = true;
        }

        public void Visit(JsonElement element, string? key)
        {
            if (_stopped)
                return;

            if (++_nodes > MaxNodes)
            {
                StopAsTooComplex();
                return;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    VisitObject(element);
                    break;
                case JsonValueKind.Array:
                    VisitArray(element, key);
                    break;
                case JsonValueKind.String:
                    CheckText(element.GetString()!, key);
                    break;
            }
        }

        private void VisitObject(JsonElement element)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (_stopped)
                    return;

                var name = property.Name;
                if (name.Length > MaxPropertyNameLength)
                {
                    StopAsTooComplex();
                    return;
                }

                _path.Add((name, 0));
                Visit(property.Value, name);
                _path.RemoveAt(_path.Count - 1);
            }
        }

        private void VisitArray(JsonElement element, string? key)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (_stopped)
                    return;

                _path.Add((null, index++));
                Visit(item, key);
                _path.RemoveAt(_path.Count - 1);
            }
        }

        private void CheckText(string value, string? key)
        {
            var compact = Compact(value);
            if (compact.Length == 0)
                return;

            if (compact.StartsWith(ImageReferencePrefix, StringComparison.Ordinal))
            {
                if (Guid.TryParse(compact[ImageReferencePrefix.Length..], out var imageId) && imageId != Guid.Empty)
                    ImageIds.Add(imageId);
                else
                    ReportHere(Reasons.ImageReferenceNotAllowed);
                return;
            }

            if (AlwaysRejectedSchemes.Any(s => compact.StartsWith(s, StringComparison.Ordinal)) || HasDisallowedAbsoluteScheme(compact))
            {
                ReportHere(Reasons.LinkSchemeNotAllowed);
                return;
            }

            if (key is null)
                return;

            if (IsImageKey(key))
            {
                if (!IsStockReference(compact) || HasFormatCharacter(value))
                    ReportHere(Reasons.ImageReferenceNotAllowed);
            }
            else if (IsLinkKey(key)
                     && (HasFormatCharacter(value) || (ReadScheme(compact) is { } scheme && !AllowedLinkSchemes.Contains(scheme))))
            {
                ReportHere(Reasons.LinkSchemeNotAllowed);
            }
        }

        private void ReportHere(string reason) => Report(new ClinicSiteConfigIssue(BuildPath(), reason));

        private void StopAsTooComplex()
        {
            Issues.Add(new ClinicSiteConfigIssue(RootPath, Reasons.TooComplex));
            _stopped = true;
        }

        private string BuildPath()
        {
            var path = new StringBuilder(RootPath);
            foreach (var (name, index) in _path)
            {
                if (name is null)
                    path.Append('[').Append(index).Append(']');
                else
                    path.Append('.').Append(name);
            }

            return path.ToString();
        }
    }
}
