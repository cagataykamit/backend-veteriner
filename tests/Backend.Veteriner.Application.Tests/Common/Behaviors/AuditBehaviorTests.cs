using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Backend.Veteriner.Application.Tests.Common.Behaviors;

/// <summary>Denetim yazımı iş sonucunu etkilemez: yazıcı hatası loglanır, yanıt/istisna aynen iletilir.</summary>
public sealed class AuditBehaviorTests
{
    // Moq'un ILogger<AuditBehavior<T,...>> proxy'si için tip erişilebilir olmalı (public).
    public sealed record SampleAuditedCommand : IAuditableRequest
    {
        public string AuditAction => "Sample.Do";
        public string? AuditTarget => "Id=1";
    }

    private readonly Mock<IAuditLogWriter> _writer = new();
    private readonly Mock<ILogger<AuditBehavior<SampleAuditedCommand, Result>>> _logger = new();

    private AuditBehavior<SampleAuditedCommand, Result> CreateBehavior()
        => new(new Mock<IClientContext>().Object, _writer.Object, _logger.Object);

    private void WriterThrows()
        => _writer.Setup(w => w.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit db down"));

    private void VerifyErrorLogged(Times times)
        => _logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public async Task Handle_Should_ReturnHandlerResponse_When_AuditWriterThrows()
    {
        WriterThrows();

        var result = await CreateBehavior().Handle(
            new SampleAuditedCommand(), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("iş yazımı commit edilmiş; denetim hatası 500'e dönüşmemeli");
        VerifyErrorLogged(Times.Once());
    }

    [Fact]
    public async Task Handle_Should_PropagateOriginalException_When_HandlerAndAuditWriterThrow()
    {
        WriterThrows();

        var act = () => CreateBehavior().Handle(
            new SampleAuditedCommand(),
            _ => throw new TimeoutException("handler failed"),
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>("denetim yazıcısının hatası özgün istisnayı maskelememeli");
        VerifyErrorLogged(Times.Once());
    }

    [Fact]
    public async Task Handle_Should_WriteSuccessEntry_And_NotLog_When_WriterSucceeds()
    {
        AuditLogEntry? entry = null;
        _writer.Setup(w => w.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => entry = e)
            .Returns(Task.CompletedTask);

        await CreateBehavior().Handle(
            new SampleAuditedCommand(), _ => Task.FromResult(Result.Failure("X.Y", "iş kuralı")), CancellationToken.None);

        entry.Should().NotBeNull();
        entry!.Action.Should().Be("Sample.Do");
        entry.Success.Should().BeFalse();
        entry.FailureReason.Should().Contain("X.Y");
        VerifyErrorLogged(Times.Never());
    }
}
