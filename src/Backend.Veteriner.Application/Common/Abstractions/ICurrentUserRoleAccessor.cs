namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>İstekteki kullanıcının role claim değerlerini döner.</summary>
public interface ICurrentUserRoleAccessor
{
    IReadOnlyList<string> GetRoleNames();
}
