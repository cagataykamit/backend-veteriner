using System;
using Backend.Veteriner.Application.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Backend.Veteriner.Api.Auth
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class HasPermissionAttribute : AuthorizeAttribute
    {
        public HasPermissionAttribute(string permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
                throw new ArgumentException("Permission boş olamaz.", nameof(permission));

            if (!PermissionCatalog.Contains(permission))
                throw new InvalidOperationException(
                    $"Tanımsız permission kullanıldı: '{permission}'. PermissionCatalog içine eklenmelidir.");

            Policy = permission;
        }
    }
}