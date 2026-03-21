-- =============================================================================
-- Yedek: dotnet ef database update çalışmıyorsa veya farklı DB'ye gidiyorsa
--        bu script ile tabloları elle oluşturup migration'ı "uygulanmış" sayın.
-- Kullanım: API'nin bağlandığı veritabanında çalıştırın.
-- =============================================================================

SET NOCOUNT ON;

-- Zaten varsa oluşturma (idempotent)
IF OBJECT_ID(N'dbo.Positions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Positions] (
        [Id]           UNIQUEIDENTIFIER NOT NULL,
        [Name]         NVARCHAR(200)    NOT NULL,
        [Code]         NVARCHAR(50)    NOT NULL,
        [Description]  NVARCHAR(1000)   NULL,
        [IsActive]     BIT              NOT NULL,
        [CreatedAtUtc]  DATETIME2        NOT NULL,
        [UpdatedAtUtc] DATETIME2        NULL,
        CONSTRAINT [PK_Positions] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_Positions_Code] ON [dbo].[Positions] ([Code]);
END

IF OBJECT_ID(N'dbo.OrganizationUnits', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OrganizationUnits] (
        [Id]                 UNIQUEIDENTIFIER NOT NULL,
        [Name]               NVARCHAR(200)    NOT NULL,
        [Code]               NVARCHAR(50)    NOT NULL,
        [ParentUnitId]       UNIQUEIDENTIFIER  NULL,
        [ManagerEmployeeId]  UNIQUEIDENTIFIER  NULL,
        [IsActive]           BIT              NOT NULL,
        [CreatedAtUtc]       DATETIME2        NOT NULL,
        [UpdatedAtUtc]       DATETIME2        NULL,
        CONSTRAINT [PK_OrganizationUnits] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_OrganizationUnits_Code] ON [dbo].[OrganizationUnits] ([Code]);
END

IF OBJECT_ID(N'dbo.Employees', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Employees] (
        [Id]                 UNIQUEIDENTIFIER NOT NULL,
        [FirstName]          NVARCHAR(100)   NOT NULL,
        [LastName]           NVARCHAR(100)   NOT NULL,
        [Email]              NVARCHAR(256)   NOT NULL,
        [Phone]              NVARCHAR(50)    NULL,
        [OrganizationUnitId] UNIQUEIDENTIFIER NOT NULL,
        [PositionId]         UNIQUEIDENTIFIER NOT NULL,
        [ManagerEmployeeId]   UNIQUEIDENTIFIER NULL,
        [HireDateUtc]        DATETIME2       NOT NULL,
        [IsActive]           BIT             NOT NULL,
        [CreatedAtUtc]       DATETIME2       NOT NULL,
        [UpdatedAtUtc]       DATETIME2       NULL,
        CONSTRAINT [PK_Employees] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_Employees_Email] ON [dbo].[Employees] ([Email]);
END

-- Migration'ı uygulanmış say (EF tekrar çalıştırmasın)
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260314175608_Add_Organization_Module')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260314175608_Add_Organization_Module', N'9.0.9');
END
