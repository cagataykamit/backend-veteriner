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
                throw new ArgumentException("Permission bo� olamaz.", nameof(permission));

            if (!PermissionCatalog.Contains(permission))
                throw new InvalidOperationException(
                    $"Tan�ms�z permission kullan�ld�: '{permission}'. PermissionCatalog i�ine eklenmelidir.");

            Policy = permission;
        }
    }
}