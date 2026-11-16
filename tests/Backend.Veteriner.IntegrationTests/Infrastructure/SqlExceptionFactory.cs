using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Belirli hata numarasıyla <see cref="SqlException"/> üretir (kurucular iç erişimli; yansıma ile). Kilitlenme (1205) gibi
/// geçici hataların işçilerce nasıl karşılandığını sunucuda kilitlenme kurmadan sınamak içindir.
/// </summary>
internal static class SqlExceptionFactory
{
    public static SqlException Create(int number)
    {
        var errorCtor = typeof(SqlError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var numberSet = false;
        var args = errorCtor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(int))
            {
                if (numberSet)
                    return (object)0;
                numberSet = true;
                return number;
            }

            if (p.ParameterType == typeof(byte))
                return (byte)0;
            if (p.ParameterType == typeof(uint))
                return 0u;
            if (p.ParameterType == typeof(string))
                return "test";
            return p.HasDefaultValue ? p.DefaultValue : null;
        }).ToArray();
        var error = (SqlError)errorCtor.Invoke(args);

        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection)
            .GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(SqlError)])!
            .Invoke(errors, [error]);

        var create = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(SqlErrorCollection), typeof(string)])!;
        return (SqlException)create.Invoke(null, [errors, "test"])!;
    }
}
