# Controller Katmanı Result Standardizasyonu – Denetim Raporu

> **Veteriner reposu:** **ContactController** bu depoda yoktur; ilgili bölümler şablon analizinden kalma örnektir. [`docs/README.md`](README.md).

**Tarih:** 2025-03-11  
**Kapsam:** Auth closure / cleanup sprint – Controller’da Result/Result&lt;T&gt; ve HTTP mapping tutarlılığı

---

## 1. Genel Değerlendirme

- **Controller katmanı büyük ölçüde standarda uyuyor.** Result dönen tüm handler’lar (Login, Refresh, GetPermissionById, Create/Update/Delete Permission, ListSessions, RevokeSession, RevokeAllSessions, AdminCreateUser) `ToActionResult(this)` ile tek tip HTTP mapping kullanıyor. Create endpoint’lerde 201 + `CreatedAtAction` doğru kullanılmış (PermissionsController, UsersAdminController).
- **Ana problem alanları:**
  1. **UsersAdminController.GetById** – Query `AdminUserDetailDto?` döndüğü için controller’da **manuel 404** (`if (user is null) return NotFound()`). Aynı tip “bulunamadı” senaryosu Permission.GetById’de Result + ToActionResult ile 404 + ProblemDetails; burada ise ham NotFound() → tutarsızlık ve ProblemDetails/code yok.
  2. **UserOperationClaimsController.Assign** – Handler `IRequest<Guid>` dönüyor; duplicate durumda `Guid.Empty` döndürüyor, controller her durumda **201 Created** + `{ id }` veriyor. Yani “zaten var” senaryosu 201 + empty guid ile dönüyor; diğer create’lerde duplicate → 409 standardı var. Ayrıca hata senaryoları (user/claim yok) exception’a kalıyor; Result standardı yok.
  3. **ContactController** – Handler `IRequest` (Unit); Result yok. Başarıda 200 OK kullanımı iletişim formu için kabul edilebilir. Tek tutarsızlık: null body için **manuel** `BadRequest(new ProblemDetails { ... })`; AuthController’daki gibi `Problem(...)` ile ortak formata çekilebilir (düşük öncelik).

Diğer controller’lar (Auth, Me, Permissions, OperationClaimPermissions, UserClaimsAdmin, UserPermissions) ya tamamen Result + ToActionResult kullanıyor ya da Unit/value dönen, idempotent/read-only handler’larla çalışıyor; **gereksiz refactor gerekmiyor**.

---

## 2. Dosya Bazlı Analiz

### AuthController.cs
- **Mevcut durum:** Login/Refresh: `Result<LoginResultDto>` → `result.ToActionResult(this)`. Logout/LogoutAll: `IRequest<Unit>`, başarıda `Ok(new { ok = true })`. Null body / eksik refreshToken için `Problem(400...)`, geçersiz JWT için `Unauthorized()`.
- **Tespit:** Result dönen yerler merkezi standarda uygun. Unauthorized/Problem kullanımı istek sınırı (boundary) ve auth doğrulama; business failure değil.
- **Risk:** Düşük.
- **Öneri:** Değişiklik yok.

---

### ContactController.cs
- **Mevcut durum:** `SendContactMessageCommand` → `IRequest` (Unit). Null body → `BadRequest(new ProblemDetails { ... })`. Başarı → `Ok(new { ok = true, message = "..." })`.
- **Tespit:** Handler Result dönmüyor; 200 OK form gönderimi için uygun. Tek küçük tutarsızlık: null body için kendi ProblemDetails’i; AuthController’da `Problem(statusCode, title, detail)` kullanılıyor.
- **Risk:** Düşük.
- **Öneri:** İsteğe bağlı: null body için `return Problem(StatusCodes.Status400BadRequest, "Geçersiz istek", "İstek gövdesi boş veya geçersiz JSON.");` ile standart Problem formatına geçilebilir. Zorunlu değil.

---

