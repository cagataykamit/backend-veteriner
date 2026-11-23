using Backend.Veteriner.Application.Imports.Validation;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>Ürün adındaki noktasız ı onarımı (IMPORT_API_CONTRACT.md §12.3); saf fonksiyon.</summary>
public sealed class ProductNameSpellingTests
{
    [Theory]
    [InlineData("Flexı Classıc Mını Sıyah", "Flexi Classic Mini Siyah")]
    [InlineData("8In1 Delıghts Strong Xs", "8In1 Delights Strong Xs")]
    [InlineData("Royal Canın Vdıet Satıety Felıne 3,5Kg", "Royal Canin Vdiet Satiety Feline 3,5Kg")]
    [InlineData("Frıskıes Junıor Cat", "Friskies Junior Cat")] // bir kelimede birden çok ı
    [InlineData("Flexı Desıgn 5M Serıt M Grı", "Flexi Design 5M Serit M Gri")]
    public void Correct_Should_Fix_Dotless_I_In_Known_Words(string input, string expected)
        => ProductNameSpelling.Correct(input).Should().Be(expected);

    [Theory]
    [InlineData("Açık Paket Kırmızı Tasması")]
    [InlineData("Kısırlaştırılmış Kedi Maması Sarı")]
    [InlineData("Kıtık Açıcı Çift Taraflı")]
    [InlineData("Sadı Kıtık Açıcı")] // belirsiz: sözlükte yok
    [InlineData("N&D Natural Yaş Mama 6 Lı")]
    public void Correct_Should_Keep_Correct_Turkish_And_Unknown_Words(string input)
        => ProductNameSpelling.Correct(input).Should().Be(input);

    [Theory]
    [InlineData("Fıprex 10-20 Kg (Dış Parazit)", "Fiprex 10-20 Kg (Dış Parazit)")]
    [InlineData("Temporo-Mandıbular Çıkık", "Temporo-Mandibular Çıkık")]
    [InlineData("Flexı Lımıted Edıt.5M Şerit", "Flexi Limited Edit.5M Şerit")]
    [InlineData("Bıalın's Tavuk-Narlı", "Bialin's Tavuk-Narlı")]
    [InlineData("Vetexpro Hıp&Joınt", "Vetexpro Hip&Joint")]
    [InlineData("Kattovıt-Feline-Spezial", "Kattovit-Feline-Spezial")]
    public void Correct_Should_Work_On_Letters_And_Keep_Punctuation(string input, string expected)
        => ProductNameSpelling.Correct(input).Should().Be(expected);

    [Theory]
    [InlineData("8ın1 Delights", "8in1 Delights")]
    [InlineData("Flexı Varıo Ikılı Kayıs", "Flexi Vario İkili Kayıs")] // Türkçe kelimenin büyük I'sı İ olur
    [InlineData("Imıda 1Ml Çözelti", "Imida 1Ml Çözelti")] // yabancı adın büyük I'sı kalır
    [InlineData("flexı mavı", "flexi mavi")]
    public void Correct_Should_Preserve_Letter_Case(string input, string expected)
        => ProductNameSpelling.Correct(input).Should().Be(expected);

    [Theory]
    [InlineData("FLEXI MAVI")] // küçük ı yok: bozulma belirtisi yok
    [InlineData("Flexi Mavi")]
    public void Correct_Should_Leave_Words_Without_Lowercase_Dotless_I(string input)
        => ProductNameSpelling.Correct(input).Should().Be(input);

    [Theory]
    [InlineData("Emilebilir Ip 3/0 75Cm", "Emilebilir İp 3/0 75Cm")] // "IP" küçültülünce ilk harf I kalır
    [InlineData("Povidon Iyot %10 Solüsyon", "Povidon İyot %10 Solüsyon")]
    [InlineData("Potasyum Iyodür Tablet", "Potasyum İyodür Tablet")]
    [InlineData("Intrameduller Pin 2.0Mm", "İntrameduller Pin 2.0Mm")]
    [InlineData("Flexı Ikili Kayıs", "Flexi İkili Kayıs")]
    [InlineData("İpek Ip/Naylon", "İpek İp/Naylon")]
    public void Correct_Should_Fix_Capital_I_Of_Words_Spelled_With_Dotted_I(string input, string expected)
        => ProductNameSpelling.Correct(input).Should().Be(expected);

    [Theory]
    [InlineData("Led Lamba IP 65")] // tamamı büyük: kısaltma
    [InlineData("Irk Bilgisi Kartı")] // doğru Türkçe, sözlükte yok
    [InlineData("Irak Menşeli Ürün")]
    [InlineData("8In1 Delights Indoor")] // doğru yazımı I ya da i olan kelime
    [InlineData("Royal Canin Senior Indoor Sensitive Digestive 2Kg")]
    public void Correct_Should_Keep_Capital_I_Words_That_Are_Not_Broken(string input)
        => ProductNameSpelling.Correct(input).Should().Be(input);

    [Theory]
    [InlineData("Royal Canın Senıor Sensıtıve 2Kg", "Royal Canin Senior Sensitive 2Kg")]
    [InlineData("Proplan Indoor Dıgestıve", "Proplan Indoor Digestive")]
    public void Correct_Should_Fix_Common_Pet_Food_Words(string input, string expected)
        => ProductNameSpelling.Correct(input).Should().Be(expected);

    [Fact]
    public void Correct_Should_Be_Idempotent()
    {
        var once = ProductNameSpelling.Correct("Flexı Varıo Ikılı Kayıs S Mavı Ip Iyot");

        ProductNameSpelling.Correct(once).Should().Be(once);
    }

    [Theory]
    [InlineData("Zıgzag Bıscuıt Mıx", new[] { "Bıscuıt", "Mıx" })] // ünlüye bitişik ı, x
    [InlineData("Supreme Wıld Duck", new[] { "Wıld" })]
    [InlineData("Herbal Shampoo Fıne", new[] { "Fıne" })]
    public void SuspiciousWords_Should_Flag_Foreign_Shaped_Words(string input, string[] expected)
        => ProductNameSpelling.SuspiciousWords(input).Should().Equal(expected);

    [Theory]
    [InlineData("Kedi Kıyafeti Yıkanabilir Enfeksiyonlarına Karşı")]
    [InlineData("Sadı Kıtık Açıcı Kılçıksız")]
    [InlineData("Flexi Classic Mini")]
    public void SuspiciousWords_Should_Not_Flag_Turkish_Words(string input)
        => ProductNameSpelling.SuspiciousWords(input).Should().BeEmpty();
}
