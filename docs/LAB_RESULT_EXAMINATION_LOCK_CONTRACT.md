# Lab sonucu — kesinleşmiş muayene kilidi sözleşmesi (Aşama 6 Dilim 0)

Karar: ASAMA6_DISCOVERY D2 (b). Migration yok; `LabResults` şeması, uçlar ve DTO'lar değişmez.

## Kural

Tedavi/reçete/aşıdan farklı olarak lab için kesinleşmiş muayene **tam kilit değildir** (sonuç geç gelebilir).

| İşlem | Muayene kesinleşmiş ise |
|---|---|
| `POST /api/v1/lab-results` (`examinationId` kesinleşmiş muayene) | **Serbest** (istem/sonuç kaydı eklenir). |
| `PUT /api/v1/lab-results/{id}` — yalnız sonuç alanları değişiyor (`resultDateUtc`, `resultText`, `interpretation`, `notes`) | **Serbest** (geç gelen sonuç, yorum). |
| `PUT` — kimlik alanlarından biri değişiyor: `testName`, `petId`, `clinicId`, `examinationId` | **Reddedilir**: `409`-sınıfı hata kodu `Examinations.Finalized`. |

"Kesinleşmiş" = kaydın **mevcut** muayenesi veya PUT ile **hedeflenen** muayene kesinleşmiş (kayıt kesinleşmiş muayeneden ayrılamaz, kesinleşmiş muayeneye taşınamaz).
Muayeneye bağlı olmayan veya taslak muayeneye bağlı kayıtlarda kısıt yoktur.

`testName` karşılaştırması kırpılmış metin üzerinden, büyük/küçük harf duyarlı yapılır. Lab'da ayrı "tür" alanı yoktur; kimlik alanı sayılan yalnız yukarıdakilerdir.

## Uygulama

Kural tek yerde: `ExaminationLinkedRecordLock.EnsureLabIdentityEditableAsync`; muayene tarafı kuralı `Examination.EnsureAcceptsLinkedRecords` aynen kullanılır. Kimlik değişimi tespiti `LabResult.ChangesIdentity`.

## Frontend notu (bu görevde değiştirilmedi)

- `examination-detail-page.component.ts`: "Lab Sonucu Oluştur" düğmesi `Draft` koşuluna bağlı **değil** (yalnız `clientId/petId` doluluğu). Yeni kuralla bu **uyumlu**: kesinleşmiş muayenede lab eklenebilir.
- Aynı bloktaki "Tedavi/Reçete/Aşı Oluştur" düğmeleri de `Draft` koşulsuz; backend bunları kesinleşmiş muayenede reddeder → **uyumsuzluk** (ayrı iş).
- Lab düzenleme formu `testName`, hayvan ve "Bağlı muayene" alanlarını kesinleşmiş muayenede de düzenlenebilir gösterir; backend artık `Examinations.Finalized` döner. Arayüzde bu alanların kilitlenmesi veya hata mesajının gösterilmesi gerekir.

## Güncelleme — Dilim 1b (gerekçeli iptal)

Kesinleşmiş muayenedeki lab kaydı (istenmiş **veya** sonuçlanmış) gerekçeli (5–500 karakter) iptal edilebilir: `POST /api/v1/lab-results/{id}/cancel` kilit nedeniyle `Examinations.Finalized` dönmez (ADR-013: silme yok, orijinal korunur, iptal eden/zaman/gerekçe okunur). Diğer lab alanları yukarıdaki kısmi kilitle kilitli kalır. İptal için ayrı kilit denetimi yoktur; kural `ExaminationLinkedRecordLock` içinde yalnız kimlik düzenlemesi denetimiyle (`EnsureLabIdentityEditableAsync`) yaşar. Ayrıntı: `LAB_RESULT_STATUS_API_CONTRACT.md`.
