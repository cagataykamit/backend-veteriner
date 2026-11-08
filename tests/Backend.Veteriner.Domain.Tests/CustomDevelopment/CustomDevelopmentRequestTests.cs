using Backend.Veteriner.Domain.CustomDevelopment;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.CustomDevelopment;

/// <summary>TIER-010: alanlar, durum akışı (Alındı → İnceleniyor → Planlandı / Kapsam dışı → Tamamlandı), kapsam dışında gerekçe.</summary>
public sealed class CustomDevelopmentRequestTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private static CustomDevelopmentRequest New()
        => CustomDevelopmentRequest.Create(
            Guid.NewGuid(), "Muayene çıktısına logo", "Çıktının üstünde klinik logosu olsun.", CustomDevelopmentTargetKind.Output,
            "Muayene çıktısı", CustomDevelopmentPriority.Normal, UserId, Now).Value!;

    private static CustomDevelopmentRequest InStatus(CustomDevelopmentRequestStatus status)
    {
        var request = New();
        if (status == CustomDevelopmentRequestStatus.Received)
            return request;
        request.ChangeStatus(CustomDevelopmentRequestStatus.InReview, null, null, AdminId, Now).IsSuccess.Should().BeTrue();
        if (status == CustomDevelopmentRequestStatus.InReview)
            return request;
        if (status == CustomDevelopmentRequestStatus.OutOfScope)
        {
            request.ChangeStatus(status, "Ürün dışı iş", null, AdminId, Now).IsSuccess.Should().BeTrue();
            return request;
        }

        request.ChangeStatus(CustomDevelopmentRequestStatus.Planned, null, null, AdminId, Now).IsSuccess.Should().BeTrue();
        if (status == CustomDevelopmentRequestStatus.Completed)
            request.ChangeStatus(status, null, null, AdminId, Now).IsSuccess.Should().BeTrue();
        return request;
    }

    [Fact]
    public void Create_Should_Trim_And_Start_Received()
    {
        var request = CustomDevelopmentRequest.Create(
            Guid.NewGuid(), "  Başlık  ", "  Metin  ", CustomDevelopmentTargetKind.Report, "  Gün sonu  ", CustomDevelopmentPriority.High, UserId, Now).Value!;

        request.RequestTitle.Should().Be("Başlık");
        request.RequestText.Should().Be("Metin");
        request.RequestTarget.Should().Be("Gün sonu");
        request.Status.Should().Be(CustomDevelopmentRequestStatus.Received);
        request.IsOpen.Should().BeTrue();
        request.CreatedAtUtc.Should().Be(Now);
        request.StatusChangedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("", "metin", "ekran")]
    [InlineData("başlık", " ", "ekran")]
    [InlineData("başlık", "metin", null)]
    public void Create_Should_Require_Title_Text_And_Target(string? title, string? text, string? target)
        => CustomDevelopmentRequest.Create(
                Guid.NewGuid(), title, text, CustomDevelopmentTargetKind.Screen, target, CustomDevelopmentPriority.Low, UserId, Now)
            .Error.Code.Should().Be(CustomDevelopmentRequest.ValidationCode);

    [Fact]
    public void Create_Should_Reject_Too_Long_Text_And_Undefined_Enums()
    {
        CustomDevelopmentRequest.Create(
                Guid.NewGuid(), "b", new string('x', CustomDevelopmentRequest.MaxRequestTextLength + 1), CustomDevelopmentTargetKind.Screen,
                "e", CustomDevelopmentPriority.Low, UserId, Now)
            .IsSuccess.Should().BeFalse();
        CustomDevelopmentRequest.Create(Guid.NewGuid(), "b", "m", (CustomDevelopmentTargetKind)9, "e", CustomDevelopmentPriority.Low, UserId, Now)
            .IsSuccess.Should().BeFalse();
        CustomDevelopmentRequest.Create(Guid.NewGuid(), "b", "m", CustomDevelopmentTargetKind.Screen, "e", (CustomDevelopmentPriority)9, UserId, Now)
            .IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(CustomDevelopmentRequestStatus.Received, CustomDevelopmentRequestStatus.InReview, true)]
    [InlineData(CustomDevelopmentRequestStatus.Received, CustomDevelopmentRequestStatus.Planned, false)]
    [InlineData(CustomDevelopmentRequestStatus.Received, CustomDevelopmentRequestStatus.Completed, false)]
    [InlineData(CustomDevelopmentRequestStatus.InReview, CustomDevelopmentRequestStatus.Planned, true)]
    [InlineData(CustomDevelopmentRequestStatus.InReview, CustomDevelopmentRequestStatus.Completed, false)]
    [InlineData(CustomDevelopmentRequestStatus.Planned, CustomDevelopmentRequestStatus.Completed, true)]
    [InlineData(CustomDevelopmentRequestStatus.Planned, CustomDevelopmentRequestStatus.InReview, false)]
    [InlineData(CustomDevelopmentRequestStatus.OutOfScope, CustomDevelopmentRequestStatus.Planned, false)]
    [InlineData(CustomDevelopmentRequestStatus.Completed, CustomDevelopmentRequestStatus.Received, false)]
    public void ChangeStatus_Should_Follow_Flow(CustomDevelopmentRequestStatus from, CustomDevelopmentRequestStatus to, bool allowed)
    {
        var request = InStatus(from);

        var result = request.ChangeStatus(to, null, "not", AdminId, Now.AddHours(1));

        result.IsSuccess.Should().Be(allowed);
        if (allowed)
        {
            request.Status.Should().Be(to);
            request.StatusChangedByUserId.Should().Be(AdminId);
            request.StatusChangedAtUtc.Should().Be(Now.AddHours(1));
            request.ResponseNote.Should().Be("not");
        }
        else
        {
            result.Error.Code.Should().Be("CustomDevelopment.InvalidStatusTransition");
            request.Status.Should().Be(from);
        }
    }

    [Fact]
    public void OutOfScope_Should_Require_Reason_And_Close_Request()
    {
        var request = InStatus(CustomDevelopmentRequestStatus.InReview);

        request.ChangeStatus(CustomDevelopmentRequestStatus.OutOfScope, "  ", null, AdminId, Now).Error.Code
            .Should().Be(CustomDevelopmentRequest.ValidationCode);
        request.Status.Should().Be(CustomDevelopmentRequestStatus.InReview);

        request.ChangeStatus(CustomDevelopmentRequestStatus.OutOfScope, "  Vetinity dışında bir sistem  ", null, AdminId, Now).IsSuccess
            .Should().BeTrue();
        request.DecisionReason.Should().Be("Vetinity dışında bir sistem");
        request.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Reason_Should_Only_Be_Written_For_OutOfScope()
    {
        var request = InStatus(CustomDevelopmentRequestStatus.InReview);

        request.ChangeStatus(CustomDevelopmentRequestStatus.Planned, "gerekçe", null, AdminId, Now).Error.Code
            .Should().Be(CustomDevelopmentRequest.ValidationCode);
        request.DecisionReason.Should().BeNull();
    }

    [Fact]
    public void Empty_Note_Should_Keep_Previous_Note()
    {
        var request = New();
        request.ChangeStatus(CustomDevelopmentRequestStatus.InReview, null, "İnceliyoruz", AdminId, Now);
        request.ChangeStatus(CustomDevelopmentRequestStatus.Planned, null, null, AdminId, Now);

        request.ResponseNote.Should().Be("İnceliyoruz");
        request.MutationSequence.Should().Be(2);
        request.IsOpen.Should().BeTrue("planlanan talep tamamlanana kadar açık sayılır");
    }
}
