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

    public static class Tenants
    {
        public const string Read = "Tenants.Read";
        public const string Create = "Tenants.Create";
    }

    public static class Clinics
    {
        public const string Read = "Clinics.Read";
        public const string Create = "Clinics.Create";
    }

    public static class Clients
    {
        public const string Read = "Clients.Read";
        public const string Create = "Clients.Create";
    }

    public static class Pets
    {
        public const string Read = "Pets.Read";
        public const string Create = "Pets.Create";
    }

    public static class Appointments
    {
        public const string Read = "Appointments.Read";
        public const string Create = "Appointments.Create";
        public const string Cancel = "Appointments.Cancel";
        public const string Complete = "Appointments.Complete";
        public const string Reschedule = "Appointments.Reschedule";
    }

    public static class Dashboard
    {
        public const string Read = "Dashboard.Read";
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

        new(Tenants.Read, "Kiracıları listeleme ve görüntüleme yetkisi", "Tenants"),
        new(Tenants.Create, "Kiracı oluşturma yetkisi", "Tenants"),

        new(Clinics.Read, "Klinikleri listeleme ve görüntüleme yetkisi", "Clinics"),
        new(Clinics.Create, "Klinik oluşturma yetkisi", "Clinics"),

        new(Clients.Read, "Müşterileri listeleme ve görüntüleme yetkisi", "Clients"),
        new(Clients.Create, "Müşteri oluşturma yetkisi", "Clients"),

        new(Pets.Read, "Hayvan kayıtlarını listeleme ve görüntüleme yetkisi", "Pets"),
        new(Pets.Create, "Hayvan kaydı oluşturma yetkisi", "Pets"),

        new(Appointments.Read, "Randevuları listeleme ve görüntüleme yetkisi", "Appointments"),
        new(Appointments.Create, "Randevu oluşturma yetkisi", "Appointments"),
        new(Appointments.Cancel, "Planlanmış randevuyu iptal etme yetkisi", "Appointments"),
        new(Appointments.Complete, "Planlanmış randevuyu tamamlama yetkisi", "Appointments"),
        new(Appointments.Reschedule, "Planlanmış randevuyu yeniden zamanlama yetkisi", "Appointments"),

        new(Dashboard.Read, "Klinik paneli özet dashboard verilerini görüntüleme yetkisi", "Dashboard"),

        new(Admin.Diagnostics, "Tanılama ve yönetim kontrollerine erişim yetkisi", "Diagnostics"),

        new(Test.FeatureRun, "Test amaçlı özellikleri çalıştırma yetkisi", "Test"),
        new(Test.ForbiddenProbe, "Forbidden test endpoint", "Test"),
    };

    public static IReadOnlyCollection<string> AllCodes =>
        All.Select(x => x.Code).ToArray();

    public static bool Contains(string code) =>
        AllCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
}
