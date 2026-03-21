namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Task integration testleri ile <see cref="TestDataSeeder"/> arasında paylaşılan sabit kimlikler.
/// </summary>
internal static class TaskIntegrationTestData
{
    public static readonly Guid PositionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000001");
    public static readonly Guid OrganizationUnitId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000002");
    public static readonly Guid CreatorEmployeeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000003");
    public static readonly Guid AssigneeEmployeeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000004");
    public static readonly Guid InactiveEmployeeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000005");
}
