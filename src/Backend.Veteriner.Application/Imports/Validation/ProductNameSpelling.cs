using System.Globalization;
using System.Text.RegularExpressions;

namespace Backend.Veteriner.Application.Imports.Validation;

/// <summary>
/// Ürün adındaki noktasız ı onarımı (IMPORT_API_CONTRACT.md §12.3, 2026-11-23). Kaynak program büyük harfle ve İngilizce klavyeyle
/// yazılmış adı ("MAVI", "FLEXI") Türkçe kurala göre küçültünce "Mavı", "Flexı" olur. Yalnız küçük ı içeren ve sözlükte bulunan kelime
/// düzeltilir; rakam, noktalama ve harflerin büyük/küçük biçimi korunur. Doğru Türkçe kelimeler ("Açık", "Kırmızı") sözlükte yoktur.
/// Aktarma (ürün adı) ve kayıtlı adların onarımı (DbMigrator <c>fix-product-names</c>) bu sınıfı kullanır; kural yalnız burada yaşar.
/// </summary>
public static class ProductNameSpelling
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// Sözlükte olmayan ama Türkçe kelimeye benzemeyen ı: ünlüye bitişik ("bıo", "haır"), q/w/x içeren ya da İngilizce ekle biten
    /// ("…ıne", "…ıc", "…ıng"). Gerçek veride doğru Türkçe kelimede eşleşmez; kaçırdığı bozuk kelime olabilir (uyarı yalnız ipucudur).
    /// </summary>
    private static readonly Regex ForeignShape = new(
        @"ı[aeıioöuü]|[aeioöuü]ı|[qwx]|ı(ne|c|ty|on|ng|ght|cks?)$", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// Doğru yazımlar (bir E-vet kliniğinin ürün listesinden, 2026-11-23). Bozuk biçim yazılmaz: kelimenin noktasız anahtarı
    /// (<see cref="DotlessKey"/>) eşleşmeyi verir. Marka ve ilaç adları kaynaktaki yazımıyla ("Desing", "Orignal") durur.
    /// </summary>
    private static readonly string[] Corrections =
    [
        "Active", "Activflex", "Agail", "Ageing", "Alizin", "Allergic", "Amitrax", "Amoksil", "Amoksivet", "Amoxynil",
        "Anallergenic", "Anamicine", "Anti", "Antisedan", "Atopic", "Atropin", "Bactimox", "Basilazin", "Baytril", "Bekombin",
        "Berovit", "Bevit", "Bialin", "Bialis", "Bikarvil", "Bio", "Bionote", "Biotrin", "Biyoline", "Biyomec", "Biyoteknik",
        "Biyoteksin", "British", "Caffeine", "Calcicaf", "Calcicure", "Calciject", "Calcimix", "Calcine", "Calsine", "Canin",
        "Canine", "Canirex", "Cardiac", "Cardiobalans", "Carni", "Carnivet", "Cazitel", "Cefasid", "Cenmicin", "Cerenia",
        "Certifect", "Cesamolin", "Cevit", "Chicken", "City", "Classic", "Cloteid", "Collection", "Combevit", "Combiotic",
        "Combipet", "Compositum", "Compovit", "Comprehensive", "Controline", "Cortizeme", "Dekstrovip", "Deli", "Delicate",
        "Delice", "Delight", "Delights", "Deposilin", "Deri", "Design", "Desing", "Diabetic", "Diet", "Dimer", "Droncit",
        "Edit", "Engemycine", "Exigent", "Fashion", "Feline", "Felix", "Fibroline", "Fip", "Fiprex", "Fiproes", "Fiprojin",
        "Fiprovet", "Fiproxin", "Fit", "Fitadinon", "Fiv", "Flamingo", "Flexi", "Floksisem", "Flumeglin", "Friskies",
        "Frontline", "Fulimed", "Fulinaljin", "Fundamin", "Funtime", "Gabiotan", "Gentamis", "Gentasamix", "Gentavilin", "Gi",
        "Giant", "Glucosamine", "Granit", "Gri", "Hair", "Hairball", "Hepatic", "Hepatonik", "Hepavita", "Hi", "Hip",
        "Histavet", "Histeral", "Hypoallergenic", "Imida", "Imidamix", "Immushield", "Instinctive", "Intestinal", "İkili", "in",
        "Jnragail", "Joint", "Junior", "Kafemin", "Kalsimin", "Katovil", "Kattovit", "Kedi", "Kenecid", "Kidney", "Kiltix",
        "Kitten", "Klavil", "Lacristal", "Ladies", "Lagrinet", "Light", "Limited", "Liq", "Liquid", "Liver", "Luquid",
        "Magnesie", "Mai", "Mandibula", "Mandibular", "Mandibulektomi", "Mavi", "Maxi", "Maxicam", "Maxillar", "Maxisilin",
        "Maxivit", "Medium", "Meloxidyl", "Metajin", "Metalgine", "Metrizin", "Micostop", "Microbagent", "Mikogen", "Mini",
        "Mmavi", "Mobility", "Multibox", "Multivitamin", "Nervit", "Norixin", "Novamizol", "Obesity", "Oksitosin", "Onleyic",
        "Opticlox", "Optivil", "Oridermyl", "Original", "Orignal", "Otimix", "Otrizol", "Paisley", "Pediat", "Pediatric",
        "Pentamycin", "Persian", "Pink", "Prazitel", "Prazivet", "Presurgical", "Primamycin", "Primavilin", "Pumkkin", "Rice",
        "Ridx", "Rilexine", "Rimadyl", "Ringer", "Ringesol", "Riv", "Rivanol", "Rumix", "Safari", "Salisil", "Satiety", "Sebil",
        "Sensible", "Serit", "Shih", "Shorthair", "Siamese", "Silvezin", "Sistemi", "Siyah", "Skin", "Spider", "Spiramet",
        "Spotline", "Sterilisd", "Sterilised", "Sterilisedsnrcat", "Sticks", "Stripes", "Symphisis", "Synpivet", "Tbil",
        "Tenidur", "Tenikur", "Tenisil", "Tenizol", "Tenyasid", "Tetramezatin", "Tetramisin", "Tetroksil", "Tifrol", "Til",
        "Tilmicos", "Tilmicure", "Tilocen", "Tiofur", "Tioned", "Tolfedine", "Tomidin", "Tricopom", "Tridox", "Trikofit",
        "Trimbol", "Triprim", "Triwalent", "Twisted", "Urinary", "Vari", "Vario", "Vdiet", "Vetimisin", "Vigantol", "Viks",
        "Vit", "Vita", "Vitamin", "Vitbe", "Vitce", "Vitoftal", "Viusid", "Weaning", "Weight", "Wings", "Yesil", "Zinc",
        "Zoletil",
    ];

    private static readonly Dictionary<string, string> ByDotlessKey = Corrections.ToDictionary(DotlessKey, StringComparer.Ordinal);

    /// <summary>Sözlükteki bozuk kelimeleri düzeltir; diğer her karakter olduğu gibi kalır. Tekrar uygulamak sonucu değiştirmez.</summary>
    public static string Correct(string name) => Word.Replace(name, m => CorrectWord(m.Value));

    /// <summary>Düzeltilmiş adda kalan, Türkçe olmayan biçimli ı'lı kelimeler (önizleme uyarısı için); yoksa boş.</summary>
    public static IReadOnlyList<string> SuspiciousWords(string correctedName)
        => Word.Matches(correctedName)
            .Select(m => m.Value)
            .Where(w => w.Contains('ı') && ForeignShape.IsMatch(w.ToLower(Turkish)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string CorrectWord(string word)
    {
        if (!word.Contains('ı') || !ByDotlessKey.TryGetValue(DotlessKey(word), out var correct))
            return word;

        // Yalnız ı/i farkı olan konum değişir; büyük harf Türkçe kuralla büyük kalır ("Ikılı" → "İkili").
        var chars = word.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var expected = char.ToLower(correct[i], Turkish);
            if (char.ToLower(chars[i], Turkish) != expected)
                chars[i] = char.IsUpper(chars[i]) ? char.ToUpper(expected, Turkish) : expected;
        }

        return new string(chars);
    }

    /// <summary>Küçük harf ve tüm i → ı: "Mavi", "Mavı", "MAVı" aynı anahtara düşer.</summary>
    private static string DotlessKey(string word) => word.ToLower(Turkish).Replace('i', 'ı');
}
