# Kritik Mutasyon Command’leri – Audit Denetim Raporu

> **Veteriner reposu:** Raporda geçen **Contact / SendContactMessageCommand** bu depoda yoktur (şablon analizinden kalma örnek). Güncel mutasyonlar için `src/` kaynağını kullanın. Ayrıca [`docs/README.md`](README.md).

**Tarih:** 2025-03-11  
**Kapsam:** Auth closure / cleanup sprint – IAuditableRequest kapsamı, metadata tutarlılığı, hassas veri riski

---

## 1. Genel değerlendirme

- **Audit kapsamı büyük ölçüde yeterli.** Listelenen kritik komutların **13/14’ü** zaten `IAuditableRequest` implement ediyor; sadece **SendContactMessageCommand** audit dışı. Yetki/güvenlik/yönetim mutasyonları (UserClaim, UserOperationClaim, Permission, OperationClaimPermission, User.Create, PasswordReset, EmailVerification) pipeline üzerinden audit bırakıyor.
- **Ana eksikler:**
  1. **SendContactMessageCommand** – IAuditableRequest yok; dış dünyaya (e-posta/ileti) etkili mutasyon audit’e alınmıyor.
  2. **Hassas veri:** `RequestPayload` tüm request’i JSON ile yazıyor; **AdminCreateUserCommand** (Password), **ConfirmPasswordResetCommand** (Token, NewPassword), **ConfirmEmailVerificationCommand** (Token) ve **AuditTarget** içinde token (ConfirmPasswordReset, ConfirmEmailVerification) doğrudan loglanıyor. Bu alanların maskelenmesi veya çıkarılması gerekir.
  3. **Business failure audit’i:** Pipeline sadece exception’da `Success: false` yazıyor; handler `Result.Failure` döndüğünde hâlâ `Success: true` yazılıyor. Yani iş kuralı başarısızlıkları audit’te başarı gibi görünüyor.
- **En kritik açıklar:** (1) Şifre ve token’ların audit log’a yazılması (uyumluluk ve güvenlik riski). (2) Contact mesajı mutasyonunun hiç audit’e alınmaması (izlenebilirlik eksikliği).

---

## 2. Dosya bazlı analiz

