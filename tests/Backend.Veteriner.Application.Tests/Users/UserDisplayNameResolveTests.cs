using Backend.Veteriner.Application.Users.Common;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Users;

public sealed class UserDisplayNameResolveTests
{
    [Fact]
    public void Resolve_Should_Prefer_Stored_Name()
        => UserDisplayName.Resolve("  Dr. Ali  ", "x@example.com").Should().Be("Dr. Ali");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Resolve_Should_Fall_Back_To_Email_Derived_Name(string? stored)
        => UserDisplayName.Resolve(stored, "ali.veli@example.com").Should().Be("ali.veli");

    [Fact]
    public void Resolve_Should_Return_Null_When_Nothing_Available()
        => UserDisplayName.Resolve(null, null).Should().BeNull();
}
