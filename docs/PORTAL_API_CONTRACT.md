# Hasta sahibi portalı — API sözleşmesi (ADR-031 v0)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-031-hasta-sahibi-portali-ve-mobil-uygulama.md` (K3, K4, K6; AK-2 giriş ikisi birden, AK-3 tek hesap çok klinik, AK-6 v0 SMS yok, AK-8, AK-9), ADR-029 (sürümlü kabul), ADR-025 (KVKK Ret → hatırlatma kapalı), ADR-021/024 (aydınlatma metni). Hata zarfı standart `ProblemDetails` + `extensions.code` (`BACKEND-CONTRACT-STANDARD.md` §9).

> **Durum:** Dilim **A** (hesap, davet, giriş, güvenlik temeli) uygulandı, dal `feat/portal-a-hesap-giris`. Dilim **B** (okuma uçları) bu belgede **taslak**tır; B ajanı ve portal frontend'i bu şekle uyar, değişiklik önce burada yapılır. Migration `AddOwnerPortalAccounts` üretildi, **hiçbir paylaşılan/geliştirme veritabanına uygulanmadı**. Yeni izin `OwnerPortal.Manage` kataloğa ve rol bağlarına eklendi; seed çalıştırılmadı.

İşaretler: **[A]** uygulandı · **[B]** taslak, sonraki dilim · **[v1]** v0 dışında, taslak yer tutucu.

## 1) Yüzeyler ve kimlik

| Yüzey | Yol öneki | Kimlik | Kim |
|---|---|---|---|
| Klinik paneli (personel) | `/api/v1/...` | Mevcut personel JWT (varsayılan şema, `Jwt:Audience`) + `OwnerPortal.Manage` | Klinik çalışanı |
| Hasta sahibi portalı | `/api/portal/v1/...` | **Ayrı şema** `PortalBearer`, **ayrı hedef kitle** `OwnerPortal:Audience` (varsayılan `Backend.Veteriner.Portal`) | Hasta sahibi (`OwnerAccount`) |

- Portal access token'ı: `aud` = portal hedef kitlesi, `sub` = `OwnerAccountId`, `principal_type` = `owner`, `email`. İzin (`permission`), `tenant_id`, `clinic_id` claim'i **taşımaz**.
- **Personel token'ı portal uçlarında 401**, **portal token'ı personel uçlarında 401** (hedef kitle uyuşmaz; portal politikası ayrıca `principal_type=owner` ister). Entegrasyon testiyle sabittir.
- `OwnerPortal:Audience`, `Jwt:Audience` ile aynı olamaz (açılışta hata). İmza anahtarı ve `iss` personelle ortaktır (`Jwt:Key`, `Jwt:Issuer`).
- Süreler (`OwnerPortal` bölümü): access **15 dk** (`AccessTokenMinutes`), refresh **30 gün** (`RefreshTokenDays`), davet **7 gün** (`InviteExpiryDays`).
- Portal oturumu kiracıya bağlı değildir (bir hesap birden çok klinik/müşteri kaydına bağlıdır, AK-3). Kiracı/klinik/müşteri her istekte **bağ** (`linkId`) üzerinden sunucuda çözülür; token'da bağ listesi yoktur, böylece klinik erişimi kapatınca etki anında olur.
- Portal DTO'ları personel DTO'larını yeniden kullanmaz; her alan bu belgede seçilir.
- Enum alanları JSON'da **metin** (ADR-029 / KVKK uçlarıyla aynı).

## 2) Alan modeli

