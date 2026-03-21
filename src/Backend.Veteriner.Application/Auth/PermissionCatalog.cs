using System.Linq;
using System;
namespace Backend.Veteriner.Application.Auth;

public sealed record PermissionDefinition(
    string Code,
    string Description,
    string Group);

public static class PermissionCatalog
{
    public static class Permissions
    {
        public const string Read = "Permissions.Read";
        public const string Write = "Permissions.Write";
    }

    public static class Users
    {
        public const string Read = "Users.Read";
        public const string Write = "Users.Write";
    }

    public static class Roles
    {
        public const string Read = "Roles.Read";
        public const string Write = "Roles.Write";
    }

    public static class Outbox
    {
        public const string Read = "Outbox.Read";
        public const string Write = "Outbox.Write";
    }

    public static class Admin
    {
        public const string Diagnostics = "Admin.Diagnostics";
    }

    public static class Test
    {
        public const string FeatureRun = "Test.Feature.Run";
        public const string ForbiddenProbe = "Test.Forbidden.Probe";
    }

    public static readonly PermissionDefinition[] All =
    {
        new(Permissions.Read, "Permission kayıtlarını görüntüleme yetkisi", "Permissions"),
        new(Permissions.Write, "Permission kayıtlarını oluşturma ve güncelleme yetkisi", "Permissions"),

        new(Users.Read, "Kullanıcıları görüntüleme yetkisi", "Users"),
        new(Users.Write, "Kullanıcı oluşturma ve güncelleme yetkisi", "Users"),

        new(Roles.Read, "Rol ve claim kayıtlarını görüntüleme yetkisi", "Roles"),
        new(Roles.Write, "Rol ve claim atama/değiştirme yetkisi", "Roles"),

        new(Outbox.Read, "Outbox kayıtlarını görüntüleme yetkisi", "Outbox"),
        new(Outbox.Write, "Outbox üzerinde yönetim işlemleri yapma yetkisi", "Outbox"),

        new(Admin.Diagnostics, "Tanılama ve yönetim kontrollerine erişim yetkisi", "Diagnostics"),

        new(Test.FeatureRun, "Test amaçlı özellikleri çalıştırma yetkisi", "Test"),
        new(Test.ForbiddenProbe, "Forbidden test endpoint", "Test"),
    };

    public static IReadOnlyCollection<string> AllCodes =>
        All.Select(x => x.Code).ToArray();

    public static bool Contains(string code) =>
        AllCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
}