### RemoveUserOperationClaimCommand
- **Dosya:** `Backend.Veteriner.Application/Auth/Commands/UserOperationClaims/Remove/RemoveUserOperationClaimCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `UserOperationClaim.Remove`, AuditTarget: `UserId=..., OperationClaimId=...`
- **Eksik/sorun:** Yok. TargetType pipeline’da request adı (RemoveUserOperationClaimCommand); isteğe bağlı olarak "UserOperationClaim" yapılabilir.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### AdminAddUserClaimCommand
- **Dosya:** `Backend.Veteriner.Application/Users/Commands/Claims/Add/AdminAddUserClaimCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `UserClaim.Add`, AuditTarget: `UserId=..., OperationClaimId=...`
- **Eksik/sorun:** Yok. Naming tutarlı.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### AdminRemoveUserClaimCommand
- **Dosya:** `Backend.Veteriner.Application/Users/Commands/Claims/Remove/AdminRemoveUserClaimCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `UserClaim.Remove`, AuditTarget: `UserId=..., OperationClaimId=...`
- **Eksik/sorun:** Yok.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### CreatePermissionCommand
- **Dosya:** `Backend.Veteriner.Application/Auth/Commands/Permissions/Create/CreatePermissionCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `Permission.Create`, AuditTarget: `Code=...`
- **Eksik/sorun:** Başarılı create’te oluşan Id AuditTarget’ta yok (henüz yok çünkü command’da yok). İsteğe bağlı: handler başarılı dönünce pipeline’da response’tan id alınamaz; kalması kabul edilebilir.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### UpdatePermissionCommand
- **Dosya:** `Backend.Veteriner.Application/Auth/Commands/Permissions/Update/UpdatePermissionCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `Permission.Update`, AuditTarget: `Id=..., Code=...`
- **Eksik/sorun:** Yok.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### DeletePermissionCommand
- **Dosya:** `Backend.Veteriner.Application/Auth/Commands/Permissions/Delete/DeletePermissionCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `Permission.Delete`, AuditTarget: `Id=...`
- **Eksik/sorun:** Yok.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### AddPermissionToClaimCommand
- **Dosya:** `Backend.Veteriner.Application/Auth/Commands/OperationClaimPermissions/Add/AddPermissionToClaimCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `OperationClaimPermission.Add`, AuditTarget: `OperationClaimId=..., PermissionId=...`
- **Eksik/sorun:** Yok.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### RemovePermissionFromClaimCommand
- **Dosya:** `Backend.Veteriner.Application/Auth/Commands/OperationClaimPermissions/Remove/RemovePermissionFromClaimCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `OperationClaimPermission.Remove`, AuditTarget: `OperationClaimId=..., PermissionId=...`
- **Eksik/sorun:** Yok.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### AdminCreateUserCommand
- **Dosya:** `Backend.Veteriner.Application/Users/Commands/Create/AdminCreateUserCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `User.Create`, AuditTarget: `Email=...`
- **Eksik/sorun:** **RequestPayload** tüm request serialize edildiği için **Password** audit log’a yazılıyor. Kritik hassas veri.
- **Risk:** Yüksek.
- **Öneri:** RequestPayload’da şifre loglanmasın: AuditBehavior’da hassas alan maskesi (aşağıda) veya bu command için payload’da Password’ü çıkar/maskele.

---

### RequestPasswordResetCommand
- **Dosya:** `Backend.Veteriner.Application/Auth/PasswordReset/Commands/Request/RequestPasswordResetCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `PasswordReset.Request`, AuditTarget: `Email=...`
- **Eksik/sorun:** Sadece Email var; RequestPayload da sadece email. Kabul edilebilir (email audit’te sıklıkla tutulur).
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### ConfirmPasswordResetCommand
- **Dosya:** `Backend.Veteriner.Application/Auth/PasswordReset/Commands/Confirm/ConfirmPasswordResetCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `PasswordReset.Confirm`, **AuditTarget: `Token=...`** (ham token yazılıyor), RequestPayload’da **Token** ve **NewPassword** tam yazılıyor.
- **Eksik/sorun:** Token ve yeni şifre audit’e yazılmamalı; AuditTarget’ta token maskelenmeli (örn. `Token=***`).
- **Risk:** Yüksek.
- **Öneri:** AuditTarget’ı `Token=***` veya benzeri maskeli yap. RequestPayload için merkezi maskeleme (AuditBehavior) veya bu command’ın hassas alanlarının log’a girmemesi sağlanmalı.

---

### RequestEmailVerificationCommand
- **Dosya:** `Backend.Veteriner.Application/EmailVerification/Commands/Request/RequestEmailVerificationCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `EmailVerification.Request`, AuditTarget: `Email=...`
- **Eksik/sorun:** Yok.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

---

### ConfirmEmailVerificationCommand
- **Dosya:** `Backend.Veteriner.Application/EmailVerification/Commands/Confirm/ConfirmEmailVerificationCommand.cs`
- **Audit durumu:** IAuditableRequest var. AuditAction: `EmailVerification.Confirm`, **AuditTarget: `Token=...`** (ham token), RequestPayload’da **Token** tam yazılıyor.
- **Eksik/sorun:** Token audit’e yazılmamalı.
- **Risk:** Yüksek.
- **Öneri:** AuditTarget `Token=***`. RequestPayload’da token maskelenmeli veya çıkarılmalı.

---

### SendContactMessageCommand
- **Dosya:** `Backend.Veteriner.Application/Contact/Commands/SendContactMessage/SendContactMessageCommand.cs`
- **Audit durumu:** **IAuditableRequest implement edilmiyor.** Mutasyon (mesaj gönderimi, muhtemelen e-posta) audit’e düşmüyor.
- **Eksik/sorun:** Yetki değişikliği yok ama “dış dünyaya etkili işlem”; izlenebilirlik için audit istenir.
- **Risk:** Orta.
- **Öneri:** IAuditableRequest ekle. AuditAction: `ContactMessage.Send`, AuditTarget: anlamlı hedef (örn. `Subject=...` veya hash); RequestPayload’da Message gibi PII uzunsa kısaltma/maskeleme değerlendirilebilir.

---

## 3. Audit naming ve metadata standardı