### PasswordController.cs
- **Mevcut durum:** RequestReset / Confirm → `IRequest<Unit>`, sonuç kullanılmıyor, `Ok(new { ok = true })`.
- **Tespit:** Komutlar Unit dönüyor; başarı/hata pipeline’da exception ile. Result standardına taşımak Application değişikliği; controller sadece “fire-and-forget” 200 dönüyor.
- **Risk:** Düşük.
- **Öneri:** Dokunma. İleride password reset’te business hata kodları (ör. “token expired”) istenirse Application’da Result’a geçilir, controller’da `result.ToActionResult(this)` eklenir.

---

### MeController.cs
- **Mevcut durum:** ListSessions → `Result<IReadOnlyList<SessionDto>>` → `ToActionResult`. RevokeSession → `Result` → `ToActionResult`. RevokeAllSessions → `Result` → `ToActionResult`; userId yoksa `Unauthorized()` (auth boundary).
- **Tespit:** Result kullanan tüm akışlar merkezi extension ile. Unauthorized sadece JWT/sub eksikliği için.
- **Risk:** Düşük.
- **Öneri:** Değişiklik yok.

---

### Admin/PermissionsController.cs
- **Mevcut durum:** GetAll → `PagedResult<PermissionDto>` doğrudan `Ok(...)`. GetById → `Result<PermissionDto>` → `ToActionResult`. Create → `Result<Guid>`; hata → `ToActionResult`, başarı → `CreatedAtAction(GetById, ..., id)`. Update/Delete → `Result` → `ToActionResult`.
- **Tespit:** Result kullanımı ve 201 + location doğru. Create’teki `if (!result.IsSuccess) return result.ToActionResult(this);` açık ve okunabilir; tek satır ternary ile de yazılabilir ama zorunlu değil.
- **Risk:** Düşük.
- **Öneri:** Değişiklik yok. İsteğe bağlı: Create’te `return result.IsSuccess ? CreatedAtAction(...) : result.ToActionResult(this);` (okunabilirlik tercihi).

---

### Admin/OperationClaimPermissionsController.cs
- **Mevcut durum:** Add/Remove → `IRequest` (Unit), handler idempotent, `await _mediator.Send(...); return NoContent();`.
- **Tespit:** Result yok; hata exception ile. İdempotent tasarım nedeniyle business “conflict” dönmüyor.
- **Risk:** Düşük.
- **Öneri:** Dokunma. İleride “claim/permission bulunamadı” gibi hatalar Result ile dönmek istenirse Application + controller güncellenir.

---

### Admin/UserClaimsAdminController.cs
- **Mevcut durum:** Get → `IReadOnlyList<...>` → `Ok(...)`. Add/Remove → `IRequest` (Unit), `Send` sonrası `NoContent()`.
- **Tespit:** Result kullanılmıyor; handler’lar idempotent.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### Admin/UserOperationClaimsController.cs
- **Mevcut durum:** GetAll / GetDetails → list query → `Ok(...)`. Assign → `IRequest<Guid>`, `var id = await _mediator.Send(...); return StatusCode(201, new { id });`. Remove → Unit → `NoContent()`.
- **Tespit:** Assign handler duplicate’ta `Guid.Empty` döndürüyor; controller her durumda 201 + `{ id }` dönüyor. Diğer create’lerde (Permissions, Users) duplicate → 409; burada 201 + empty guid. Ayrıca user/claim yoksa handler’dan exception bekleniyor; Result standardı yok.
- **Risk:** Orta–yüksek (sözleşme ve hata standardı tutarsızlığı).
- **Öneri:** Application’da `AssignUserOperationClaimCommand` → `Result<Guid>`, handler: NotFound (user/claim yok), Conflict (zaten var), Value (id). Controller: `result.ToActionResult(this)` hata için; başarıda `CreatedAtAction` veya `StatusCode(201, new { id = result.Value })`. Böylece 201 sadece gerçek create’te, duplicate → 409, notfound → 404 + ProblemDetails.

---

### Admin/UserPermissionsController.cs
- **Mevcut durum:** GetEffective → query list → `Ok(list)`.
- **Tespit:** Salt okuma, Result yok; uygun.
- **Risk:** Düşük.
- **Öneri:** Değişiklik yok.

