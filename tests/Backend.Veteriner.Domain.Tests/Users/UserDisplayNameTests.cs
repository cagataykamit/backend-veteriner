using Backend.Veteriner.Domain.Users;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Users;

public sealed class UserDisplayNameTests
{
    private static User NewUser() => new("ali.veli@example.com", "hash");

    [Fact]
    public void New_User_Should_Have_No_DisplayName()
        => NewUser().DisplayName.Should().BeNull();

    [Fact]
    public void SetDisplayName_Should_Trim_And_Store()
    {
        var user = NewUser();

        var result = user.SetDisplayName("  Dr. Ali Veli  ");

        result.IsSuccess.Should().BeTrue();
        user.DisplayName.Should().Be("Dr. Ali Veli");
        user.UpdatedAtUtc.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetDisplayName_With_Blank_Should_Clear(string? blank)
    {
        var user = NewUser();
        user.SetDisplayName("Ali");

        user.SetDisplayName(blank).IsSuccess.Should().BeTrue();

        user.DisplayName.Should().BeNull();
    }

    [Fact]
    public void SetDisplayName_Over_Max_Length_Should_Fail_And_Keep_Previous()
    {
        var user = NewUser();
        user.SetDisplayName("Ali");

        var result = user.SetDisplayName(new string('a', User.MaxDisplayNameLength + 1));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Users.Validation");
        user.DisplayName.Should().Be("Ali");
    }
}