### Önerilen Action listesi (mevcut + tutarlı)

| Command | Önerilen Action | Mevcut | Not |
|--------|------------------|--------|-----|
| UserOperationClaim.Remove | UserOperationClaim.Remove | ✓ | |
| UserClaim.Add | UserClaim.Add | ✓ | |
| UserClaim.Remove | UserClaim.Remove | ✓ | |
| Permission.Create | Permission.Create | ✓ | |
| Permission.Update | Permission.Update | ✓ | |
| Permission.Delete | Permission.Delete | ✓ | |
| OperationClaimPermission.Add | OperationClaimPermission.Add | ✓ | |
| OperationClaimPermission.Remove | OperationClaimPermission.Remove | ✓ | |
| User.Create | User.Create | ✓ | |
| PasswordReset.Request | PasswordReset.Request | ✓ | |
| PasswordReset.Confirm | PasswordReset.Confirm | ✓ | |
| EmailVerification.Request | EmailVerification.Request | ✓ | |
| EmailVerification.Confirm | EmailVerification.Confirm | ✓ | |
| ContactMessage.Send | (yok) | – | Eklenecek |
| UserOperationClaim.Assign | UserOperationClaim.Assign | ✓ | (listede yoktu, mevcut) |

Naming tutarlı: `Entity.Action` veya `Domain.Action`.

### TargetType standardı

- **Mevcut:** Pipeline `TargetType = typeof(TRequest).Name` (örn. `AdminCreateUserCommand`). Domain entity adı değil.
- **Öneri (isteğe bağlı):** IAuditableRequest’e opsiyonel `string? AuditTargetType { get; }` eklenebilir; verilirse onu kullan (örn. `User`, `Permission`, `UserOperationClaim`). Verilmezse mevcut request adı kalsın. Bu rapor için zorunlu değil.

### TargetId kuralları

- Hedef, işlemin etkilendiği entity/ kaynağı tanımlasın (Id, composite key, anlamlı tanımlayıcı).
- **Asla:** Şifre, token, refresh token, hash. Varsa `***` veya benzeri maskele.
- Composite: `UserId=..., OperationClaimId=...` gibi format uygun.

### FailureReason kuralları

- **Mevcut:** Sadece exception fırlatıldığında dolu (ex.Message). Result.Failure’da pipeline başarı yazıyor.
- **Öneri (sonra):** Result dönen request’lerde, response’un IsSuccess’ine göre Success=false ve FailureReason=result.Error.Message yazacak şekilde pipeline genişletilebilir. Bu, mevcut behavior’a ek mantık gerektirir.

---

## 4. Güvenlik / gizlilik değerlendirmesi

### Log’a yazılmaması gereken alanlar

- **Password**, **NewPassword**, **ConfirmPassword** ve türevleri.
- **Token** (email verification, password reset), **RefreshToken**, **AccessToken** ve benzeri tüm token’lar.
- **Hash** (password hash, token hash).
- **Secret**, **ApiKey**, **ClientSecret**.

### Maskelenmesi gereken alanlar (AuditTarget / RequestPayload)

- Token kullanılıyorsa: AuditTarget’ta `Token=***`.
- E-posta gövdesi veya çok uzun PII: İsteğe bağlı kısaltma veya “***” (örn. Message ilk N karakter + "...").

### Tamamen audit dışı bırakılması gereken içerikler

- Şifre ve token’ların ham değeri hiçbir zaman RequestPayload veya AuditTarget’ta saklanmamalı.
- Mevcut kodda **AdminCreateUserCommand**, **ConfirmPasswordResetCommand**, **ConfirmEmailVerificationCommand** bu kuralı ihlal ediyor (tüm request serialize + bazı AuditTarget’larda token).

---

## 5. Nihai aksiyon listesi

### Önce yapılacaklar (kritik)

