using System.Security.Cryptography;
using System.Text;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files;
using Backend.Veteriner.Application.Files.Access;
using Backend.Veteriner.Application.Files.Commands.Upload;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Files;

/// <summary>
/// Yükleme sırası ve güvenlik kuralları (IMAGING_API_CONTRACT.md §2/§5, ASAMA6 R2/R4): içerikten tür (sahte uzantı reddi),
/// boyut sınırı (bildirilen ve akış sırasında), kayıt başına sayı, yazma/tarama hatasında temizlik.
/// </summary>
public sealed class UploadStoredFileCommandHandlerTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8.ToArray(), 0x00, 0x01, 0x01, 0x00];

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IStoredFileOwnerAccess> _owner = new();
    private readonly Mock<IRepository<StoredFile>> _files = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly Mock<IFileMalwareScanner> _scanner = new();
    private readonly FileStorageOptions _options = new() { MaxFileBytes = 64, MaxFilesPerOwner = 3 };
    private readonly List<StoredFile> _added = [];

    public UploadStoredFileCommandHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
        _client.SetupGet(c => c.UserId).Returns(_userId);
        _owner.SetupGet(o => o.OwnerType).Returns(StoredFileOwnerType.ImagingRecord);
        _owner.Setup(o => o.ResolveForAttachAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(_clinicId));
        _files.Setup(r => r.CountAsync(It.IsAny<ISpecification<StoredFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _files.Setup(r => r.AddAsync(It.IsAny<StoredFile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredFile f, CancellationToken _) =>
            {
                _added.Add(f);
                return f;
            });
        _files.Setup(r => r.UpdateAsync(It.IsAny<StoredFile>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
    }

    private UploadStoredFileCommandHandler CreateHandler(IFileStorage? storage = null)
        => new(
            _tenant.Object,
            _client.Object,
            [_owner.Object],
            _files.Object,
            storage ?? _storage,
            _scanner.Object,
            Options.Create(_options),
            TimeProvider.System,
            NullLogger<UploadStoredFileCommandHandler>.Instance);

    private UploadStoredFileCommand Command(byte[] content, string fileName = "röntgen.jpg", long? length = null)
        => new(StoredFileOwnerType.ImagingRecord, _ownerId, fileName, length ?? content.Length, new MemoryStream(content));

    [Fact]
    public async Task Jpeg_Content_Should_Become_Ready_With_Detected_Type_Hash_And_Size()
    {
        var content = Jpeg.Concat(new byte[20]).ToArray();

        var result = await CreateHandler().Handle(Command(content, "foto.pdf"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/jpeg");
        result.Value.Kind.Should().Be("Image");
        result.Value.SizeBytes.Should().Be(content.Length);
        result.Value.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        result.Value.FileName.Should().Be("foto.pdf");
        var file = _added.Should().ContainSingle().Subject;
        file.Status.Should().Be(StoredFileStatus.Ready);
        file.ClinicId.Should().Be(_clinicId);
        _storage.Content[file.StorageKey].Should().Equal(content);
    }

    [Fact]
    public async Task Html_Disguised_As_Jpg_Should_Be_Rejected_Before_Anything_Is_Written()
    {
        var html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");

        var result = await CreateHandler().Handle(Command(html, "röntgen.jpg"), CancellationToken.None);

        result.Error.Code.Should().Be("Files.UnsupportedType");
        _added.Should().BeEmpty();
        _storage.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task Declared_Size_Over_Limit_Should_Be_Rejected_Without_Reading()
    {
        var result = await CreateHandler().Handle(Command(Jpeg, length: 65), CancellationToken.None);

        result.Error.Code.Should().Be("Files.TooLarge");
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Stream_Longer_Than_Limit_Should_Be_Discarded_And_Closed()
    {
        var content = Jpeg.Concat(new byte[100]).ToArray();

        var result = await CreateHandler().Handle(Command(content, length: 10), CancellationToken.None);

        result.Error.Code.Should().Be("Files.TooLarge");
        var file = _added.Should().ContainSingle().Subject;
        file.Status.Should().Be(StoredFileStatus.Deleted);
        _storage.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_File_Should_Be_Rejected()
        => (await CreateHandler().Handle(Command([], length: 0), CancellationToken.None))
            .Error.Code.Should().Be("Files.Validation");

    [Fact]
    public async Task Owner_File_Limit_Should_Be_Enforced()
    {
        _files.Setup(r => r.CountAsync(It.IsAny<ISpecification<StoredFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await CreateHandler().Handle(Command(Jpeg), CancellationToken.None);

        result.Error.Code.Should().Be("Files.LimitReached");
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Owner_State_Error_Should_Pass_Through()
    {
        _owner.Setup(o => o.ResolveForAttachAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("ImagingRecords.AlreadyCancelled", "iptal"));

        var result = await CreateHandler().Handle(Command(Jpeg), CancellationToken.None);

        result.Error.Code.Should().Be("ImagingRecords.AlreadyCancelled");
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Storage_Failure_Should_Return_Unavailable_And_Close_Metadata()
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileStorageUnavailableException("root yok"));

        var result = await CreateHandler(storage.Object).Handle(Command(Jpeg), CancellationToken.None);

        result.Error.Code.Should().Be("Files.StorageUnavailable");
        _added.Single().Status.Should().Be(StoredFileStatus.Deleted);
        storage.Verify(s => s.DeleteAsync(_added.Single().StorageKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Scanner_Rejection_Should_Delete_Content()
    {
        _scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Files.MalwareDetected", "zararlı"));

        var result = await CreateHandler().Handle(Command(Jpeg), CancellationToken.None);

        result.Error.Code.Should().Be("Files.MalwareDetected");
        _added.Single().Status.Should().Be(StoredFileStatus.Deleted);
        _storage.Content.Should().BeEmpty();
    }

    [Fact]
    public void Audit_Payload_Should_Not_Contain_File_Content()
    {
        // AuditBehavior ile aynı seçenekler (Web: camelCase).
        var json = System.Text.Json.JsonSerializer.Serialize(
            Command(Jpeg), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        json.Should().NotContain("\"content\"");
        json.Should().Contain("\"fileName\"").And.Contain("\"length\"");
    }

    [Fact]
    public async Task Hashing_Stream_Should_Hash_Everything_And_Throw_Over_Limit()
    {
        var data = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        await using (var ok = new HashingLimitedReadStream(new MemoryStream(data), 200))
        {
            await ok.CopyToAsync(Stream.Null);
            ok.BytesRead.Should().Be(200);
            ok.HashHex.Should().Be(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
        }

        await using var over = new HashingLimitedReadStream(new MemoryStream(data), 199);
        var copy = () => over.CopyToAsync(Stream.Null);
        await copy.Should().ThrowAsync<FileTooLargeException>();
    }

    /// <summary>Sözleşmeye uyan bellek içi depo: aynı anahtar ezilmez.</summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Content { get; } = [];

        public async Task WriteAsync(string storageKey, Stream content, CancellationToken ct)
        {
            if (Content.ContainsKey(storageKey))
                throw new IOException("exists");
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Content[storageKey] = buffer.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)
            => Task.FromResult<Stream?>(Content.TryGetValue(storageKey, out var b) ? new MemoryStream(b) : null);

        public Task DeleteAsync(string storageKey, CancellationToken ct)
        {
            Content.Remove(storageKey);
            return Task.CompletedTask;
        }
    }
}
