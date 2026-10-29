using PdfSharp.Fonts;

namespace Backend.Veteriner.Application.Reports.Pdf;

/// <summary>
/// PDF yazı tipi: sunucuda kurulu, Türkçe harfleri (ç, ğ, ı, İ, ö, ş, ü) kapsayan bir sans-serif. Yazı tipi dosyası depoya
/// konmaz (lisans); sırasıyla Windows Arial, Linux DejaVu Sans / Liberation Sans, macOS Arial aranır. Bulunamazsa PDF üretimi
/// açık hatayla durur (CSV/XLSX etkilenmez). PDFsharp yazı tipi çözücüsü süreç genelidir; bir kez kurulur.
/// </summary>
internal static class ReportPdfFonts
{
    public const string FamilyName = "VetinityReport";

    private static readonly Lazy<bool> Configured = new(Configure, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Uygun yazı tipi bulundu ve PDFsharp'a bağlandı mı.</summary>
    public static bool IsAvailable => Configured.Value;

    public static void EnsureConfigured()
    {
        if (!Configured.Value)
        {
            throw new InvalidOperationException(
                "PDF için Türkçe karakter destekli yazı tipi bulunamadı (Windows: Arial; Linux: DejaVu Sans veya Liberation Sans).");
        }
    }

    private static bool Configure()
    {
        var resolver = SystemFontResolver.TryCreate();
        if (resolver is null)
            return false;

        GlobalFontSettings.FontResolver = resolver;
        return true;
    }

    private sealed class SystemFontResolver : IFontResolver
    {
        private const string RegularFace = FamilyName + "#R";
        private const string BoldFace = FamilyName + "#B";

        private readonly byte[] _regular;
        private readonly byte[]? _bold;

        private SystemFontResolver(byte[] regular, byte[]? bold)
        {
            _regular = regular;
            _bold = bold;
        }

        public static SystemFontResolver? TryCreate()
        {
            foreach (var (regular, bold) in Candidates())
            {
                if (!File.Exists(regular))
                    continue;

                return new SystemFontResolver(File.ReadAllBytes(regular), File.Exists(bold) ? File.ReadAllBytes(bold) : null);
            }

            return null;
        }

        /// <summary>Belgede tek aile kullanılır; MigraDoc başka aile isterse de aynı yazı tipi döner.</summary>
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var hasBold = bold && _bold is not null;
            return new FontResolverInfo(hasBold ? BoldFace : RegularFace, mustSimulateBold: bold && !hasBold, mustSimulateItalic: italic);
        }

        public byte[] GetFont(string faceName) => faceName == BoldFace && _bold is not null ? _bold : _regular;

        private static IEnumerable<(string Regular, string Bold)> Candidates()
        {
            var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(windowsFonts))
                yield return (Path.Combine(windowsFonts, "arial.ttf"), Path.Combine(windowsFonts, "arialbd.ttf"));

            foreach (var dir in new[] { "/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/dejavu", "/usr/share/fonts/TTF" })
                yield return (Path.Combine(dir, "DejaVuSans.ttf"), Path.Combine(dir, "DejaVuSans-Bold.ttf"));

            foreach (var dir in new[] { "/usr/share/fonts/truetype/liberation", "/usr/share/fonts/liberation-sans" })
                yield return (Path.Combine(dir, "LiberationSans-Regular.ttf"), Path.Combine(dir, "LiberationSans-Bold.ttf"));

            yield return ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf");
        }
    }
}
