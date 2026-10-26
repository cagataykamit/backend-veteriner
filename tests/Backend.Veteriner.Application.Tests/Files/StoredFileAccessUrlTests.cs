using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files;
using Backend.Veteriner.Application.Files.Access;
using Backend.Veteriner.Application.Files.Queries.CreateAccessUrl;
using Backend.Veteriner.Application.Files.Queries.GetSignedContent;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Files;

/// <summary>
/// İmzalı URL (IMAGING_API_CONTRACT.md §5): sahip okunamıyorsa üretilmez, süre dolunca veya imza bozulunca 403, iptal edilen
/// (Deleted) dosya 404. İmzanın kriptografisi entegrasyon testinde (HMAC uygulaması) doğrulanır.
/// </summary>
public sealed class StoredFileAccessUrlTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly StoredFile _file;
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IStoredFileOwnerAccess> _owner = new();
    private readonly Mock<IReadRepository<StoredFile>> _files = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly FixedTime _time = new(Now);
    private readonly FakeSigner _signer = new();

    public StoredFileAccessUrlTests()
    {
        _file = new StoredFile(_tenantId, Guid.NewGuid(), StoredFileOwnerType.ImagingRecord, _ownerId, "a.png",
            StoredFileType.Png, Guid.NewGuid(), Now.UtcDateTime);
        _file.MarkReady(10, new string('a', 64), Now.UtcDateTime);

        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
        _owner.SetupGet(o => o.OwnerType).Returns(StoredFileOwnerType.ImagingRecord);
        _owner.Setup(o => o.ResolveForReadAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(_file.ClinicId));
        _files.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<StoredFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _file);
        _storage.Setup(s => s.OpenReadAsync(_file.StorageKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
    }

    private StoredFileContentReader Reader()
        => new([_owner.Object], _files.Object, _storage.Object, NullLogger<StoredFileContentReader>.Instance);

    private ValueTask<Result<Backend.Veteriner.Application.Files.Contracts.Dtos.StoredFileAccessUrlDto>> CreateUrl(Guid? fileId = null)
        => new CreateStoredFileAccessUrlQueryHandler(
                _tenant.Object, Reader(), _signer, Options.Create(new FileStorageOptions()), _time)
            .Handle(new CreateStoredFileAccessUrlQuery(StoredFileOwnerType.ImagingRecord, _ownerId, fileId ?? _file.Id), CancellationToken.None);

    private ValueTask<Result<Backend.Veteriner.Application.Files.Contracts.Dtos.StoredFileContent>> Open(long expires, string? signature)
        => new GetSignedStoredFileContentQueryHandler(Reader(), _signer, _time)
            .Handle(new GetSignedStoredFileContentQuery(_file.Id, expires, signature), CancellationToken.None);

    [Fact]
    public async Task Url_Should_Expire_After_Five_Minutes_By_Default()
    {
        var url = (await CreateUrl()).Value!;

        url.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddMinutes(5));
        url.Url.Should().StartWith($"/api/v1/files/signed/{_file.Id:D}?expires={Now.AddMinutes(5).ToUnixTimeSeconds()}&signature=");
    }

    [Fact]
    public async Task Signed_Content_Should_Open_Within_Lifetime_And_Fail_After()
    {
        var expires = Now.AddMinutes(5).ToUnixTimeSeconds();
        var signature = _signer.Sign(_file.Id, expires);

        var ok = await Open(expires, signature);
        ok.IsSuccess.Should().BeTrue();
        ok.Value!.ContentType.Should().Be("image/png");

        _time.Now = Now.AddMinutes(5).AddSeconds(1);
        (await Open(expires, signature)).Error.Code.Should().Be("Files.AccessUrlInvalid");
    }

    [Fact]
    public async Task Tampered_Or_Missing_Signature_Should_Be_Rejected()
    {
        var expires = Now.AddMinutes(5).ToUnixTimeSeconds();

        (await Open(expires, "bozuk")).Error.Code.Should().Be("Files.AccessUrlInvalid");
        (await Open(expires, null)).Error.Code.Should().Be("Files.AccessUrlInvalid");
        (await Open(expires + 3600, _signer.Sign(_file.Id, expires))).Error.Code.Should().Be("Files.AccessUrlInvalid");
    }

    [Fact]
    public async Task Deleted_File_Should_Not_Open_Even_With_Valid_Signature()
    {
        var expires = Now.AddMinutes(5).ToUnixTimeSeconds();
        var signature = _signer.Sign(_file.Id, expires);
        _file.MarkDeleted(Guid.NewGuid(), Now.UtcDateTime);

        (await Open(expires, signature)).Error.Code.Should().Be("Files.NotFound");
    }

    [Fact]
    public async Task Url_Should_Not_Be_Issued_When_Owner_Is_Not_Readable_Or_File_Belongs_Elsewhere()
    {
        var foreign = new StoredFile(_tenantId, Guid.NewGuid(), StoredFileOwnerType.ImagingRecord, Guid.NewGuid(), "b.png",
            StoredFileType.Png, Guid.NewGuid(), Now.UtcDateTime);
        foreign.MarkReady(10, new string('b', 64), Now.UtcDateTime);
        _files.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<StoredFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreign);
        (await CreateUrl(foreign.Id)).Error.Code.Should().Be("Files.NotFound");

        _owner.Setup(o => o.ResolveForReadAsync(_tenantId, _ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("ImagingRecords.NotFound", "yok"));
        (await CreateUrl()).Error.Code.Should().Be("ImagingRecords.NotFound");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeSigner : IFileAccessSigner
    {
        public string Sign(Guid fileId, long expiresUnixSeconds) => $"{fileId:N}-{expiresUnixSeconds}";

        public bool IsValid(Guid fileId, long expiresUnixSeconds, string? signature)
            => signature == Sign(fileId, expiresUnixSeconds);
    }
}
