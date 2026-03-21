using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Backend.Veteriner.Application.Auth.Policies
{
    public static class PermissionClaimTypes
    {
        // JWT i�inde kulland���n t�rler � ikisini de kontrol edece�iz (tek tek veya CSV)
        public const string Single = "permission";   // birden �ok claim olabiliyor
        public const string Multiple = "permissions"; // CSV (�rn: "Users.Read,Users.Write")
    }
}
