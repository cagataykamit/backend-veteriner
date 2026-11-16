using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Persistence;

/// <summary>Arka plan işlerinin geçici SQL hatası kuralı (veritabanı gerektirmez).</summary>
public sealed class TransientSqlRetryTests
{
    [Theory]
    [InlineData(1205)]
    [InlineData(1222)]
    [InlineData(-2)]
    public void Deadlock_And_Timeouts_Should_Be_Transient(int number)
        => TransientSqlRetry.IsTransient(SqlExceptionFactory.Create(number)).Should().BeTrue();

    [Fact]
    public void Wrapped_Deadlock_Should_Be_Transient_And_Constraint_Violation_Should_Not()
    {
        TransientSqlRetry.IsTransient(new DbUpdateException("x", SqlExceptionFactory.Create(1205))).Should().BeTrue();
        TransientSqlRetry.IsTransient(SqlExceptionFactory.Create(2627)).Should().BeFalse();
        TransientSqlRetry.IsTransient(new DbUpdateConcurrencyException("x")).Should().BeFalse();
        TransientSqlRetry.IsTransient(new InvalidOperationException()).Should().BeFalse();
    }

    [Fact]
    public async Task Deadlock_Should_Be_Retried_Until_Success()
    {
        var calls = 0;

        var result = await TransientSqlRetry.ExecuteAsync(
            _ => ++calls == 1 ? throw SqlExceptionFactory.Create(1205) : Task.FromResult(42),
            null, "test", CancellationToken.None);

        result.Should().Be(42);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Non_Transient_Error_Should_Not_Be_Retried()
    {
        var calls = 0;

        var act = () => TransientSqlRetry.ExecuteAsync<int>(
            _ =>
            {
                calls++;
                throw SqlExceptionFactory.Create(2627);
            },
            null, "test", CancellationToken.None);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Persistent_Deadlock_Should_Give_Up_After_Limited_Attempts()
    {
        var calls = 0;

        var act = () => TransientSqlRetry.ExecuteAsync<int>(
            _ =>
            {
                calls++;
                throw SqlExceptionFactory.Create(1205);
            },
            null, "test", CancellationToken.None);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
        calls.Should().Be(TransientSqlRetry.Delays.Count + 1);
    }
}