1. **Hassas veri:** AuditBehavior’da RequestPayload üretirken hassas property’leri maskele veya çıkar. Öneri: `SerializeSafely` içinde JSON serialize ettikten sonra bilinen property adlarını (Password, Token, NewPassword, RefreshToken, vb.) `"***"` ile değiştiren bir maskeleme; veya request’i serialize ederken bu isimleri atlayan özel bir serializer kullan.
2. **ConfirmPasswordResetCommand:** `AuditTarget` içinde token yazmayın; `Token=***` veya sadece `Action=PasswordReset.Confirm` ile yetinin.
3. **ConfirmEmailVerificationCommand:** Aynı şekilde `AuditTarget`’ta token yerine `Token=***`.
4. **SendContactMessageCommand:** IAuditableRequest ekleyin; Action: `ContactMessage.Send`, AuditTarget: örn. `Subject=...` (Message gövdesi uzunsa payload’da kısaltılabilir veya maskelenebilir).

### Sonra yapılacaklar

5. **Business failure audit’i:** Result dönen command’larda handler başarısız dönünce (Result.Failure) audit’e Success=false ve FailureReason yazacak mekanizma (AuditBehavior’da TResponse için Result/Result<T> kontrolü ve ikinci bir WriteAsync çağrısı veya tek noktada success/failure kararı).
6. **TargetType:** İsteğe bağlı IAuditableRequest.AuditTargetType ile domain entity adı kullanımı.

### Dokunulmaması gereken yerler

- RemoveUserOperationClaim, AdminAddUserClaim, AdminRemoveUserClaim, Create/Update/Delete Permission, Add/Remove OperationClaimPermission, RequestPasswordReset, RequestEmailVerification: Mevcut audit metadata’ları yeterli; sadece hassas veri tarafı merkezi maskeden faydalanır.

---

## 6. Örnek kod

### 6.1 SendContactMessageCommand – IAuditableRequest eklenmesi

```csharp
// Backend.Veteriner.Application/Contact/Commands/SendContactMessage/SendContactMessageCommand.cs
using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Contact.Commands.SendContactMessage;

public class SendContactMessageCommand : IRequest, IAuditableRequest
{
    public string FullName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? Phone { get; init; }
    public string Subject { get; init; } = default!;
    public string Message { get; init; } = default!;

    public string AuditAction => "ContactMessage.Send";
    public string? AuditTarget => $"Subject={Subject}, Email={Email}";
}
```

(Message gövdesi RequestPayload’da kalacak; gerekirse AuditBehavior maskesi ile uzun metinler kısaltılabilir.)

### 6.2 ConfirmPasswordResetCommand – AuditTarget maskesi

```csharp
// AuditTarget: token yazmayın
public string? AuditTarget => "Token=***";
```

### 6.3 ConfirmEmailVerificationCommand – AuditTarget maskesi

```csharp
public string? AuditTarget => "Token=***";
```

### 6.4 AuditBehavior – hassas alan maskesi (özet)

RequestPayload için: JSON serialize ettikten sonra, bilinen hassas anahtar adlarını (password, token, newpassword, refreshtoken, vb.) regex veya JsonDocument ile bularak değerlerini `"***"` ile değiştirmek. Örnek (pseudocode):

- `JsonSerializer.Serialize(request)` → string.
- Hassas property adları (case-insensitive): Password, Token, NewPassword, RefreshToken, AccessToken, …
- Bu anahtarların değerleri `"***"` ile değiştirilir; böylece RequestPayload’da sadece yapı kalır, değerler maskelenir.

Bu rapor, mevcut pipeline’ı bozmadan eksik audit noktasını (Contact) ve hassas veri açıklarını kapatmayı hedefler; business failure audit’i ayrı bir adımda genişletilebilir.

---

## 7. Uygulanan düzeltmeler (2025-03-11)

- **SendContactMessageCommand:** IAuditableRequest eklendi; AuditAction: `ContactMessage.Send`, AuditTarget: `Subject=..., Email=...`.
- **ConfirmPasswordResetCommand:** AuditTarget artık `Token=***` (ham token kaldırıldı).
- **ConfirmEmailVerificationCommand:** AuditTarget artık `Token=***`.
- **AuditBehavior:** RequestPayload üretirken hassas property değerleri maskeleniyor. `SensitivePayloadKeys`: Password, NewPassword, ConfirmPassword, Token, RefreshToken, AccessToken, Secret, ApiKey, ClientSecret, Hash, PasswordHash. Bu isimlerdeki (case-insensitive) tüm property’lerin değeri JSON’da `"***"` olarak yazılıyor.
