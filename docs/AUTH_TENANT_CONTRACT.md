# Auth ve tenant sözleşmesi (API)

Bu belge mevcut backend davranışını özetler; yeni mimari önerisi değildir.

Hata zarfı (`ProblemDetails`, `extensions.code`, validation vs. iş kuralı ayrımı) için ekip standardı: `docs/BACKEND-CONTRACT-STANDARD.md` §9.

## Login (`POST .../auth/login`)

| Alan | Zorunluluk | Açıklama |
|------|------------|----------|
| `email` | Evet | |
| `password` | Evet | |
| `tenantId` | İsteğe bağlı | Nihai model: kullanıcı **tek kiracılı** (`UserTenants.UserId` benzersiz). Gövdede yok/aynı kiracı kabul; farklı GUID `Auth.TenantMismatch`. Veride birden fazla kiracı üyeliği kalmışsa `Auth.UserMultipleTenantsForbidden`. |

Sunucu `UserTenant` üzerinden üyeliği doğrular. Üyelik yoksa `Auth.TenantMembershipRequired`; üyelik var ama token kiracısı doğrulanamazsa `Auth.TenantNotMember`.

Başarılı yanıtta: `accessToken`, `refreshToken`, `expiresAt`, `resolvedTenantId`, login cevabında `tenantMembershipCount` her zaman `1` (tek kiracı). Refresh/select-clinic yanıtlarında `tenantMembershipCount` genelde `null`; `resolvedTenantId` oturum kiracısıdır.

**Klinik ataması** yalnız `UserClinics` ile; seed otomatik olarak tenant’taki tüm klinikleri kullanıcıya **eklemez** (yalnızca `DataSeeder` içinde adlı varsayılan klinik + admin ataması).

## Refresh (`POST .../auth/refresh`)

İstek gövdesi yalnızca:

```json
{ "refreshToken": "<ham refresh token>" }
```

Kiracı **istekte taşınmaz**; yeni access token, sunucudaki refresh kaydındaki `TenantId` ile üretilir. Eski kayıtlarda `TenantId` yoksa veya oturum geçersizse ilgili hata kodu döner (handler: `RefreshCommandHandler`).

## JWT

Access token içinde `tenant_id` claim’i, login/refresh sırasında çözümlenen kiracı için üretilir.

## Tenant-scoped API’ler

Kiracı bağlamı **`ITenantContext`** (JWT `tenant_id` ve gerektiğinde sorgu `tenantId` çözümlemesi) üzerinden gelir; create/list/get için gövde `tenantId` beklenmez (ilgili komutlar buna göre güncellenmiştir).

## Ürün / politika (kod dışı)

Aşağıdakiler ayrı karar gerektirir ve bu dosyada yalnızca not olarak tutulur:

- Platform admin hangi endpoint’lerde tenant bypass yapabilir.
- Çok kiracılı kullanıcıda arayüzde tenant seçimi nasıl yapılır (login `tenantId` vs sonradan bağlam değişimi).
- Eski refresh satırları için toplu backfill mi, yalnızca re-login mi.