| Tablo | Anlam | Not |
|---|---|---|
| `OwnerAccounts` | Kişi başına tek hesap | `Email` benzersiz (trim + küçük harf), `PasswordHash` opsiyonel (BCrypt, personelle aynı hasher), `DisplayName`, `Status` (`Active`/`Disabled`), `CreatedAtUtc`, `LastLoginAtUtc`, `FailedSignInCount`, `LockedUntilUtc` |
| `OwnerAccountClientLinks` | Hesap ↔ `Client` bağı | `OwnerAccountId`, `TenantId`, `ClinicId` (daveti gönderen klinik; aydınlatma metni bu kliniğin), `ClientId`, `Status` (`Active`/`Closed`), `CreatedAtUtc`, `CreatedByUserId`, `ClosedAtUtc`, `ClosedByUserId`, `DisclosureAcknowledgedAtUtc`. Bir müşteri kaydında **en fazla bir aktif bağ** (filtreli benzersiz indeks). Kapatılan bağ silinmez |
| `OwnerPortalInvites` | Davet | `TenantId`, `ClinicId`, `ClientId`, `Email` (gönderim anındaki müşteri e-postası), `TokenHash` (SHA-256; ham token yalnız e-postadadır, saklanmaz/loglanmaz), `Status` (`Pending`/`Accepted`/`Revoked`), `ExpiresAtUtc`, `CreatedAtUtc`, `CreatedByUserId`, `AcceptedAtUtc`, `AcceptedOwnerAccountId`, `RevokedAtUtc`, `RevokedByUserId` |
| `OwnerPortalCodes` | Tek kullanımlık e-posta kodu | `OwnerAccountId`, `Purpose` (`SignIn`/`PasswordReset`), `CodeHash` (kod satır kimliğiyle tuzlanmış SHA-256), `ExpiresAtUtc`, `FailedAttempts`, `ConsumedAtUtc`, `InvalidatedAtUtc` |
| `OwnerPortalRefreshTokens` | Portal oturumu | Personel `RefreshTokens` tablosundan **ayrı**; `TokenHash`, `ExpiresAtUtc`, `LastUsedAtUtc`, `RevokedAtUtc`, `RevokeReason`, `ReplacedByTokenHash`. IP/cihaz tutulmaz (veri azaltma) |
| `LegalAcceptances` (genişletildi) | ADR-029 kabul kaydı | Yeni `OwnerAccountId` sütunu; `UserId` ve `TenantId` artık boş olabilir. Satırda **ya** `UserId` **ya** `OwnerAccountId` dolu (CHECK). Personel kayıtları değişmez |

## 3) Güvenlik kuralları (tek yer: `Domain/OwnerPortal`)

| Kural | Değer | Yer |
|---|---|---|
| Davet uygunluğu | Peşin müşteri değil, e-postası dolu, **KVKK Ret değil ve `RemindersOptedOut` kapalı** (ADR-025/031 K4) | `OwnerPortalInvitePolicy.CanInvite` |
| Davet | Tek kullanımlık, 7 gün; aynı müşteriye yeni davet önceki bekleyeni iptal eder; kabulde müşteri e-postası davet e-postasıyla hâlâ aynı ve müşteri hâlâ uygun olmalı (e-posta değişince eski davet geçersiz, K3) | `OwnerPortalInvite` |
| Kod | 6 hane, 10 dk, en fazla 5 yanlış deneme (sonra geçersiz), tek kullanımlık; yeni kod aynı amaçtaki eskisini geçersiz kılar; 60 sn içinde ikinci kod üretilmez | `OwnerPortalCode` |
| Kilit | Hesapta art arda **5** başarısız giriş (yanlış parola veya kod) → **15 dk** kilit; kilitliyken doğru bilgi de reddedilir; başarılı giriş sayacı sıfırlar | `OwnerAccount` |
| Parola | Personelle aynı güçlü parola kuralı (en az 8, büyük/küçük harf, rakam, özel karakter; en fazla 128) | `StrongPasswordRules` |
| Numaralandırma | Kod/şifre sıfırlama isteği hesap var/yok her durumda **204**; giriş hatası, kilit ve yanlış kod **aynı** yanıt: 401 `Portal.Auth.Unauthorized.InvalidCredentials` | Handler'lar |
| Hız sınırı (IP) | `portal-auth` 10/dk (anonim kimlik uçları), `portal-refresh` ~30/dk, `portal` 120/dk (IP + hesap, oturumlu uçlar). Hesap bazlı koruma kilit + kod bekleme süresidir (veritabanında) | `RateLimitingExtensions` |
| Refresh | Her yenilemede döndürülür; iptal edilmiş token tekrar kullanılırsa hesabın **tüm** portal oturumları kapanır | `RefreshPortalSessionCommandHandler` |
| Sahiplik | Her bağ kapsamlı uç `OwnerPortalAccess.ResolveLinkAsync`: bağ bu hesaba ait, `Active`, kiracı aktif; değilse **404** `Portal.Links.NotFound` (başka hesabın/kiracının bağı ile ayırt edilemez) | `OwnerPortalAccess` |
| Denetim | AuditBehavior: davet oluşturma/iptal, bağ kapatma, davet kabulü, kod isteği, kodla/parolayla giriş, şifre sıfırlama, çıkış, kabul, aydınlatma onayı. Parola, token, `oneTimeCode` maskelenir. Portal isteklerinde `ActorUserId` boştur (personel kimliğiyle karışmaz); hedef `OwnerAccountId=...` | `AuditBehavior` |

