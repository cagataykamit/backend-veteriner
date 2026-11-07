using Backend.Veteriner.Domain.ClinicSites;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.ClinicSites;

/// <summary>Yapılandırma güvenlik sınırları (ADR-045 K3/K8/K14): tasarım alanı yorumlanmaz, sınırlar genel kuralla denetlenir.</summary>
public sealed class ClinicSiteConfigTests
{
    private static string Config(string body) => "{\"schemaVersion\":1," + body + "}";

    private static void ShouldFail(string json, string path, string reason)
    {
        var result = ClinicSiteConfig.Parse(json);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ClinicSiteConfig.ValidationCode);
        var issues = (IReadOnlyCollection<ClinicSiteConfigIssue>)result.Error.Extensions!["issues"]!;
        issues.Should().Contain(new ClinicSiteConfigIssue(path, reason));
    }

    [Fact]
    public void Parse_Should_Accept_Taslak_Shape_And_Unknown_Design_Fields()
    {
        var imageId = Guid.NewGuid();
        var json = Config($$$"""
            "content":{"clinic":{"logo":null,"slogan":"Saat: 9-18 arası açığız","website":"ornek.example.com","mapUrl":"https://maps.example.com/x","instagram":"ornekklinik","whatsapp":"0555 000 00 00"},
              "heroImage":{"src":"site-image:{{{imageId}}}","alt":"Kedi"},
              "gallery":[{"src":"stock:hayvan/kedi-1.jpg","alt":""}],
              "members":[{"image":null,"bio":"Dr. Ayşe: 10 yıl deneyim"}]},
            "design":{"conceptKey":"gelecek-paket","yeniAlan":{"derin":[1,2,{"x":"y"}]},"sections":{"hero":"alt-3"}},
            "seo":{"title":"Ana sayfa","ogImage":"site-image:{{{imageId}}}","favicon":""}
            """);

        var result = ClinicSiteConfig.Parse(json);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SchemaVersion.Should().Be(1);
        result.Value.Json.Should().Be(json);
        result.Value.ImageIds.Should().Equal(imageId);
    }

    [Theory]
    [InlineData("{bozuk")]
    [InlineData("[]")]
    [InlineData("\"metin\"")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_Should_Reject_Non_Object(string? json)
        => ClinicSiteConfig.Parse(json).Error.Code.Should().Be(ClinicSiteConfig.ValidationCode);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"schemaVersion":0}""")]
    [InlineData("""{"schemaVersion":"1"}""")]
    [InlineData("""{"schemaVersion":1.5}""")]
    public void Parse_Should_Require_SchemaVersion(string json)
        => ShouldFail(json, ClinicSiteConfig.SchemaVersionKey, ClinicSiteConfig.Reasons.SchemaVersionRequired);

    [Fact]
    public void Parse_Should_Reject_Over_Size_Limit()
    {
        var json = Config($"\"x\":\"{new string('a', ClinicSiteConfig.MaxBytes)}\"");
        ShouldFail(json, "$", ClinicSiteConfig.Reasons.TooLarge);
    }

    [Fact]
    public void Parse_Should_Count_Size_As_Utf8_Bytes()
    {
        // Karakter sayısı sınırın altında, UTF-8 bayt sayısı üstünde.
        var json = Config($"\"x\":\"{new string('ş', ClinicSiteConfig.MaxBytes / 2 + 1)}\"");
        ShouldFail(json, "$", ClinicSiteConfig.Reasons.TooLarge);
    }

    [Fact]
    public void Parse_Should_Reject_Excessive_Depth()
    {
        var depth = ClinicSiteConfig.MaxDepth + 1;
        var json = string.Concat(Enumerable.Repeat("""{"a":""", depth)) + "1" + new string('}', depth);
        ShouldFail(json, "$", ClinicSiteConfig.Reasons.InvalidJson);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("java\\tscript:alert(1)")]
    [InlineData("java\\nscript:alert(1)")]
    [InlineData("\\u0001javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public void Parse_Should_Reject_Dangerous_Schemes_In_Any_Field(string value)
    {
        ShouldFail(Config($"\"content\":{{\"cta\":{{\"href\":\"{value}\"}}}}"), "$.content.cta.href", ClinicSiteConfig.Reasons.LinkSchemeNotAllowed);
        ShouldFail(Config($"\"design\":{{\"bilinmeyenAlan\":\"{value}\"}}"), "$.design.bilinmeyenAlan", ClinicSiteConfig.Reasons.LinkSchemeNotAllowed);
        ShouldFail(Config($"\"list\":[\"{value}\"]"), "$.list[0]", ClinicSiteConfig.Reasons.LinkSchemeNotAllowed);
    }

    [Theory]
    [InlineData("http://ornek.example.com")]
    [InlineData("https://ornek.example.com")]
    [InlineData("tel:+905550000000")]
    [InlineData("mailto:klinik@example.com")]
    [InlineData("ornek.example.com")]
    [InlineData("/hizmetler")]
    public void Parse_Should_Allow_Link_Schemes(string value)
        => ClinicSiteConfig.Parse(Config($"\"mapUrl\":\"{value}\"")).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("ftp://ornek.example.com/a")]
    [InlineData("file:///etc/passwd")]
    [InlineData("sms:+905550000000")]
    public void Parse_Should_Reject_Other_Schemes_In_Link_Fields(string value)
    {
        ShouldFail(Config($"\"mapUrl\":\"{value}\""), "$.mapUrl", ClinicSiteConfig.Reasons.LinkSchemeNotAllowed);
        ShouldFail(Config($"\"facebook\":\"{value}\""), "$.facebook", ClinicSiteConfig.Reasons.LinkSchemeNotAllowed);
    }

    [Fact]
    public void Parse_Should_Reject_Unknown_Absolute_Scheme_In_Any_Field()
        => ShouldFail(Config("""
            "design":{"yeniPaketAlani":"ftp://ornek.example.com/a"}
            """), "$.design.yeniPaketAlani", ClinicSiteConfig.Reasons.LinkSchemeNotAllowed);

    [Theory]
    [InlineData("Acil: 0555 000 00 00")]
    [InlineData("Not:randevu gerekli")]
    [InlineData("javascript: nedir diye sormayın")]
    public void Parse_Should_Not_Treat_Free_Text_As_Link_Unless_Dangerous(string value)
    {
        var result = ClinicSiteConfig.Parse(Config($"\"content\":{{\"body\":\"{value}\"}}"));

        if (value.StartsWith("javascript", StringComparison.Ordinal))
            result.IsSuccess.Should().BeFalse("tehlikeli başlangıç her alanda reddedilir");
        else
            result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://images.unsplash.com/photo-1")]
    [InlineData("http://ornek.example.com/a.jpg")]
    [InlineData("/assets/a.jpg")]
    [InlineData("a.jpg")]
    [InlineData("stock:")]
    [InlineData("stock:../../gizli dosya")]
    public void Parse_Should_Reject_External_Image_References(string value)
    {
        ShouldFail(Config($"\"content\":{{\"heroImage\":{{\"src\":\"{value}\"}}}}"), "$.content.heroImage.src", ClinicSiteConfig.Reasons.ImageReferenceNotAllowed);
        ShouldFail(Config($"\"seo\":{{\"ogImage\":\"{value}\"}}"), "$.seo.ogImage", ClinicSiteConfig.Reasons.ImageReferenceNotAllowed);
    }

    [Theory]
    [InlineData("site-image:bozuk")]
    [InlineData("site-image:00000000-0000-0000-0000-000000000000")]
    public void Parse_Should_Reject_Malformed_Image_Reference_In_Any_Field(string value)
        => ShouldFail(Config($"\"design\":{{\"x\":\"{value}\"}}"), "$.design.x", ClinicSiteConfig.Reasons.ImageReferenceNotAllowed);

    [Fact]
    public void Parse_Should_Collect_Image_References_From_Any_Field_Regardless_Of_Case()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = ClinicSiteConfig.Parse(Config($$$"""
            "design":{"yeniAlan":"SITE-IMAGE:{{{a.ToString().ToUpperInvariant()}}}"},"content":{"gallery":[{"src":"site-image:{{{b:N}}}"},{"src":"site-image:{{{b}}}"}]}
            """));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ImageIds.Should().BeEquivalentTo([a, b]);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("phone")]
    [InlineData("email")]
    [InlineData("address")]
    [InlineData("district")]
    [InlineData("city")]
    [InlineData("workingDays")]
    public void Parse_Should_Reject_Clinic_Record_Fields(string field)
        => ShouldFail(
            Config($"\"content\":{{\"clinic\":{{\"{field}\":\"x\"}}}}"),
            $"$.content.clinic.{field}",
            ClinicSiteConfig.Reasons.ClinicRecordField);

    [Fact]
    public void Parse_Should_Reject_Address_In_Seo()
        => ShouldFail(Config("""
            "seo":{"slug":"baska-adres"}
            """), "$.seo.slug", ClinicSiteConfig.Reasons.ClinicRecordField);

    [Fact]
    public void ValidationError_Should_Cap_Reported_Issues()
    {
        var issues = Enumerable.Range(0, ClinicSiteConfig.MaxReportedIssues + 5)
            .Select(i => new ClinicSiteConfigIssue($"$.x{i}", ClinicSiteConfig.Reasons.LinkSchemeNotAllowed))
            .ToList();

        var error = ClinicSiteConfig.ValidationError(issues);

        ((IReadOnlyCollection<ClinicSiteConfigIssue>)error.Extensions!["issues"]!).Should().HaveCount(ClinicSiteConfig.MaxReportedIssues);
    }
}
