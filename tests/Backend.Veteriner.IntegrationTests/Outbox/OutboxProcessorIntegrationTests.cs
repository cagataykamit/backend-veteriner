using System.Linq.Expressions;
using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Outbox;

[CollectionDefinition("outbox-processor", DisableParallelization = true)]
public sealed class OutboxProcessorCollection;

/// <summary>
/// Ayrı LocalDB + kayıt tutan sahte SMTP ile OutboxProcessor işleme döngüsünü doğrular. Barındırılan döngü fabrikada
/// kapalıdır; partiler testteki işlemci örnekleriyle deterministik işletilir.
/// </summary>
[Collection("outbox-processor")]
public sealed class OutboxProcessorIntegrationTests : IClassFixture<OutboxProcessorWebApplicationFactory>
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    private readonly OutboxProcessorWebApplicationFactory _factory;

    public OutboxProcessorIntegrationTests(OutboxProcessorWebApplicationFactory factory) => _factory = factory;

    private RecordingEmailSenderImmediate Sender => _factory.Services.GetRequiredService<RecordingEmailSenderImmediate>();

    public static TheoryData<string> ProjectionEventTypes =>
    [
        AppointmentIntegrationEventTypes.Created,
        ClientIntegrationEventTypes.All[0],
        PetIntegrationEventTypes.Created,
        PaymentIntegrationEventTypes.All[0]
    ];

    [Fact]
    public async Task ProcessBatch_Should_MarkEmailMessageProcessed_WhenImmediateSenderSucceeds()
    {
        await ClearOutboxAsync();
        var subject = NewSubject();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            await outbox.EnqueueEmailAsync(new EmailOutboxPayload
            {
                To = "noop@example.com",
                Subject = subject,
                Body = "integration"
            });
        }

        var processed = await NewProcessor().ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(1);
        Sender.AttemptsFor(subject).Should().Be(1);

        var row = await SingleAsync(m => m.Payload.Contains(subject));
        row.Type.Should().Be(OutboxMessageTypes.Email, "Outbox email mesaj tipleri canonical olarak üretilmeli");
        row.ProcessedAtUtc.Should().NotBeNull("OutboxProcessor should set ProcessedAtUtc after successful email handling");
        row.LastError.Should().BeNull();
        row.Error.Should().BeNull();
        row.DeadLetterAtUtc.Should().BeNull();
        row.ClaimToken.Should().BeNull("başarılı kapanış claim alanlarını temizler");
    }

    [Theory]
    [MemberData(nameof(ProjectionEventTypes))]
    public async Task ProcessBatch_Should_NotConsumeProjectionIntegrationEvents(string projectionEventType)
    {
        await ClearOutboxAsync();
        var projectionMessageId = await AddMessageAsync(projectionEventType, "{}", BaseTime);
        await AddEmailAsync(NewSubject(), BaseTime.AddMinutes(1));

        var processed = await NewProcessor().ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(1, "yalnız e-posta mesajı işlenir");
        var row = await GetAsync(projectionMessageId);
        row.ProcessedAtUtc.Should().BeNull("projeksiyon olayları OutboxProcessor tarafından tüketilmemeli");
        row.DeadLetterAtUtc.Should().BeNull();
        row.RetryCount.Should().Be(0);
        row.ClaimToken.Should().BeNull("projeksiyon olayı generic işlemci tarafından claim edilmemeli");
    }

    [Fact]
    public async Task ProcessBatch_Should_RetryUnknownType()
    {
        await ClearOutboxAsync();
        var messageId = await AddMessageAsync("appointment.unknown.v1", "{}", BaseTime);

        var processed = await NewProcessor().ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(0);
        var row = await GetAsync(messageId);
        row.RetryCount.Should().Be(1);
        row.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow, "sıradaki deneme backoff ile ileri atılır");
        row.ProcessedAtUtc.Should().BeNull();
        row.DeadLetterAtUtc.Should().BeNull();
        row.LastError.Should().NotBeNullOrWhiteSpace();
        row.ClaimToken.Should().BeNull("retry kaydı claim'i bırakır");
    }

    [Fact]
    public async Task ProcessBatch_Should_DeadLetter_WhenMaxRetryReached()
    {
        await ClearOutboxAsync();
        var subject = NewSubject();
        Sender.OnSend(subject, () => throw new InvalidOperationException("smtp down"));
        var messageId = await AddEmailAsync(subject, BaseTime);

        var processed = await NewProcessor(maxRetryCount: 1).ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(0);
        var row = await GetAsync(messageId);
        row.DeadLetterAtUtc.Should().NotBeNull();
        row.ProcessedAtUtc.Should().BeNull();
        row.LastError.Should().Be("smtp down");
    }

    [Fact]
    public async Task ProcessBatch_Should_ProcessEmailBehindManyAppointmentMessages()
    {
        await ClearOutboxAsync();
        for (var i = 0; i < 55; i++)
            await AddMessageAsync(AppointmentIntegrationEventTypes.Created, "{}", BaseTime.AddMinutes(i));

        var emailMessageId = await AddEmailAsync(NewSubject(), BaseTime.AddHours(1));

        var processed = await NewProcessor(batchSize: 50).ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(1);
        (await GetAsync(emailMessageId)).ProcessedAtUtc.Should().NotBeNull(
            "email mesaji appointment mesajlarinin arkasinda kalsa da islenmeli");

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointmentRows = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == AppointmentIntegrationEventTypes.Created)
            .ToListAsync();
        appointmentRows.Should().HaveCount(55).And.AllSatisfy(m =>
        {
            m.ProcessedAtUtc.Should().BeNull();
            m.RetryCount.Should().Be(0);
        });
    }

    [Fact]
    public async Task ProcessBatch_Should_KeepOtherMessagesProcessed_WhenOneMessageFails()
    {
        await ClearOutboxAsync();
        var (first, failing, last) = (NewSubject(), NewSubject(), NewSubject());
        Sender.OnSend(failing, () => throw new InvalidOperationException("smtp rejected"));
        var firstId = await AddEmailAsync(first, BaseTime);
        var failingId = await AddEmailAsync(failing, BaseTime.AddMinutes(1));
        var lastId = await AddEmailAsync(last, BaseTime.AddMinutes(2));

        var processed = await NewProcessor().ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(2);
        (await GetAsync(firstId)).ProcessedAtUtc.Should().NotBeNull();
        (await GetAsync(lastId)).ProcessedAtUtc.Should().NotBeNull("hata alan mesaj sonrakileri durdurmaz");

        var failed = await GetAsync(failingId);
        failed.ProcessedAtUtc.Should().BeNull();
        failed.DeadLetterAtUtc.Should().BeNull();
        failed.RetryCount.Should().Be(1);
        failed.LastError.Should().Be("smtp rejected");
        failed.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow);
    }

    /// <summary>
    /// A işlemcisi ikinci mesajın gönderiminde bekletilir: ilk mesajın sonucu o anda veritabanında olmalı (mesaj başına
    /// kayıt) ve kalan mesajlar A'nın claim'inde olduğu için B hiçbir mesajı alamaz. Sıralama bekletme kapısıyla kurulur,
    /// zamanlamaya bağlı değildir.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_Should_PersistEachResult_And_NotLetSecondProcessorTakeClaimedMessages()
    {
        await ClearOutboxAsync();
        var (first, gated, last) = (NewSubject(), NewSubject(), NewSubject());
        var gateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Sender.OnSend(gated, async () =>
        {
            gateEntered.TrySetResult();
            await gateRelease.Task;
        });

        var firstId = await AddEmailAsync(first, BaseTime);
        var gatedId = await AddEmailAsync(gated, BaseTime.AddMinutes(1));
        var lastId = await AddEmailAsync(last, BaseTime.AddMinutes(2));

        var processorA = NewProcessor();
        var processorB = NewProcessor();
        var runA = processorA.ProcessBatchAsync(CancellationToken.None);

        try
        {
            await gateEntered.Task.WaitAsync(GateTimeout);

            (await GetAsync(firstId)).ProcessedAtUtc.Should().NotBeNull("sonuç parti sonunu beklemeden yazılır");
            var gatedRow = await GetAsync(gatedId);
            gatedRow.ProcessedAtUtc.Should().BeNull();
            gatedRow.ClaimedBy.Should().Be(processorA.WorkerId);
            (await GetAsync(lastId)).ClaimedBy.Should().Be(processorA.WorkerId);

            var processedByB = await processorB.ProcessBatchAsync(CancellationToken.None);

            processedByB.Should().Be(0, "A'nın claim ettiği mesajlar B tarafından alınmaz");
            Sender.AttemptsFor(gated).Should().Be(1);
            Sender.AttemptsFor(last).Should().Be(0);
        }
        finally
        {
            gateRelease.TrySetResult();
        }

        (await runA.WaitAsync(GateTimeout)).Should().Be(3);
        foreach (var id in new[] { firstId, gatedId, lastId })
            (await GetAsync(id)).ProcessedAtUtc.Should().NotBeNull();
        foreach (var subject in new[] { first, gated, last })
            Sender.AttemptsFor(subject).Should().Be(1, "her mesaj tam bir kez gönderilir");
    }

    [Fact]
    public async Task HostedLoop_Should_ProcessEmail()
    {
        await ClearOutboxAsync();
        var messageId = await AddEmailAsync(NewSubject(), BaseTime);
        var processor = new OutboxProcessor(
            _factory.Services,
            Options.Create(new OutboxOptions { Enabled = true, LoopIntervalSeconds = 1 }),
            TimeProvider.System);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            await EventualConsistencyTestSupport.EventuallyAsync(
                async () => (await GetAsync(messageId)).ProcessedAtUtc is not null,
                timeout: TimeSpan.FromSeconds(30),
                pollInterval: TimeSpan.FromMilliseconds(200),
                because: "barındırılan döngü bekleyen e-postayı işlemeli");
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    private static DateTime BaseTime => DateTime.UtcNow.AddHours(-2);

    private static string NewSubject() => $"proc-{Guid.NewGuid():N}";

    private OutboxProcessor NewProcessor(int batchSize = 50, int maxRetryCount = 10)
        => new(
            _factory.Services,
            Options.Create(new OutboxOptions { BatchSize = batchSize, MaxRetryCount = maxRetryCount }),
            TimeProvider.System);

    private Task<Guid> AddEmailAsync(string subject, DateTime createdAtUtc)
        => AddMessageAsync(
            OutboxMessageTypes.Email,
            JsonSerializer.Serialize(new EmailOutboxPayload { To = "noop@example.com", Subject = subject, Body = "integration" }),
            createdAtUtc);

    private async Task<Guid> AddMessageAsync(string type, string payload, DateTime createdAtUtc)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = new OutboxMessage { Type = type, Payload = payload, CreatedAtUtc = createdAtUtc };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private Task<OutboxMessage> GetAsync(Guid id) => SingleAsync(m => m.Id == id);

    private async Task<OutboxMessage> SingleAsync(Expression<Func<OutboxMessage, bool>> predicate)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(predicate);
    }

    /// <summary>Ayrı veritabanında önceki testlerin mesajları bu testin partisine karışmasın.</summary>
    private async Task ClearOutboxAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.OutboxMessages.ExecuteDeleteAsync();
    }
}