## 4) Klinik paneli uçları [A]

Hepsi personel JWT + `[Authorize(Policy = "OwnerPortal.Manage")]`, kiracı JWT `tenant_id`'den. İzin rolleri: `Admin`, `Owner`, `ClinicAdmin` (`RolePermissionBindings`). Klinik ataması olan (tenant-wide olmayan) kullanıcı yalnız atandığı kliniğin daveti/bağı üzerinde işlem yapar (`Clinics.AccessDenied` 403).

### 4.1 `GET /api/v1/clients/{clientId}/portal-access`

Müşteri kartındaki "Portal" bölümü: bağ durumu ve davet listesi (son 20 davet, en yeni önce; müşteri başına küçük liste, sayfalama yok).

```json
{
  "clientId": "…",
  "canInvite": false,
  "inviteBlockedCode": "OwnerPortal.ClientOptedOut",
  "links": [
    { "linkId": "…", "clinicId": "…", "clinicName": "Merkez", "status": "Active", "accountEmail": "a@b.com",
      "linkedAtUtc": "2026-10-16T10:00:00Z", "closedAtUtc": null }
  ],
  "invites": [
    { "inviteId": "…", "clinicId": "…", "email": "a@b.com", "status": "Pending",
      "createdAtUtc": "…", "expiresAtUtc": "…", "acceptedAtUtc": null, "revokedAtUtc": null }
  ]
}
```

- `canInvite`: müşteri davet kuralına uyuyor **ve** aktif bağı yok. Değilse `inviteBlockedCode` (`OwnerPortal.ClientEmailMissing`, `OwnerPortal.ClientOptedOut`, `OwnerPortal.ClientNotInvitable`, `OwnerPortal.AlreadyLinked`).
- Davet `status`: `Pending`, `Accepted`, `Revoked`, `Expired` (bekleyen ama süresi geçmiş; hesaplanır).

### 4.2 `POST /api/v1/clients/{clientId}/portal-invites`

Gövde (opsiyonel): `{ "clinicId": "…" }`. Klinik **context-first**: JWT `clinic_id` / `X-Clinic-Id`; gövdedeki farklıysa 400 `OwnerPortal.ClinicContextMismatch`; hiçbiri yoksa 400 `OwnerPortal.ClinicRequired`.

- Davet e-postası **müşteri kaydındaki e-postaya** gider (outbox, mevcut MailKit hattı); bağlantı `{OwnerPortal:PortalBaseUrl}/invite?token=…` (boşsa `{App:FrontendBaseUrl}/portal`). Gövdede klinik adı (marka, AK-10).
- Ham token **yanıtta dönmez**; personel görmez.
- Aynı müşterinin bekleyen daveti varsa iptal edilir (yeni davet onun yerine geçer).

Yanıt **200**: 4.1'deki `invites[]` öğesi şekli.

| Kod | HTTP | Durum |
|---|---|---|
| `Clients.NotFound` | 404 | Müşteri bu kiracıda yok |
| `OwnerPortal.ClientEmailMissing` | 409 | Müşteri kaydında e-posta yok |
| `OwnerPortal.ClientOptedOut` | 409 | KVKK Ret veya hatırlatma istemiyor (ADR-025) — davet gitmez |
| `OwnerPortal.ClientNotInvitable` | 409 | Peşin müşteri (sistem kaydı) |
| `OwnerPortal.AlreadyLinked` | 409 | Müşterinin aktif portal bağı var |
| `OwnerPortal.ClinicRequired` / `OwnerPortal.ClinicContextMismatch` | 400 | Klinik bağlamı yok / uyuşmuyor |
| `OwnerPortal.ClinicInvalid` | 400 | Klinik bulunamadı veya pasif |
| `Clinics.AccessDenied` | 403 | Kliniğe atanmamış kullanıcı |
| `Tenants.ContextMissing` / `Tenants.TenantInactive` | 400 / 403 | Kiracı yok / pasif |

