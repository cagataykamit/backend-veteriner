using System.Text;
using Backend.Veteriner.Domain.Files;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Files;

/// <summary>Dosya katmanı domain kuralları (IMAGING_API_CONTRACT.md §2): içerikten tür, ad temizleme, durum geçişleri.</summary>
public sealed class StoredFileTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 30, 0, DateTimeKind.Utc);

    public static TheoryData<byte[], string> AllowedHeaders => new()
    {
        { [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1], "image/jpeg" },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D], "image/png" },
        { [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WEBP"u8.ToArray()], "image/webp" },
        { [.. "%PDF-1.7\n%"u8.ToArray(), 0xE2, 0xE3], "application/pdf" },
    };

    [Theory]
    [MemberData(nameof(AllowedHeaders))]
    public void Detect_Should_Recognize_Allowed_Types_From_Content(byte[] header, string contentType)
        => StoredFileType.Detect(header)!.ContentType.Should().Be(contentType);

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("<!DOCTYPE html><html><script>alert(1)</script>")]
    [InlineData("GIF89a\u0001\u0000\u0001\u0000")]
    [InlineData("MZ\u0090\u0000\u0003\u0000\u0000\u0000")]
    [InlineData("PK\u0003\u0004 zip veya docx")]
    [InlineData("  %PDF-1.4 bosluktan sonra")]
    public void Detect_Should_Reject_Other_Content_Regardless_Of_Name(string content)
        => StoredFileType.Detect(Encoding.ASCII.GetBytes(content)).Should().BeNull();

    [Fact]
    public void Detect_Should_Reject_Riff_Without_Webp_Marker_And_Short_Input()
    {
        StoredFileType.Detect([.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WAVE"u8.ToArray()]).Should().BeNull();
        StoredFileType.Detect([0xFF, 0xD8]).Should().BeNull();
        StoredFileType.Detect([]).Should().BeNull();
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\Windows\\win.ini", "win.ini")]
    [InlineData("C:\\Users\\vet\\röntgen sol.jpg", "röntgen sol.jpg")]
    [InlineData("a<b>c:d\"e|f?g*h.png", "abcdefgh.png")]
    [InlineData("  ..gizli.pdf.. ", "gizli.pdf")]
    [InlineData("rapor\u0000\u001f.pdf", "rapor.pdf")]
    [InlineData("", "dosya")]
    [InlineData("...", "dosya")]
    [InlineData(null, "dosya")]
    public void Sanitize_Should_Strip_Path_And_Unsafe_Characters(string? input, string expected)
        => StoredFileName.Sanitize(input).Should().Be(expected);

    [Fact]
    public void Sanitize_Should_Limit_Length_And_Keep_Extension()
    {
        var name = new string('a', 300) + ".jpeg";

        var sanitized = StoredFileName.Sanitize(name);

        sanitized.Length.Should().Be(StoredFileName.MaxLength);
        sanitized.Should().EndWith(".jpeg");
    }

    [Fact]
    public void Download_Name_Should_Use_Detected_Extension()
    {
        StoredFileName.ForDownload("röntgen.exe", StoredFileType.Jpeg).Should().Be("röntgen.jpg");
        StoredFileName.ForDownload("sonuc", StoredFileType.Pdf).Should().Be("sonuc.pdf");
    }

    [Fact]
    public void New_File_Should_Be_Uploading_With_Unpredictable_Key_Under_Tenant_Clinic_And_Month()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();

        var file = NewFile(tenantId, clinicId, "../röntgen.png", StoredFileType.Png);

        file.Status.Should().Be(StoredFileStatus.Uploading);
        file.OriginalFileName.Should().Be("röntgen.png");
        file.ContentType.Should().Be("image/png");
        file.StorageKey.Should().Be($"{tenantId:N}/{clinicId:N}/2026/10/{file.Id:N}");
        file.StorageKey.Should().NotContain("röntgen").And.NotContain(".");
    }

    [Fact]
    public void MarkReady_Should_Record_Size_And_Hash_Once()
    {
        var file = NewFile(Guid.NewGuid(), Guid.NewGuid(), "a.pdf", StoredFileType.Pdf);
        var hash = new string('A', 64);

        file.MarkReady(1234, hash, Now).IsSuccess.Should().BeTrue();

        file.Status.Should().Be(StoredFileStatus.Ready);
        file.SizeBytes.Should().Be(1234);
        file.Sha256.Should().Be(new string('a', 64));
        file.ReadyAtUtc.Should().Be(Now);
        file.MarkReady(1234, hash, Now).Error.Code.Should().Be("Files.InvalidState");
    }

    [Fact]
    public void MarkReady_Should_Reject_Empty_Or_Bad_Hash()
    {
        var file = NewFile(Guid.NewGuid(), Guid.NewGuid(), "a.pdf", StoredFileType.Pdf);

        file.MarkReady(0, new string('a', 64), Now).Error.Code.Should().Be("Files.Validation");
        file.MarkReady(10, "abc", Now).Error.Code.Should().Be("Files.Validation");
        file.Status.Should().Be(StoredFileStatus.Uploading);
    }

    [Fact]
    public void MarkDeleted_Should_Close_Access_And_Be_Idempotent()
    {
        var file = NewFile(Guid.NewGuid(), Guid.NewGuid(), "a.pdf", StoredFileType.Pdf);
        var userId = Guid.NewGuid();

        file.MarkDeleted(userId, Now);
        file.MarkDeleted(null, Now.AddHours(1));

        file.Status.Should().Be(StoredFileStatus.Deleted);
        file.DeletedByUserId.Should().Be(userId);
        file.DeletedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void IsStaleUpload_Should_Match_Only_Old_Uploading_Records()
    {
        var old = NewFile(Guid.NewGuid(), Guid.NewGuid(), "a.pdf", StoredFileType.Pdf);
        var fresh = new StoredFile(Guid.NewGuid(), Guid.NewGuid(), StoredFileOwnerType.LabResult, Guid.NewGuid(), "b.pdf", StoredFileType.Pdf, Guid.NewGuid(), Now.AddMinutes(50));
        var ready = NewFile(Guid.NewGuid(), Guid.NewGuid(), "c.pdf", StoredFileType.Pdf);
        ready.MarkReady(10, new string('a', 64), Now);

        var stale = StoredFile.IsStaleUpload(Now.AddMinutes(61), TimeSpan.FromMinutes(60)).Compile();

        stale(old).Should().BeTrue();
        stale(fresh).Should().BeFalse();
        stale(ready).Should().BeFalse();
    }

    private static StoredFile NewFile(Guid tenantId, Guid clinicId, string name, StoredFileType type)
        => new(tenantId, clinicId, StoredFileOwnerType.ImagingRecord, Guid.NewGuid(), name, type, Guid.NewGuid(), Now);
}
