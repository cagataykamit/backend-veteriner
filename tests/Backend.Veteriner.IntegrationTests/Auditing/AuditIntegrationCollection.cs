namespace Backend.IntegrationTests.Auditing;

/// <summary>
/// Audit uçtan uca testleri paylaşılan auth ve sıralı çalıştırma ile stabil tutulur.
/// </summary>
[CollectionDefinition("audit-integration", DisableParallelization = true)]
public sealed class AuditIntegrationCollection : ICollectionFixture<AuditAuthFixture>
{
}
