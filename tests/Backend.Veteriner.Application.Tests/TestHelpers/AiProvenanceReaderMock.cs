using Backend.Veteriner.Application.Ai.Drafts;
using Moq;

namespace Backend.Veteriner.Application.Tests.TestHelpers;

/// <summary><see cref="IAiProvenanceReader"/>: hiçbir kayıtta "AI taslağından" izi yok.</summary>
internal static class AiProvenanceReaderMock
{
    public static Mock<IAiProvenanceReader> Empty()
    {
        var mock = new Mock<IAiProvenanceReader>();
        mock.Setup(r => r.ForRecordsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<AiProvenanceDto>>());
        return mock;
    }
}
