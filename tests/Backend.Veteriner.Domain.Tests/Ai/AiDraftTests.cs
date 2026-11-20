using Backend.Veteriner.Domain.Ai;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Ai;

public sealed class AiDraftTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private const string Content = """{"note":"taslak"}""";

    private static AiDraft NewDraft(IEnumerable<AiSourceRef>? sources = null)
        => AiDraft.Create(
            Guid.NewGuid(), Guid.NewGuid(), AiFeature.ExamNoteStructure, " Examination ", TargetId, sources, Content,
            "claude-haiku-5-5", "exam-note/1", UserId, Now, Lifetime);

    [Fact]
    public void Create_Starts_As_Draft_With_Content_And_Expiry()
    {
        var draft = NewDraft();

        draft.Status.Should().Be(AiDraftStatus.Draft);
        draft.TargetType.Should().Be("Examination");
        draft.Content.Should().Be(Content);
        draft.ExpiresAtUtc.Should().Be(Now + Lifetime);
        draft.DecidedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Create_Rejects_Empty_Or_Oversized_Content()
    {
        var create = (string content) => AiDraft.Create(
            Guid.NewGuid(), Guid.NewGuid(), AiFeature.ExamNoteStructure, "Examination", TargetId, null, content,
            "m", "v/1", UserId, Now, Lifetime);

        Action empty = () => create(" ");
        Action oversized = () => create(new string('x', AiDraft.ContentMaxLength + 1));

        empty.Should().Throw<ArgumentException>();
        oversized.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false, AiDraftStatus.Accepted)]
    [InlineData(true, AiDraftStatus.AcceptedWithEdits)]
    public void Accept_Records_Decision_And_Clears_Content(bool wasEdited, AiDraftStatus expected)
    {
        var draft = NewDraft();
        var appliedId = Guid.NewGuid();

        draft.Accept(UserId, wasEdited, appliedId, Now.AddMinutes(5)).IsSuccess.Should().BeTrue();

        draft.Status.Should().Be(expected);
        draft.Content.Should().BeNull();
        draft.AppliedRecordId.Should().Be(appliedId);
        (draft.DecidedByUserId, draft.DecidedAtUtc).Should().Be((UserId, Now.AddMinutes(5)));
    }

    [Fact]
    public void Discard_Clears_Content_And_Blocks_Further_Decisions()
    {
        var draft = NewDraft();

        draft.Discard(UserId, Now).IsSuccess.Should().BeTrue();

        draft.Status.Should().Be(AiDraftStatus.Discarded);
        draft.Content.Should().BeNull();
        draft.Accept(UserId, false, null, Now).Error.Code.Should().Be("AiDrafts.NotPending");
        draft.Discard(UserId, Now).Error.Code.Should().Be("AiDrafts.NotPending");
    }

    [Fact]
    public void Past_Expiry_Reads_As_Expired_And_Cannot_Be_Decided()
    {
        var draft = NewDraft();
        var later = Now + Lifetime;

        draft.StatusAt(later.AddSeconds(-1)).Should().Be(AiDraftStatus.Draft);
        draft.StatusAt(later).Should().Be(AiDraftStatus.Expired);
        draft.Accept(UserId, false, null, later).Error.Code.Should().Be("AiDrafts.Expired");
        draft.Discard(UserId, later).Error.Code.Should().Be("AiDrafts.Expired");
        draft.Content.Should().NotBeNull("okuma anı durumu içeriği silmez; süre taraması siler");
    }

    [Fact]
    public void ExpireIfDue_Only_Closes_Pending_Drafts_Past_Expiry()
    {
        var draft = NewDraft();

        draft.ExpireIfDue(Now.AddDays(1)).Should().BeFalse();
        draft.ExpireIfDue(Now + Lifetime).Should().BeTrue();

        draft.Status.Should().Be(AiDraftStatus.Expired);
        draft.Content.Should().BeNull();
        draft.DecidedByUserId.Should().BeNull();
        draft.ExpireIfDue(Now + Lifetime + Lifetime).Should().BeFalse();

        var accepted = NewDraft();
        accepted.Accept(UserId, false, null, Now);
        accepted.ExpireIfDue(Now + Lifetime).Should().BeFalse();
        accepted.Status.Should().Be(AiDraftStatus.Accepted);
    }

    [Fact]
    public void Source_Refs_Keep_Only_Distinct_Ids_In_Order()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var draft = NewDraft([new("Examination", a), new(" Examination ", a), new("Pet", b), new("", b), new("Pet", Guid.Empty)]);

        draft.SourceRefs.Should().Equal(new AiSourceRef("Examination", a), new AiSourceRef("Pet", b));
    }

    [Fact]
    public void Source_Refs_Are_Capped()
    {
        var refs = Enumerable.Range(0, AiSourceRef.MaxCount + 5).Select(_ => new AiSourceRef("Examination", Guid.NewGuid()));

        AiSourceRef.Normalize(refs).Should().HaveCount(AiSourceRef.MaxCount);
    }
}
