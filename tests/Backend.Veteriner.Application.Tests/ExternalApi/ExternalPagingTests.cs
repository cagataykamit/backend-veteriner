using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExternalApi;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.ExternalApi;

/// <summary>TIER-009: dış API sayfa isteği; kiracı yalnız bağlamdan, sayfa boyutu üst sınırı, imleç gidiş-dönüşü ve bozuk imleç.</summary>
public sealed class ExternalPagingTests
{
    private sealed record Row(Guid Id, DateTime ChangedAtUtc) : IExternalRecord;

    private static ITenantContext Tenant(Guid? tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.SetupGet(t => t.TenantId).Returns(tenantId);
        return mock.Object;
    }

    [Fact]
    public void Resolve_Should_Fail_Closed_Without_Tenant()
        => ExternalPaging.Resolve(Tenant(null), new ExternalPageRequest(null, null, null)).Error.Code
            .Should().Be(ExternalApiErrors.KeyInvalidCode);

    [Theory]
    [InlineData(null, ExternalPaging.DefaultPageSize)]
    [InlineData(0, 1)]
    [InlineData(50, 50)]
    [InlineData(10_000, ExternalPaging.MaxPageSize)]
    public void Resolve_Should_Clamp_Page_Size(int? requested, int expected)
        => ExternalPaging.Resolve(Tenant(Guid.NewGuid()), new ExternalPageRequest(null, null, requested)).Value!.PageSize
            .Should().Be(expected);

    [Fact]
    public void Resolve_Should_Treat_Unspecified_UpdatedSince_As_Utc()
    {
        var since = new DateTime(2026, 11, 1, 8, 30, 0, DateTimeKind.Unspecified);

        var resolved = ExternalPaging.Resolve(Tenant(Guid.NewGuid()), new ExternalPageRequest(since, null, null)).Value!;

        resolved.UpdatedSince!.Value.Kind.Should().Be(DateTimeKind.Utc);
        resolved.UpdatedSince.Value.Should().Be(new DateTime(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Next_Cursor_Should_Round_Trip_And_Be_Absent_On_Last_Page()
    {
        var tenantId = Guid.NewGuid();
        var page = ExternalPaging.Resolve(Tenant(tenantId), new ExternalPageRequest(null, null, 2)).Value!;
        var t = new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var rows = new[] { new Row(Guid.NewGuid(), t), new Row(Guid.NewGuid(), t.AddTicks(1)), new Row(Guid.NewGuid(), t.AddTicks(2)) };

        var first = ExternalPaging.ToPage(rows, page);

        first.Items.Should().HaveCount(2);
        first.NextCursor.Should().NotBeNullOrEmpty();
        var next = ExternalPaging.Resolve(Tenant(tenantId), new ExternalPageRequest(null, first.NextCursor, 2)).Value!;
        next.After.Should().Be(new ExternalPaging.Position(rows[1].ChangedAtUtc, rows[1].Id));
        ExternalPaging.ToPage(rows[..2], page).NextCursor.Should().BeNull();
    }

    [Theory]
    [InlineData("bozuk")]
    [InlineData("MTIzNDU")]
    [InlineData("!!!")]
    public void Resolve_Should_Reject_Invalid_Cursor(string cursor)
        => ExternalPaging.Resolve(Tenant(Guid.NewGuid()), new ExternalPageRequest(null, cursor, null)).Error.Code
            .Should().Be(ExternalApiErrors.ValidationCode);
}
