# Tedavi kaydı API sözleşmesi — yaşam döngüsü kuralları (ADR-043)

Backend tek doğruluk kaynağıdır. Bu belge tedavi kaydının yaşam döngüsü kurallarını tanımlar; alan listesi ve diğer uçlar Swagger'dadır. Kaynak: ürün deposu `decisions/ADR-043-birlesik-klinik-kayit-modeli.md` (AK-2, K4, K5), `decisions/ADR-013-…`. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum (2026-11-14, dal `feature/kayit-modeli-adr043`):** migration `AddTreatmentCancellationAndRowVersion` (iptal sütunları, `RowVersion`, denetim kısıtı), yalnız yerel geliştirme veritabanına. Yeni izin yok. **Sözleşme kırıcı:** `PUT` artık `rowVersion` ister; backend ve panel aynı yayında çıkmalıdır.

## 1) Eşzamanlılık `[YENİ]`

- `GET /treatments/{id}` yanıtında `rowVersion` (Base64, 8 bayt).
- `PUT /treatments/{id}` gövdesinde `rowVersion` **zorunlu**; yoksa ya da geçersizse 400 `Treatments.Validation`, eskiyse 409 `Treatments.ConcurrencyConflict`. Yanıt değişmedi (204).

## 2) Gerekçeli iptal `[YENİ]`

### `POST /treatments/{id}/cancel` (`Treatments.Update`)

```json
{ "reason": "string (5–500)", "rowVersion": "base64 (zorunlu)" }
```

- Yanıt **204**. Tedavi silinmez; iptal eden, zaman ve gerekçe saklanır (`RecordCancellationRules`). İptal son durumdur; iptal edilen tedavi değiştirilemez (`PUT` 409 `Treatments.AlreadyCancelled`).
- Kesinleşmiş muayeneye bağlı tedavide de yapılabilir (düzeltme yolu "gerekçeli iptal + yeni kayıt").
- Tedaviye bağlı **iptal edilmemiş** reçete ya da uygulama varsa 409 `Treatments.LinkedRecordsActive`: önce onlar iptal edilir (uygulama iptali stok sonucu seçimi ister).
- İptal edilmiş tedaviye yeni reçete ya da uygulama bağlanamaz: 409 `Treatments.AlreadyCancelled` (`POST /prescriptions`, `PUT /prescriptions/{id}` bağ değişirken, `POST /administrations`).
- `GET /treatments/{id}` yanıtında `[YENİ]` `cancelledAtUtc`, `cancelReason`.

| Kod | HTTP | Ne zaman |
|---|---|---|
| `Treatments.Validation` | 400 | Gerekçe 5–500 değil; `rowVersion` yok ya da geçersiz |
| `Treatments.AlreadyCancelled` | 409 | Zaten iptal; iptal edilmiş tedavide PUT; iptal edilmiş tedaviye bağ |
| `Treatments.LinkedRecordsActive` | 409 | Bağlı etkin reçete ya da uygulama var |
| `Treatments.ConcurrencyConflict` | 409 | `rowVersion` eski |
| `Treatments.NotFound` | 404 | Yok ya da klinik kapsamı dışında |

## 3) Okuma etkisi

| Yer | Davranış |
|---|---|
| `GET /treatments` | İptal edilenler varsayılan gizli; `[YENİ]` `includeCancelled=true` ile gelir, satırda `[YENİ]` `isCancelled`. `active=true` (ADR-025) iptali hiç içermez |
| Muayene özeti (`related-summary`) | Tedavi satırı kalır; `[YENİ]` `isCancelled` |
| Hasta geçmiş özeti | İptal edilen tedavi listelenmez |
| Zaman çizelgesi | Varsayılan gizli; `includeVoided=true` ile `isVoided=true`, `status="Cancelled"` |
| Muayene kayıt defteri | Tedavi şekli hücresinde başlık ` (iptal)` ekiyle kalır (reçete deseni) |
| Açık işler, otomatik kontrol hatırlatması | İptal edilen tedavi sayılmaz |
| Veri dışa aktarma `tedaviler.csv` | `[YENİ]` `Iptal`, `IptalNedeni` |

## 4) Kesinleşmiş muayeneye tedavi (ADR-043 AK-6, ADR-013 Ek 2) `[DEĞİŞTİ]`

- `POST /treatments` kesinleşmiş muayeneye artık reddedilmez (önce 409 `Examinations.Finalized`). Gövdede `[YENİ]` `lateEntryReason` (5–500) zorunludur; yoksa 400 `Treatments.LateEntryReasonRequired`. Kayıt "Kesinleştirmeden sonra eklendi" olarak yazılır (`addedAfterFinalization = true`, `lateEntryReason`).
- Muayene açıksa `lateEntryReason` yok sayılır.
- Mevcut tedavi kilidi değişmedi: kesinleşmiş muayeneye bağlı tedavi `PUT` ile değiştirilemez (409 `Examinations.Finalized`); düzeltme gerekçeli iptal + yeni kayıttır.
- `GET /treatments/{id}`, muayene özeti ve zaman çizelgesi `[YENİ]` `addedAfterFinalization` taşır (detayda `lateEntryReason` da).
- Migration `AddTreatmentAndVaccinationLateEntry`.
