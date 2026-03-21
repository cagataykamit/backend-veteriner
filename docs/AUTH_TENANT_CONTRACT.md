# Auth ve tenant sözleşmesi (API)

Bu belge mevcut backend davranışını özetler; yeni mimari önerisi değildir.

## Login (`POST .../auth/login`)

| Alan | Zorunluluk | Açıklama |
|------|------------|----------|
| `email` | Evet | |
| `password` | Evet | |
| `tenantId` | Koşullu | Kullanıcının **birden fazla** kiracı üyeliği varsa **zorunlu** (GUID). Tek üyelikte **atlanabilir**; yanlış veya farklı GUID gönderilirse `Auth.TenantMismatch`. |

Sunucu `UserTenant` üzerinden üyeliği doğrular. Üyelik yoksa `Auth.TenantMembershipRequired` veya `Auth.TenantNotMember`; çok kiracıda seçim yoksa `Auth.TenantRequired`.

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