---

### Admin/UsersAdminController.cs
- **Mevcut durum:** GetAll → `PagedResult<...>` → `Ok(...)`. GetById → `AdminUserDetailDto?`; null → `NotFound()`, değilse `Ok(user)`. Create → `Result<Guid>`; hata → `ToActionResult`, başarı → `CreatedAtAction(GetById, ..., id)`.
- **Tespit:** GetById’de **manuel 404**; aynı “bulunamadı” senaryosu Permissions.GetById’de Result + ToActionResult ile 404 + ProblemDetails. Burada body/code standardı eksik.
- **Risk:** Orta.
- **Öneri:** Application’da `AdminGetUserByIdQuery` → `Result<AdminUserDetailDto>` (NotFound kodu ile); controller `return result.ToActionResult(this)`. Böylece 404 tutarlı ProblemDetails + code ile döner. API sözleşmesi (404) bozulmaz.

---

## 3. Refactor Önerileri

| Ne | Nerede | Aksiyon |
|----|--------|--------|
| Result + 201 | PermissionsController.Create, UsersAdminController.Create | Olduğu gibi kalsın; zaten doğru. |
| ToActionResult | Result dönen tüm diğer endpoint’ler | Zaten kullanılıyor; değişiklik yok. |
| Manuel 404 | UsersAdminController.GetById | Query’yi `Result<AdminUserDetailDto>` yap, controller’da `result.ToActionResult(this)`. |
| 201 + empty guid / exception | UserOperationClaimsController.Assign | Command’i `Result<Guid>` yap; controller’da hata → `ToActionResult`, başarı → 201 + `result.Value`. |
| Null body | ContactController | İsteğe bağlı: `Problem(400, title, detail)` ile standart Problem. |
| Logout / LogoutAll / Password / OperationClaimPermissions / UserClaims | İlgili controller’lar | Unit/idempotent; dokunma. |

Create endpoint’lerde özel 201 akışı:
- **Korunacak:** PermissionsController.Create, UsersAdminController.Create (zaten doğru).
- **Eklenecek/düzeltilecek:** UserOperationClaimsController.Assign → sadece başarıda 201, hata ToActionResult.

---

## 4. Nihai Aksiyon Listesi

**Önce yapılacaklar (standardı net bozan):**
1. **UsersAdminController.GetById** – `AdminGetUserByIdQuery` + handler’ı `Result<AdminUserDetailDto>` yap; controller’da `return result.ToActionResult(this)` (manuel NotFound kaldır).
2. **UserOperationClaimsController.Assign** – `AssignUserOperationClaimCommand` + handler’ı `Result<Guid>` yap; controller’da hata için `result.ToActionResult(this)`, başarı için 201 + `result.Value` (CreatedAtAction veya StatusCode(201, new { id })).

**Sonra yapılacaklar (isteğe bağlı / tutarlılık):**
3. ContactController null body yanıtını `Problem(...)` ile standart ProblemDetails’e çekmek.
4. PermissionsController.Create’te tek return ile ifade (ternary) – sadece stil.

**Hiç dokunulmaması gereken yerler:**
- AuthController (Login/Refresh zaten ToActionResult; Logout/LogoutAll Unit, boundary validation).
- MeController.
- PasswordController (Unit; ileride Result’a geçilirse o zaman controller güncellenir).
- PermissionsController (GetById, Update, Delete, Create 201).
- OperationClaimPermissionsController (idempotent Unit).
- UserClaimsAdminController.
- UserPermissionsController.
- UsersAdminController.GetAll ve Create (Create zaten Result + 201).

---

## 5. Örnek Kod (Sadece Değişecek Yerler)

### 5.1 UsersAdminController.GetById (Application Result’a geçtikten sonra)

```csharp
// Backend.Veteriner.Api - UsersAdminController.cs
[HttpGet("{id:guid}")]
[ProducesResponseType(typeof(AdminUserDetailDto), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new AdminGetUserByIdQuery(id), ct);
    return result.ToActionResult(this);
}
```