Abonelik salt okunurken yazma kapısı (`Subscriptions.*`) uygulanır.

### 4.3 `POST /api/v1/clients/{clientId}/portal-invites/{inviteId}/revoke`

Bekleyen daveti iptal eder. **204**. `OwnerPortal.InviteNotFound` (404), `OwnerPortal.InviteNotPending` (409: kabul edilmiş/iptal). Abonelik salt okunurken de çalışır.

### 4.4 `POST /api/v1/clients/{clientId}/portal-links/{linkId}/close`

Erişimi kapatır (tek tıkla, K3). Bağ `Closed` olur, silinmez; hasta sahibi o bağa ait uçlarda 404 alır, hesabı ve başka kliniklerdeki bağları açık kalır. **204**. `OwnerPortal.LinkNotFound` (404), `OwnerPortal.LinkAlreadyClosed` (409). Abonelik salt okunurken de çalışır (KVKK güvenli taraf).

## 5) Portal uçları — Dilim A [A]

Anonim uçlar `[AllowAnonymous]` + `portal-auth` hız sınırı. Oturumlu uçlar portal şeması + `portal` hız sınırı.

### 5.1 Token yanıtı (`PortalAuthTokensDto`)

```json
{ "accessToken": "…", "accessTokenExpiresAtUtc": "…", "refreshToken": "…", "refreshTokenExpiresAtUtc": "…" }
```

### 5.2 `GET /api/portal/v1/invites/{token}` (anonim)

Davet önizlemesi.

```json
{ "clinicName": "Merkez", "emailMasked": "a***@example.com", "expiresAtUtc": "…", "hasAccount": false,
  "requiredLegalDocuments": [ { "documentType": "PortalTerms", "version": "0.1" }, { "documentType": "PortalPrivacy", "version": "0.1" } ] }
```

- `hasAccount`: bu e-postayla portal hesabı var (davet kabulü mevcut hesaba bağlanır). Bilgi yalnız e-postaya giden token sahibine görünür.
- `requiredLegalDocuments`: hesap yoksa iki portal belgesi; varsa yalnız bekleyenler.
- Geçersiz/süresi dolmuş/iptal/kullanılmış davet, değişmiş müşteri e-postası veya sonradan uygunluğunu yitirmiş müşteri: **404** `Portal.Invites.NotFound` (sebep ayırt edilmez).

### 5.3 `POST /api/portal/v1/invites/{token}/accept` (anonim)

```json
{ "displayName": "Ayşe", "password": "…", "acceptedLegalDocuments": [ { "documentType": "PortalTerms", "version": "0.1" }, { "documentType": "PortalPrivacy", "version": "0.1" } ] }
```

