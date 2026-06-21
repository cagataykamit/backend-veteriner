using System.Data;
using System.Data.Common;
using System.Text;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Projections;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Backend.Veteriner.Infrastructure.Outbox;

public sealed class SqlPaymentOutboxClaimRepository : IPaymentOutboxClaimRepository
{
    private static readonly string[] PaymentEventTypes = PaymentIntegrationEventTypes.All.ToArray();

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public SqlPaymentOutboxClaimRepository(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ClaimedIntegrationOutboxMessage>> ClaimNextBatchAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be at least 1.");

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var leaseExpiresAtUtc = nowUtc.Add(leaseDuration);

        var sql = BuildClaimSql();
        await using var command = await CreateCommandAsync(sql, cancellationToken);
        AddClaimParameters(command, workerId, batchSize, nowUtc, leaseExpiresAtUtc);

        return await ReadClaimedMessagesAsync(command, cancellationToken);
    }

    public async Task<bool> MarkProcessedAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        const string sql = """
            UPDATE OutboxMessages
            SET ProcessedAtUtc = @nowUtc,
                LastError = NULL,
                Error = NULL,
                NextAttemptAtUtc = NULL,
                ClaimedBy = NULL,
                ClaimToken = NULL,
                ClaimedAtUtc = NULL,
                LeaseExpiresAtUtc = NULL
            WHERE Id = @messageId
              AND ClaimToken = @claimToken
              AND ClaimedBy = @workerId
              AND ProcessedAtUtc IS NULL
            """;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await using var command = await CreateCommandAsync(sql, cancellationToken);
        command.Parameters.Add(new SqlParameter("@nowUtc", SqlDbType.DateTime2) { Value = nowUtc });
        command.Parameters.Add(new SqlParameter("@messageId", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@claimToken", SqlDbType.UniqueIdentifier) { Value = claimToken });
        command.Parameters.Add(new SqlParameter("@workerId", SqlDbType.NVarChar, 128) { Value = workerId });

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<bool> MarkRetryAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        int retryCount,
        DateTime nextAttemptAtUtc,
        string error,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        const string sql = """
            UPDATE OutboxMessages
            SET RetryCount = @retryCount,
                NextAttemptAtUtc = @nextAttemptAtUtc,
                LastError = @error,
                Error = @error,
                ClaimedBy = NULL,
                ClaimToken = NULL,
                ClaimedAtUtc = NULL,
                LeaseExpiresAtUtc = NULL
            WHERE Id = @messageId
              AND ClaimToken = @claimToken
              AND ClaimedBy = @workerId
              AND ProcessedAtUtc IS NULL
              AND DeadLetterAtUtc IS NULL
            """;

        await using var command = await CreateCommandAsync(sql, cancellationToken);
        command.Parameters.Add(new SqlParameter("@retryCount", SqlDbType.Int) { Value = retryCount });
        command.Parameters.Add(new SqlParameter("@nextAttemptAtUtc", SqlDbType.DateTime2) { Value = nextAttemptAtUtc });
        command.Parameters.Add(new SqlParameter("@error", SqlDbType.NVarChar, -1) { Value = error });
        command.Parameters.Add(new SqlParameter("@messageId", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@claimToken", SqlDbType.UniqueIdentifier) { Value = claimToken });
        command.Parameters.Add(new SqlParameter("@workerId", SqlDbType.NVarChar, 128) { Value = workerId });

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<bool> MarkDeadLetterAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        string error,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        const string sql = """
            UPDATE OutboxMessages
            SET DeadLetterAtUtc = @nowUtc,
                LastError = @error,
                Error = @error,
                ClaimedBy = NULL,
                ClaimToken = NULL,
                ClaimedAtUtc = NULL,
                LeaseExpiresAtUtc = NULL
            WHERE Id = @messageId
              AND ClaimToken = @claimToken
              AND ClaimedBy = @workerId
              AND ProcessedAtUtc IS NULL
            """;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await using var command = await CreateCommandAsync(sql, cancellationToken);
        command.Parameters.Add(new SqlParameter("@nowUtc", SqlDbType.DateTime2) { Value = nowUtc });
        command.Parameters.Add(new SqlParameter("@error", SqlDbType.NVarChar, -1) { Value = error });
        command.Parameters.Add(new SqlParameter("@messageId", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@claimToken", SqlDbType.UniqueIdentifier) { Value = claimToken });
        command.Parameters.Add(new SqlParameter("@workerId", SqlDbType.NVarChar, 128) { Value = workerId });

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    private static string BuildClaimSql()
    {
        var typeInClause = new StringBuilder();
        for (var i = 0; i < PaymentEventTypes.Length; i++)
        {
            if (i > 0)
                typeInClause.Append(", ");

            typeInClause.Append("@type").Append(i);
        }

        return $"""
            ;WITH candidates AS (
                SELECT TOP (@batchSize) o.Id
                FROM OutboxMessages o WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE o.ProcessedAtUtc IS NULL
                  AND o.DeadLetterAtUtc IS NULL
                  AND o.Type IN ({typeInClause})
                  AND (o.NextAttemptAtUtc IS NULL OR o.NextAttemptAtUtc <= @nowUtc)
                  AND (o.ClaimToken IS NULL OR o.LeaseExpiresAtUtc IS NULL OR o.LeaseExpiresAtUtc <= @nowUtc)
                ORDER BY o.CreatedAtUtc, o.Id
            )
            UPDATE o
            SET ClaimedBy = @workerId,
                ClaimToken = NEWID(),
                ClaimedAtUtc = @nowUtc,
                LeaseExpiresAtUtc = @leaseExpiresAtUtc
            OUTPUT
                inserted.Id,
                inserted.Type,
                inserted.Payload,
                inserted.CreatedAtUtc,
                inserted.RetryCount,
                inserted.ClaimToken,
                inserted.ClaimedBy,
                inserted.ClaimedAtUtc,
                inserted.LeaseExpiresAtUtc
            FROM OutboxMessages o
            INNER JOIN candidates c ON c.Id = o.Id
            """;
    }

    private static void AddClaimParameters(
        DbCommand command,
        string workerId,
        int batchSize,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc)
    {
        command.Parameters.Add(new SqlParameter("@batchSize", SqlDbType.Int) { Value = batchSize });
        command.Parameters.Add(new SqlParameter("@workerId", SqlDbType.NVarChar, 128) { Value = workerId });
        command.Parameters.Add(new SqlParameter("@nowUtc", SqlDbType.DateTime2) { Value = nowUtc });
        command.Parameters.Add(new SqlParameter("@leaseExpiresAtUtc", SqlDbType.DateTime2) { Value = leaseExpiresAtUtc });

        for (var i = 0; i < PaymentEventTypes.Length; i++)
        {
            command.Parameters.Add(new SqlParameter($"@type{i}", SqlDbType.NVarChar, 64)
            {
                Value = PaymentEventTypes[i]
            });
        }
    }

    private async Task<DbCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static async Task<IReadOnlyList<ClaimedIntegrationOutboxMessage>> ReadClaimedMessagesAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<ClaimedIntegrationOutboxMessage>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ClaimedIntegrationOutboxMessage(
                Id: reader.GetGuid(0),
                Type: reader.GetString(1),
                Payload: reader.GetString(2),
                CreatedAtUtc: reader.GetDateTime(3),
                RetryCount: reader.GetInt32(4),
                ClaimToken: reader.GetGuid(5),
                ClaimedBy: reader.GetString(6),
                ClaimedAtUtc: reader.GetDateTime(7),
                LeaseExpiresAtUtc: reader.GetDateTime(8)));
        }

        return results;
    }
}
