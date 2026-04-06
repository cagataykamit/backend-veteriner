namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>İstekteki kullanıcının JWT <c>permission</c> claim'lerinde kod var mı.</summary>
public interface ICurrentUserPermissionChecker
{
    bool HasPermission(string permissionCode);
}
