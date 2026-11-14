# Aşı kaydı API sözleşmesi — yaşam döngüsü kuralları (ADR-043)

Backend tek doğruluk kaynağıdır. Bu belge aşı kaydının yaşam döngüsü kurallarını tanımlar; alan listesi ve diğer uçlar Swagger'dadır. Kaynak: ürün deposu `decisions/ADR-043-birlesik-klinik-kayit-modeli.md` (AK-3, K4, K5), `decisions/ADR-013-…` (uygulama iptali). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum (2026-11-14, dal `feature/kayit-modeli-adr043`):** migration `AddVaccinationCancellationAndRowVersion` (iptal sütunları, `RowVersion`, denetim kısıtı), yalnız yerel geliştirme veritabanına. Yeni izin yok. **Sözleşme kırıcı:** `PUT` artık `rowVersion` ister; backend ve panel aynı yayında çıkmalıdır.

## 1) Durum geçişleri `[DEĞİŞTİ]`

| Geçiş | Kural |
|---|---|
| Oluşturma | `Scheduled` veya `Applied`. `Cancelled` ile oluşturma 400 `Vaccinations.InvalidStatusTransition` |
| `Scheduled → Applied` (PUT) | Serbest (tarih kuralları aynı) |
| Aynı durumda kalma (PUT) | Serbest |
| `Applied → Scheduled` (PUT) | 400 `Vaccinations.InvalidStatusTransition`; yanlış kayıt gerekçeyle iptal edilir |
| `* → Cancelled` (PUT) | 400 `Vaccinations.InvalidStatusTransition`; iptal ayrı eylemdir (§3) |
| `Cancelled → *` | 409 `Vaccinations.AlreadyCancelled`; iptal son durumdur, kayıt değiştirilemez |

Kural tek yerde: `Vaccination.EnsureStatusTransition`.

## 2) Eşzamanlılık `[YENİ]`

- `GET /vaccinations/{id}` yanıtında `rowVersion` (Base64, 8 bayt).
- `PUT /vaccinations/{id}` gövdesinde `rowVersion` **zorunlu**; yoksa ya da geçersizse 400 `Vaccinations.Validation`, eskiyse 409 `Vaccinations.ConcurrencyConflict`. Yanıt değişmedi (204).
- HBS geri kaydı (`PATCH /vaccinations/{id}/hbs`) klinik içerik değildir; sürüm istemez (değişmedi).

## 3) Gerekçeli iptal `[YENİ]`

### `POST /vaccinations/{id}/cancel` (`Vaccinations.Update`)

```json
{ "reason": "string (5–500)", "rowVersion": "base64 (zorunlu)" }
```

- Yanıt **204**. Kayıt silinmez; `status = Cancelled`, `appliedAtUtc = null` (iptal kayıtta uygulama tarihi olmaz; önceki değer değişiklik geçmişinde kalır). İptal eden, zaman ve gerekçe saklanır (`RecordCancellationRules`).
- Uygulamaya (stok düşümü) bağlı aşı burada iptal edilmez: 409 `Vaccinations.LinkedToAdministration`; iptal stok sonucu seçilerek uygulama iptaliyle yapılır (ADR-013; bu yolda aşının iptal alanları boş kalır, gerekçe uygulama kaydındadır).
- Kesinleşmiş muayene kilidi iptali engellemez (düzeltme yolu "gerekçeli iptal + yeni kayıt").
- `GET /vaccinations/{id}` yanıtında `[YENİ]` `cancelledAtUtc`, `cancelReason`.

| Kod | HTTP | Ne zaman |
|---|---|---|
| `Vaccinations.Validation` | 400 | Gerekçe 5–500 değil; `rowVersion` yok ya da geçersiz |
| `Vaccinations.AlreadyCancelled` | 409 | Zaten iptal |
| `Vaccinations.LinkedToAdministration` | 409 | Uygulamaya bağlı aşı |
| `Vaccinations.ConcurrencyConflict` | 409 | `rowVersion` eski |
| `Vaccinations.NotFound` | 404 | Yok ya da klinik kapsamı dışında |

## 4) Okuma etkisi

Değişmedi: iptal edilen aşı zaman çizelgesinde varsayılan gizli (`includeVoided=true` ile gelir), listede `status` ile görünür, hatırlatma ve HBS takibine girmez. Hasta sahibi portalı iptal edilen aşıyı geçmişte `Cancelled` olarak gösterir (`PORTAL_API_CONTRACT.md`, değişmedi).