- Hesap yoksa oluşturulur (`displayName` boşsa müşteri adı; `password` **opsiyonel**: verilirse güçlü parola kuralı, verilmezse kodla giriş). Hesap varsa davet ona bağlanır; `password` yalnız hesabın parolası yoksa belirlenir.
- E-posta kontrolü davet bağlantısının kendisidir (bağlantı müşteri e-postasına gider; v0'da SMS yok, AK-6). Ayrıca kod istenmez.
- Bekleyen portal belgelerinin güncel sürümü zorunlu (ADR-029 mekanizması; satır `OwnerAccountId` ile).
- Tek işlemde: hesap, bağ (`Active`, davetin klinik/kiracı/müşterisi), davet `Accepted`, kabul satırları, oturum. Başarısızsa hiçbiri kalmaz.

Yanıt **200**: `{ "linkId": "…", …PortalAuthTokensDto }`.

| Kod | HTTP | Durum |
|---|---|---|
| `Portal.Invites.NotFound` | 404 | 5.2 ile aynı |
| `LegalAcceptances.Required` / `LegalAcceptances.VersionOutdated` | 400 / 409 | ADR-029 ile aynı |
| `Portal.Invites.PasswordAlreadySet` | 409 | Mevcut hesabın parolası var; gövdede parola gönderilmemeli |
| `OwnerPortal.AlreadyLinked` | 409 | Müşteri bu arada başka hesaba bağlandı |
| (validation) | 400 | Zayıf parola, `displayName` > 100 |

### 5.4 Kodla giriş

`POST /api/portal/v1/auth/code` — `{ "email": "…" }` → **204** her durumda. Hesap varsa ve aktifse, bekleme süresi dışındaysa 6 haneli kod e-postayla gider.

`POST /api/portal/v1/auth/code/verify` — `{ "email": "…", "oneTimeCode": "123456" }` → **200** `PortalAuthTokensDto`; aksi **401** `Portal.Auth.Unauthorized.InvalidCredentials` (hesap yok, kod yanlış/süresi dolmuş/kullanılmış, deneme hakkı bitti, hesap kilitli).

### 5.5 Parolayla giriş

`POST /api/portal/v1/auth/login` — `{ "email": "…", "password": "…" }` → **200** `PortalAuthTokensDto`; aksi **401** `Portal.Auth.Unauthorized.InvalidCredentials` (parolası olmayan hesap dahil).

### 5.6 Parola sıfırlama (kod ile)

`POST /api/portal/v1/auth/password-reset` — `{ "email": "…" }` → **204** her durumda (kod amacı `PasswordReset`).

`POST /api/portal/v1/auth/password-reset/confirm` — `{ "email": "…", "oneTimeCode": "123456", "newPassword": "…" }` → **204**. Başarıda parola yazılır, kilit sıfırlanır, hesabın **tüm portal oturumları kapanır**. Kod/kilit hatası **401** `Portal.Auth.Unauthorized.InvalidCredentials`; zayıf parola 400 (validation). Parolası olmayan hesap bu akışla parola belirleyebilir.

### 5.7 Oturum

| Uç | Gövde | Yanıt |
|---|---|---|
| `POST /api/portal/v1/auth/refresh` (anonim, `portal-refresh`) | `{ "refreshToken": "…" }` | 200 `PortalAuthTokensDto`; 401 `Portal.Auth.Unauthorized.InvalidRefreshToken` (yok/süresi dolmuş/hesap pasif), 401 `Portal.Auth.Unauthorized.RefreshTokenReused` (tüm oturumlar kapatıldı) |
| `POST /api/portal/v1/auth/logout` | `{ "refreshToken": "…" }` | 204 (başka hesabın token'ı veya boş gövde no-op) |
| `POST /api/portal/v1/auth/logout-all` | — | 204, hesabın tüm portal oturumları ("tüm cihazlardan çıkış") |

Access token kısa ömürlüdür; çıkışta sunucu access token'ı kara listeye almaz (en fazla 15 dk geçerli kalır, personel deseniyle aynı).

### 5.8 `GET /api/portal/v1/me`

```json
{ "id": "…", "email": "a@b.com", "displayName": "Ayşe", "hasPassword": false,
  "links": [ { "linkId": "…", "clinicId": "…", "clinicName": "Merkez", "clientFullName": "Ayşe Yılmaz" } ] }
```

`links`: yalnız aktif bağlar, aktif kiracılarda. v0 istemcisi tek klinik gösterebilir (AK-3 notu); model çoklu.

### 5.9 Hukuki kabul ve aydınlatma (ADR-029 genişletmesi, ADR-031 K4)

Belge türleri (`documentType`, tek yer `LegalDocumentCatalog`):

| `documentType` | Belge | Kim | Sürüm |
|---|---|---|---|
| `PortalTerms` | Kullanım Şartları — hasta sahibi varyantı | Her portal hesabı | `0.1` (taslak) |
| `PortalPrivacy` | Gizlilik Politikası — hasta sahibi varyantı | Her portal hesabı | `0.1` (taslak) |

Personel belgeleri (`Terms`, `Privacy`, `DataProcessing`) ve personel uçları değişmedi; `GET /api/v1/public/legal-documents` yine yalnız personel belgelerini döner. Portal belgesi kabul satırında `OwnerAccountId` dolu, `UserId` ve `TenantId` boştur (hesap kiracılar üstüdür).

`GET /api/portal/v1/legal-documents` (anonim) → `[{ "documentType": "PortalTerms", "version": "0.1" }, { "documentType": "PortalPrivacy", "version": "0.1" }]`.

`GET /api/portal/v1/me/legal-acceptances`:

```json
{ "requiresAcceptance": true,
  "pendingDocuments": [ { "documentType": "PortalTerms", "version": "0.1" } ],
  "pendingClinicDisclosures": [ { "linkId": "…", "clinicId": "…", "clinicName": "Merkez" } ] }
```

- `requiresAcceptance`: bekleyen belge var. `pendingClinicDisclosures`: aydınlatma metni henüz onaylanmamış aktif bağlar. İstemci ikisi boşalana dek portalı engeller (sunucu diğer uçları engellemez; ADR-029 madde 5 ile aynı).

`POST /api/portal/v1/me/legal-acceptances` — `{ "documents": [ … ] }`. Personel ucuyla aynı kurallar: bekleyen her belge için yeni satır; gerekmeyen/güncel olanlar yok sayılır; eksik → 400 `LegalAcceptances.Required`; eski sürüm → 409 `LegalAcceptances.VersionOutdated`. Yanıt 200, GET ile aynı şekil.

`GET /api/portal/v1/links/{linkId}/clinic-disclosure` — kliniğin aydınlatma metni (mevcut `kvkk-disclosure-template` kaydı; yoksa taslak varsayılan):

```json
{ "linkId": "…", "clinicId": "…", "clinicName": "Merkez", "text": "…", "isDefault": true, "acknowledgedAtUtc": null }
```

`{{clinicName}}`, `{{clientName}}`, `{{date}}` yer tutucuları **sunucuda** doldurulur (tarih İstanbul günü, `dd.MM.yyyy`).

`POST /api/portal/v1/links/{linkId}/clinic-disclosure/acknowledge` → **204**; ilk onay zamanı bağa yazılır, tekrar çağrı değiştirmez (bilgilendirme kaydı; açık rıza değildir).

Hatalar: bağ bu hesabın değil/kapalı → 404 `Portal.Links.NotFound`.

## 6) Portal uçları — Dilim B [B] (taslak)

Tüm uçlar oturumlu, `portal` hız sınırı, **`OwnerPortalAccess.ResolveLinkAsync(ownerAccountId, linkId)` ile başlar**, sorgular bağın `TenantId` + `ClientId` (+ klinik bilgisi için `ClinicId`) filtresiyle çalışır. Genel arama/liste ucu yoktur; yalnız kimlikle erişim. Her başarılı okuma denetim kaydına yazılır (kim, ne zaman, hangi kayıt; **içerik değil**, K4) — B dilimi `IAuditableRequest`'i okuma sorgularında da uygular, eylem adları `Portal.Read.*`. Başka hesabın/kiracının/müşterinin kaydı: **404** (`Portal.Links.NotFound`, `Portal.Pets.NotFound`). Vefat etmiş hasta listede görünür, `status` ile işaretlidir (öneri; B ajanı ürün kararı gerekirse sorar).

| Uç | Yanıt (alanlar) | Sayfalama |
|---|---|---|
| `GET /api/portal/v1/links/{linkId}/clinic` | `{ clinicId, name, phone, email, address, city, workingHours: [{ dayOfWeek, opensAt, closesAt, isClosed }] }` | — |
| `GET /api/portal/v1/links/{linkId}/pets` | `[{ petId, name, speciesName, breedName, gender, birthDate, microchipNumber, weightKg, status }]` | yok (müşteri başına küçük) |
| `GET /api/portal/v1/links/{linkId}/pets/{petId}` | Liste öğesi + `isNeutered`, `colorName` | — |
| `GET /api/portal/v1/links/{linkId}/appointments?scope=upcoming\|past&page=1&pageSize=20` | `PagedResult` `{ items: [{ appointmentId, petId, petName, scheduledAtUtc, durationMinutes, appointmentType, status }], page, pageSize, totalCount }`. `upcoming`: bugünden sonra, artan; `past`: son 12 ay, azalan. Notlar dönmez | `pageSize` en fazla 50 |
| `GET /api/portal/v1/links/{linkId}/pets/{petId}/vaccinations` | `{ history: [{ vaccinationId, vaccineName, appliedAtUtc }], upcoming: [{ vaccinationId, vaccineName, dueAtUtc, isOverdue }] }` — gecikme tanımı ADR-028 (backend tek kural). Notlar dönmez | yok |
| `GET /api/portal/v1/links/{linkId}/balance` | `{ currency, totalCharged, totalPaid, outstanding, lastPaymentAtUtc }` — yalnız tutar/durum (AK-9); kalem, açıklama, fatura yok. Bağın kliniğiyle sınırlı | — |
| `GET /api/portal/v1/links/{linkId}/pets/{petId}/prescriptions` **[v1]** | `[{ prescriptionId, issuedAtUtc, items: [{ name, dosageText }] }]` sade dil | AK-8 (b) kararı; sıralama notu "v1" onay bekliyor, klinik bazlı açma anahtarı + hukuki görüş (AK-5) |
| `GET /api/portal/v1/links/{linkId}/pets/{petId}/treatments` **[v1]** | `[{ treatmentId, title, startedOn, followUpOn }]` sade özet; muayene notu/lab/görüntü **yok** | aynı |

B için ortak hata: `Portal.Links.NotFound` 404, `Portal.Pets.NotFound` 404, validation 400 (`scope`, `pageSize`).

## 7) Uç tablosu (özet)

| Yöntem | Yol | Dilim | Kimlik |
|---|---|---|---|
| GET | `/api/v1/clients/{clientId}/portal-access` | A | Personel + `OwnerPortal.Manage` |
| POST | `/api/v1/clients/{clientId}/portal-invites` | A | Personel + `OwnerPortal.Manage` |
| POST | `/api/v1/clients/{clientId}/portal-invites/{inviteId}/revoke` | A | Personel + `OwnerPortal.Manage` |
| POST | `/api/v1/clients/{clientId}/portal-links/{linkId}/close` | A | Personel + `OwnerPortal.Manage` |
| GET | `/api/portal/v1/legal-documents` | A | Anonim |
| GET | `/api/portal/v1/invites/{token}` | A | Anonim |
| POST | `/api/portal/v1/invites/{token}/accept` | A | Anonim |
| POST | `/api/portal/v1/auth/code` | A | Anonim |
| POST | `/api/portal/v1/auth/code/verify` | A | Anonim |
| POST | `/api/portal/v1/auth/login` | A | Anonim |
| POST | `/api/portal/v1/auth/password-reset` | A | Anonim |
| POST | `/api/portal/v1/auth/password-reset/confirm` | A | Anonim |
| POST | `/api/portal/v1/auth/refresh` | A | Anonim (refresh token) |
| POST | `/api/portal/v1/auth/logout` | A | Portal |
| POST | `/api/portal/v1/auth/logout-all` | A | Portal |
| GET | `/api/portal/v1/me` | A | Portal |
| GET/POST | `/api/portal/v1/me/legal-acceptances` | A | Portal |
| GET | `/api/portal/v1/links/{linkId}/clinic-disclosure` | A | Portal |
| POST | `/api/portal/v1/links/{linkId}/clinic-disclosure/acknowledge` | A | Portal |
| GET | `/api/portal/v1/links/{linkId}/clinic`, `/pets`, `/pets/{petId}`, `/appointments`, `/pets/{petId}/vaccinations`, `/balance` | B | Portal |
| GET | `/api/portal/v1/links/{linkId}/pets/{petId}/prescriptions`, `/treatments` | v1 | Portal |

## 8) Yapılandırma (`OwnerPortal` bölümü)

| Anahtar | Varsayılan | Not |
|---|---|---|
| `Audience` | `Backend.Veteriner.Portal` | `Jwt:Audience`'tan farklı olmalı |
| `AccessTokenMinutes` | 15 | |
| `RefreshTokenDays` | 30 | |
| `InviteExpiryDays` | 7 | ADR-031 K3 |
| `PortalBaseUrl` | boş → `{App:FrontendBaseUrl}/portal` | Davet bağlantısı kökü |

## 9) Kapsam dışı (v0)

SMS ve telefon doğrulaması (AK-6), push, hesap silme/veri talebi ucu (K4, v1), portal bayrağı ile klinik bazında kapatma (K8), personel tarafı kiracı geneli davet listesi ekranı, oturum listesi ekranı, access token kara listesi.
