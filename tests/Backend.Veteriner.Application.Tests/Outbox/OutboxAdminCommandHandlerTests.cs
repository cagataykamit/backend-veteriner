using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Outbox;
using Backend.Veteriner.Application.Outbox.Commands.Delete;
using Backend.Veteriner.Application.Outbox.Commands.RetryAllDeadLetters;
using Backend.Veteriner.Application.Outbox.Commands.RetryDeadLetter;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Outbox;

/// <summary>
/// Outbox yönetim komutları: başarısız sonuçta commit yok; platform işlemi olduğundan kiracı abonelik yazma kapısından muaf.
/// </summary>
public sealed class OutboxAdminCommandHandlerTests
{
    private readonly Mock<IOutboxAdminRepository> _outbox = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClientContext> _client = new();

    [Theory]
    [InlineData(typeof(RetryOutboxDeadLetterCommand))]
    [InlineData(typeof(RetryAllOutboxDeadLettersCommand))]
    [InlineData(typeof(DeleteOutboxMessageCommand))]
    public void Commands_Should_Implement_IgnoreTenantWriteSubscriptionGuard(Type commandType)
        => typeof(IIgnoreTenantWriteSubscriptionGuard).IsAssignableFrom(commandType).Should().BeTrue();

    [Fact]
    public void Commands_Should_Be_Audited_With_Action_And_Target()
    {
        var id = Guid.NewGuid();
        IAuditableRequest[] commands =
        [
            new RetryOutboxDeadLetterCommand(id),
            new RetryAllOutboxDeadLettersCommand(),
            new DeleteOutboxMessageCommand(id)
        ];

        commands.Select(c => c.AuditAction).Should().Equal("Outbox.RetryDeadLetter", "Outbox.RetryAllDeadLetters", "Outbox.Delete");
        commands.Select(c => c.AuditTarget).Should().Equal($"OutboxMessageId={id}", null, $"OutboxMessageId={id}");
    }

    [Theory]
    [InlineData(OutboxRetryOutcome.NotFound, "Outbox.NotFound")]
    [InlineData(OutboxRetryOutcome.NotDeadLetter, "Outbox.NotDeadLetter")]
    public async Task RetryDeadLetter_Should_FailWithoutCommit_When_NotRetried(OutboxRetryOutcome outcome, string expectedCode)
    {
        var id = Guid.NewGuid();
        _outbox.Setup(x => x.RetryDeadLetterAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);
        var handler = new RetryOutboxDeadLetterCommandHandler(
            _outbox.Object, _uow.Object, _client.Object, NullLogger<RetryOutboxDeadLetterCommandHandler>.Instance);

        var result = await handler.Handle(new RetryOutboxDeadLetterCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(expectedCode);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryDeadLetter_Should_Commit_When_Retried()
    {
        var id = Guid.NewGuid();
        _outbox.Setup(x => x.RetryDeadLetterAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(OutboxRetryOutcome.Retried);
        var handler = new RetryOutboxDeadLetterCommandHandler(
            _outbox.Object, _uow.Object, _client.Object, NullLogger<RetryOutboxDeadLetterCommandHandler>.Instance);

        var result = await handler.Handle(new RetryOutboxDeadLetterCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Should_FailWithoutCommit_When_MessageMissing()
    {
        _outbox.Setup(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var handler = new DeleteOutboxMessageCommandHandler(
            _outbox.Object, _uow.Object, _client.Object, NullLogger<DeleteOutboxMessageCommandHandler>.Instance);

        var result = await handler.Handle(new DeleteOutboxMessageCommand(Guid.NewGuid()), CancellationToken.None);

        result.Error.Should().Be(OutboxErrors.NotFound);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryAll_Should_SkipCommit_When_NoDeadLetters()
    {
        _outbox.Setup(x => x.RetryAllDeadLettersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var handler = new RetryAllOutboxDeadLettersCommandHandler(
            _outbox.Object, _uow.Object, _client.Object, NullLogger<RetryAllOutboxDeadLettersCommandHandler>.Instance);

        var count = await handler.Handle(new RetryAllOutboxDeadLettersCommand(), CancellationToken.None);

        count.Should().Be(0);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
