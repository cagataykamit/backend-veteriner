using Backend.Veteriner.Domain.Users;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Users;

public sealed class UserDisplayNameTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static User NewUser() => new("ali.veli@example.com", "hash", Now);

    [Fact]
    public void New_User_Should_Have_No_DisplayName()
        => NewUser().DisplayName.Should().BeNull();

    [Fact]
    public void SetDisplayName_Should_Trim_And_Store()
    {
        var user = NewUser();

        var result = user.SetDisplayName("  Dr. Ali Veli  ", Now);

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
        user.SetDisplayName("Ali", Now);

        user.SetDisplayName(blank, Now).IsSuccess.Should().BeTrue();

        user.DisplayName.Should().BeNull();
    }

    [Fact]
    public void SetDisplayName_Over_Max_Length_Should_Fail_And_Keep_Previous()
    {
        var user = NewUser();
        user.SetDisplayName("Ali", Now);

        var result = user.SetDisplayName(new string('a', User.MaxDisplayNameLength + 1), Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Users.Validation");
        user.DisplayName.Should().Be("Ali");
    }
}
