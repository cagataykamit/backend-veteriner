using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Application.Tenants.Activation;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Mediator;
using Microsoft.Extensions.Logging;
using Moq;

namespace Backend.Veteriner.Application.Tests.Common.Behaviors;

/// <summary>
/// TRIAL-008: işaretli komut başarıyla bitince ilk aksiyon kaydedilir; başarısız sonuçta, kiracı bağlamı yokken ve
/// işaretsiz istekte yazılmaz; kaydedici hatası yutulur ve loglanır (iş sonucu değişmez).
/// </summary>
public sealed class ActivationSignalBehaviorTests
{
    public sealed record SampleSignalCommand : IRequest<Result<Guid>>, IActivationSignalRequest
    {
        public ActivationSignalKind ActivationSignal => ActivationSignalKind.PetCreated;
    }

    public sealed record PlainCommand : IRequest<Result>;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<ITenantActivationRecorder> _recorder = new();
    private readonly Mock<ILogger<ActivationSignalBehavior<SampleSignalCommand, Result<Guid>>>> _logger = new();

    public ActivationSignalBehaviorTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        _client.SetupGet(c => c.UserId).Returns(UserId);
    }

    private ActivationSignalBehavior<SampleSignalCommand, Result<Guid>> CreateBehavior()
        => new(_tenant.Object, _client.Object, _recorder.Object, _logger.Object);

    private void VerifyRecorded(Times times)
        => _recorder.Verify(r => r.RecordFirstActionAsync(TenantId, ActivationSignalKind.PetCreated, UserId, It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Handle_Should_Record_When_Command_Succeeds()
    {
        var result = await CreateBehavior().Handle(
            new SampleSignalCommand(), (_, _) => ValueTask.FromResult(Result<Guid>.Success(Guid.NewGuid())), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyRecorded(Times.Once());
    }

    [Fact]
    public async Task Handle_Should_NotRecord_When_Command_Fails()
    {
        await CreateBehavior().Handle(
            new SampleSignalCommand(), (_, _) => ValueTask.FromResult(Result<Guid>.Failure("Pets.Invalid", "x")), CancellationToken.None);

        _recorder.Verify(r => r.RecordFirstActionAsync(It.IsAny<Guid>(), It.IsAny<ActivationSignalKind>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_Should_NotRecord_When_TenantContext_Missing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        await CreateBehavior().Handle(
            new SampleSignalCommand(), (_, _) => ValueTask.FromResult(Result<Guid>.Success(Guid.NewGuid())), CancellationToken.None);

        _recorder.Verify(r => r.RecordFirstActionAsync(It.IsAny<Guid>(), It.IsAny<ActivationSignalKind>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_Should_PassThrough_When_Request_Not_Marked()
    {
        var behavior = new ActivationSignalBehavior<PlainCommand, Result>(
            _tenant.Object, _client.Object, _recorder.Object,
            new Mock<ILogger<ActivationSignalBehavior<PlainCommand, Result>>>().Object);

        var result = await behavior.Handle(new PlainCommand(), (_, _) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _recorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_ReturnHandlerResponse_And_LogError_When_Recorder_Throws()
    {
        _recorder.Setup(r => r.RecordFirstActionAsync(It.IsAny<Guid>(), It.IsAny<ActivationSignalKind>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var id = Guid.NewGuid();

        var result = await CreateBehavior().Handle(
            new SampleSignalCommand(), (_, _) => ValueTask.FromResult(Result<Guid>.Success(id)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("ölçüm hatası iş sonucunu bozmamalı");
        result.Value.Should().Be(id);
        _logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
    }
}