Application tarafı: `AdminGetUserByIdQuery` → `IRequest<Result<AdminUserDetailDto>>`, handler’da kullanıcı yoksa `Result.Failure(Error.NotFound(...))`, varsa `Result.Success(dto)`.

---

### 5.2 UserOperationClaimsController.Assign (Application Result<Guid>’e geçtikten sonra)

```csharp
// Backend.Veteriner.Api - UserOperationClaimsController.cs
[HttpPost("{claimId:guid}")]
[ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
public async Task<IActionResult> Assign(Guid userId, Guid claimId, CancellationToken ct)
{
    var result = await _mediator.Send(new AssignUserOperationClaimCommand(userId, claimId), ct);
    if (!result.IsSuccess)
        return result.ToActionResult(this);
    return StatusCode(StatusCodes.Status201Created, new { id = result.Value });
}
```

İsterseniz `CreatedAtAction` ile location header da eklenebilir (ör. GetDetails veya ilgili bir GetById varsa). Application: command `IRequest<Result<Guid>>`, handler NotFound/Conflict/Success ile Result döner.

---

Bu rapor, **gereksiz refactor yapmadan** sadece Result/HTTP standardını bozan iki noktayı (GetById manuel 404, Assign 201/duplicate tutarsızlığı) netleştirir; diğer controller’lar mevcut haliyle standarda uygun kabul edilir.

---

## 6. UserOperationClaimsController.Assign Refactor (Tamamlandı)

### Yapılan değişiklikler
- **Command:** `IRequest<Guid>` → `IRequest<Result<Guid>>`.
- **Handler:** `Result<Guid>` dönüyor; UserNotFound, OperationClaimNotFound, Duplicate → `Result<Guid>.Failure(code, message)`; başarı → `Result<Guid>.Success(entity.Id)`. User/claim varlık kontrolü eklendi; `Guid.Empty` sentinel kaldırıldı.
- **Controller:** Başarısızsa `result.ToActionResult(this)`; başarılıysa `StatusCode(201, new { id = result.Value })`.
- **Yeni:** `IOperationClaimReadRepository` (ExistsAsync) + `OperationClaimReadRepository` (Infrastructure).

### Beklenen davranış tablosu (Assign endpoint)

| Senaryo | HTTP status | Açıklama |
|--------|-------------|----------|
| Başarı (yeni atama) | 201 Created | Body: `{ "id": "<guid>" }` |
| Duplicate (kullanıcı zaten bu role sahip) | 409 Conflict | ProblemDetails, code: UserOperationClaims.Duplicate |
| User not found | 404 Not Found | ProblemDetails, code: UserOperationClaims.UserNotFound |
| Operation claim not found | 404 Not Found | ProblemDetails, code: UserOperationClaims.OperationClaimNotFound |
| Forbidden (policy: Roles.Write başarısız) | 403 Forbidden | Authorization middleware (Result değil) |
| Unauthorized (kimlik yok) | 401 Unauthorized | Authentication middleware |

---

## 7. UsersAdminController.GetById Refactor (Tamamlandı)

### Yapılan değişiklikler
- **Query:** `IRequest<AdminUserDetailDto?>` → `IRequest<Result<AdminUserDetailDto>>`.
- **Handler:** `Result<AdminUserDetailDto>` dönüyor; user yoksa `Result<AdminUserDetailDto>.Failure("Users.NotFound", "User not found.")`; bulunursa `Result.Success(dto)`.
- **Controller:** Manuel `if (user is null) return NotFound(); return Ok(user)` kaldırıldı; tek satır `return result.ToActionResult(this)`. 404 için `ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)`.

### Beklenen davranış tablosu (GetById endpoint)

| Senaryo | HTTP status | Açıklama |
|--------|-------------|----------|
| Başarı (kullanıcı bulundu) | 200 OK | Body: AdminUserDetailDto |
| User not found | 404 Not Found | ProblemDetails, code: Users.NotFound |
| Unauthorized (kimlik yok) | 401 Unauthorized | Authentication middleware |
| Forbidden (Users.Read policy başarısız) | 403 Forbidden | Authorization middleware |
