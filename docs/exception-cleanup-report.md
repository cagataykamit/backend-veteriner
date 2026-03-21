# Eski Exception Modeli Temizlik Raporu

## 1. Genel Değerlendirme

Projede **NotFoundException**, **ForbiddenException**, **ConflictException** ve **AppException** tipleri tanımlı ancak **hiçbir yerde kullanılmıyor**. Daha önce yapılan refactor’larda tüm beklenen iş hataları zaten **Result / Result&lt;T&gt;** pattern’ine taşınmış:

- **Permissions**: Create/Update/Delete/GetById → `Result` / `Result<Guid>` / `Result<PermissionDto>`
- **Users**: AdminCreateUser → `Result<Guid>` (Users.DuplicateEmail)
- **Auth**: Login/Refresh → `Result<LoginResultDto>` (Auth.Unauthorized.*)
- **Sessions**: Revoke / RevokeAllMy / RevokeMy → `Result` (Sessions.NotFound, Sessions.Forbidden)

**Sonuç:** Aktif exception kullanımı yok; sadece kullanılmayan sınıfların kaldırılması yeterli. Yeni refactor veya kod değişikliği gerekmez.

---

## 2. Dosya Dosya Analiz (Kullanım Taraması)

### 2.1 `throw new NotFoundException` kullanımları

| Dosya yolu | Mevcut kullanım | Sınıflandırma | Önerilen yapı | Gerekçe |
|------------|-----------------|---------------|----------------|---------|
| *(yok)* | — | — | — | Projede hiç kullanılmıyor. |

### 2.2 `throw new ForbiddenException` kullanımları

| Dosya yolu | Mevcut kullanım | Sınıflandırma | Önerilen yapı | Gerekçe |
|------------|-----------------|---------------|----------------|---------|
| *(yok)* | — | — | — | Projede hiç kullanılmıyor. |

### 2.3 `throw new ConflictException` kullanımları

| Dosya yolu | Mevcut kullanım | Sınıflandırma | Önerilen yapı | Gerekçe |
|------------|-----------------|---------------|----------------|---------|
| *(yok)* | — | — | — | Projede hiç kullanılmıyor. |

### 2.4 `throw new AppException` kullanımları

| Dosya yolu | Mevcut kullanım | Sınıflandırma | Önerilen yapı | Gerekçe |
|------------|-----------------|---------------|----------------|---------|
| *(yok)* | — | — | — | Projede hiç kullanılmıyor. |

### 2.5 Exception sınıflarının referansları

- **AppException:** Sadece kendi dosyasında tanımlı; başka dosyada `using` veya `catch` yok.
- **NotFoundException / ForbiddenException / ConflictException:** Sadece kendi dosyalarında ve `AppException`’dan türetme; hiçbir handler/controller/middleware referans vermiyor.

---

## 3. Silinebilecek / Silinmemesi Gereken Exception Sınıfları

### Silinebilecek (artık kullanılmıyor)

| Sınıf | Dosya | Neden |
|-------|--------|--------|
| **AppException** | `Backend.Veteriner.Application/Common/Exceptions/AppException.cs` | Hiçbir yerde throw/catch edilmiyor; base sınıf olarak da yalnızca aşağıdaki üç türev tarafından kullanılıyor. |
| **NotFoundException** | `Backend.Veteriner.Application/Common/Exceptions/NotFoundException.cs` | Tüm “bulunamadı” senaryoları Result (örn. Permissions.NotFound, Sessions.NotFound) ile dönüyor. |
| **ForbiddenException** | `Backend.Veteriner.Application/Common/Exceptions/ForbiddenException.cs` | Yetki ihlalleri Result (Sessions.Forbidden vb.) ile dönüyor. |
| **ConflictException** | `Backend.Veteriner.Application/Common/Exceptions/ConflictException.cs` | Çakışma senaryoları Result (Permissions.DuplicateCode, Users.DuplicateEmail vb.) ile dönüyor. |

### Silinmemesi gereken (farklı amaç)

| Sınıf / davranış | Neden |
|-------------------|--------|
| **ValidationException** (FluentValidation) | Validation hataları pipeline’da ValidationBehavior tarafından fırlatılıyor; global exception handler veya MVC ile 400 ProblemDetails’e dönüşüyor. Bu akış korunmalı. |
| **Sistemsel exception’lar** (InvalidOperationException, vb.) | Beklenmeyen altyapı/uygulama hataları için exception kullanımı devam etmeli; UnhandledExceptionBehavior loglayıp rethrow ediyor. |

---

## 4. Aksiyon Listesi

1. **Aşağıdaki dört dosyayı sil:**
   - `Backend.Veteriner.Application/Common/Exceptions/AppException.cs`
   - `Backend.Veteriner.Application/Common/Exceptions/NotFoundException.cs`
   - `Backend.Veteriner.Application/Common/Exceptions/ForbiddenException.cs`
   - `Backend.Veteriner.Application/Common/Exceptions/ConflictException.cs`

2. **Kod değişikliği yapma:** Hiçbir handler veya controller bu tipleri kullanmadığı için refactor gerekmez.

3. **Test / manuel kontrol:**  
   - Solution build’in temiz olduğunu doğrula.  
   - İstersen 404/403/409 dönen mevcut endpoint’leri (ör. Permission GetById, Session revoke, User create duplicate) kısa bir smoke ile doğrula; hepsi zaten Result + ResultExtensions üzerinden çalışıyor.

4. **ResultExtensions:** Mevcut error code mapping’i (notfound → 404, forbidden → 403, duplicate → 409) zaten doğru; ek mapping veya değişiklik gerekmez.

---

## 5. Refactor Örnek Kodu

Bu projede **refactor gerekmediği** için örnek kod yok. Eğer ileride başka bir modülde hâlâ bu exception’lar kullanılsaydı, örnek dönüşüm şöyle olurdu:

```csharp
// Eski (kaldırılacak)
var entity = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("Entity", id);

// Yeni (mevcut standart)
var entity = await _repo.GetByIdAsync(id, ct);
if (entity is null)
    return Result<EntityDto>.Failure("Module.NotFound", "Entity bulunamadı.");
```

Controller tarafı değişmez: `return result.ToActionResult(this);`

---

## 6. Controller / Handler Kontrol Listesi

Silme sonrası özellikle **davranış değişikliği beklenmez**; aşağıdakiler mevcut Result akışının doğru çalıştığını teyit etmek için:

- **PermissionsController:** GetById (404), Create (409), Update (404/409), Delete (404).
- **UsersAdminController:** Create (409 duplicate email).
- **MeController:** GetSessions (401), RevokeSession (401/403/404), RevokeAllSessions (401/403).
- **AuthController:** Login / Refresh (401).

ResultExtensions ile çakışma veya eksik mapping riski **yok**; tüm bu endpoint’ler zaten Result dönüyor ve `ToActionResult` kullanıyor.
