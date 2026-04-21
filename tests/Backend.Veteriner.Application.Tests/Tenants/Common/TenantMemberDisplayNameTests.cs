using Backend.Veteriner.Application.Tenants.Common;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Tenants.Common;

public sealed class TenantMemberDisplayNameTests
{
    [Theory]
    [InlineData("ali@klinik.com", "ali")]
    [InlineData("ali.veli@klinik.com", "ali.veli")]
    [InlineData("MIXED.Case@Example.COM", "MIXED.Case")]
    [InlineData("  trim.me@x.y  ", "trim.me")]
    [InlineData("a@b", "a")]
    public void Derive_Should_Return_LocalPart_For_Valid_Emails(string email, string expected)
    {
        TenantMemberDisplayName.DeriveFromEmail(email).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@no-local.com")]
    [InlineData("@")]
    public void Derive_Should_Return_Null_For_Invalid_Or_Empty(string? email)
    {
        TenantMemberDisplayName.DeriveFromEmail(email).Should().BeNull();
    }

    [Fact]
    public void Derive_Should_Return_Input_When_No_AtSign()
    {
        TenantMemberDisplayName.DeriveFromEmail("plainname").Should().Be("plainname");
    }
}
