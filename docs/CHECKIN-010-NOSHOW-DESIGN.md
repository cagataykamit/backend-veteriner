# CHECKIN-010 — "Gelmedi" (no-show): uygulama planı (seçenek B)

> **Durum:** Plan. **Kod yazılmadı; onay bekliyor.**
> Karar değişikliği: ürün canlıda değil, gerçek müşteri verisi yok; geriye uyum / veri taşıma riski yok. Bu yüzden önceki öneri (D, ayrı `AppointmentNoShow` kaydı) yerine **B: `AppointmentStatus.NoShow`** (tek doğruluk kaynağı) planlanıyor. A (Visit'te NoShow) ve C/D/E önceki gerekçelerle elendi (bkz. git geçmişi, `2ec225c`).

## 1) Enum değeri

- Eski `NoShow` **3** idi. Migration `20260321021818_AddSaaSCoreDomainTables` DB'deki 3'leri `Cancelled(2)` yapıp değeri temizledi. Kodda, testlerde, DTO ve projeksiyonlarda `3`/`NoShow` kalıntısı **yok** (aranıp doğrulandı; tek iz enum yorumu ve o migration yorumu).
- Durum `int` olarak saklanıyor (`HasConversion<int>`, kontrol kısıtı yok); Query DB read model `Status` da `int`.
- **Öneri: `NoShow = 3`** (tarihsel numara; çakışma yok, şema değişikliği gerekmez, migration gerekmez). Enum yorumu güncellenir.

## 2) Tüketici tablosu

| Tüketici | Değişiklik |
|---|---|
| **Domain (`Appointment`)** | Yeni `MarkNoShow(nowUtc)` ve `RevertNoShow()` (kural entity'de). `Cancel/Complete/RescheduleTo/UpdateDetails` yalnızca `Scheduled` kalır, NoShow terminal gibi reddeder. `EnsureCanApplyStatus` ve `ApplyWriteUpdate`: NoShow'a PUT ile geçiş **yok** (aşağıda bypass) |
| **Randevu raporu** (`GetAppointmentsReportQueryHandler`, status breakdown, CSV/XLSX, `AppointmentStatusTurkishDisplay`) | `noShow` sayısı eklenir ve toplam `scheduled+completed+cancelled+noShow` olur (bugün bilinmeyen durum toplama sessizce girmez). Etiket "Gelmedi". Durum filtresi enum üzerinden zaten çalışır |
| **Dashboard sayıları** (`DashboardTodayAppointmentStatusCounts`, `...CountsReader`, Query DB karşılığı) | Üçlü sayım dörtlüye; yeni alan eklenir. Mevcut alanlar değişmez |
| **Gecikmiş planlı uyarısı** (`DashboardOverdueScheduledAppointmentsCountSpec`) | **Kod değişmez**: yalnızca `Scheduled` sayar, NoShow otomatik düşer. Asıl kazanç bu |
| **Takvim / liste** (`AppointmentsCalendarSpec`, list specs, DTO'lar) | Sorgular durum filtrelemiyorsa değişmez; DTO enum'u zaten döner. Frontend `NoShow` değerini tanımalı (etiket/renk) |
| **Slot / çakışma** (`AppointmentOverlapping*Spec`) | Değişmez: yalnızca `Scheduled` slot tutar, NoShow slotu serbest bırakır (geçmiş saatte zaten önemsiz) |
| **Hatırlatma** (`ReminderProcessorService`) | Değişmez (`Scheduled` ve gelecek) |
| **Query DB projeksiyonu** (`AppointmentProjectionProcessor`, `RebuildService`, `AppointmentReadModelReader`, dashboard read model) | Yeni outbox olayı `appointment.noshow.v1` (cancel/complete ile aynı snapshot yolu) ve işleyici; saymada (`:640`, rebuild `:473`) `NoShow` dalı. Okuma bayrakları kapalı; açılmadan parity testi. Read model'de ayrı sayaç kolonu gerekirse ayrı Query DB migration'ı (doğrulanacak, ilk adımda bakılır) |
| **Muayene oluşturma** | `Cancelled` gibi reddedilir (`NoShow` randevuya doğrudan muayene açılmaz; hasta geldiyse önce geliş/geri alma). Visit üzerinden gelen akış etkilenmez (aşağıda) |
| **Visit / Bugün** | `CreateVisit`: NoShow randevu için geliş açılırsa randevu aynı transaction'da `Scheduled`'a döner (aşağıda). Bugün planlı satırı `!= Cancelled` olduğu için NoShow satırı kendiliğinden gelir; `appointmentStatus=NoShow` ile **son grupta ("Gelmedi")** gösterilir ki aynı gün geri alınabilsin (`GroupRank`'e bir sıra eklenir) |
| **`PUT /appointments/{id}` bypass'ı** | **Doğrulandı (kod okuyarak, HTTP denenmedi):** uç `Appointments.Reschedule` ister, ama gövdedeki `Status` ile `ApplyWriteUpdate` `Complete()`/`Cancel()` çağırıyor; `Cancel`/`Complete` izinleri dolanılıyor. `POST /appointments` de `Status` kabul ediyor (`Create` izniyle `Completed/Cancelled` açılabiliyor). **Çözüm:** PUT'ta `Status` yalnızca mevcut durumla aynı veya `Scheduled` olabilir, farklıysa `Appointments.InvalidStatusTransition`; create'te başlangıç durumu yalnızca `Scheduled`. Durum geçişleri yalnızca `/cancel`, `/complete`, `/no-show`, `/no-show/revert`. Bu bypass kapatması NoShow'dan bağımsız, aynı pakette yapılır |

## 3) Domain kuralı

- **İşaretleme:** yalnızca `Scheduled` **ve** `ScheduledAtUtc <= şimdi` (geçmiş saatli; gelecek randevu NoShow olamaz → `Appointments.NoShowNotYetDue`). Randevu için (yanlış geliş olmayan) Visit varsa işaretlenemez (hasta gelmiş; `Appointments.HasVisit`, 409). Idempotent: zaten NoShow ise 200.
- **Geç gelen hasta — iki seçenek:**
  - **(1) Geliş engellenir:** basit ama resepsiyon önce ayrı geri alma yapmak zorunda; hasta kapıda beklerken ek adım. Gerçekte gelen hastayı reddetmek akışa ters.
  - **(2) Geliş NoShow'u otomatik geri alır** (`NoShow → Scheduled`, sonra normal akış: muayene randevuyu `Completed` yapar): ek adım yok, gerçeklik (geliş) önceliklidir; tek yer `RevertNoShow()`.
  - **Öneri: (2).**
- **Geri alma (`NoShow → Scheduled`):** açık uçla da yapılır; **gerekçe zorunlu + audit** (`IAuditableRequest`, `Appointment.NoShowRevert`), çünkü rapor sayısını değiştirir. İşaretleme de audit'li (gerekçe opsiyonel). Otomatik (geliş) geri alma ayrı audit gerektirmez: Visit kaydı zaten iz bırakır.

## 4) İzin

- Randevu durum geçişleri bugün `Appointments.Cancel/Complete/Reschedule`. `Visits.Update` Visit içindir ve randevu durumunu yönetmez; kullanmak izin ayrımını yeniden bulandırır (az önce kapattığımız bypass'ın benzeri).
- **Öneri: yeni `Appointments.NoShow`**, `Appointments.Cancel` ile aynı rollere bağlanır (seed adımı; canlı veri olmadığı için maliyeti düşük). Geri alma da aynı izin.

## 5) Uçlar ve sözleşme taslağı

| Uç | İzin | Gövde | Sonuç |
|---|---|---|---|
| `POST /api/v1/appointments/{id}/no-show` | `Appointments.NoShow` | `{ "reason": "string?" }` | 204; zaten NoShow ise 204 (idempotent). Hatalar: `Appointments.NotFound` 404, `Appointments.NoShowNotYetDue` 400, `Appointments.HasVisit` 409, `Appointments.InvalidStatusTransition` 409 (Completed/Cancelled) |
| `POST /api/v1/appointments/{id}/no-show/revert` | `Appointments.NoShow` | `{ "reason": "string (5-500)" }` | 204; NoShow değilse `Appointments.InvalidStatusTransition`. Audit'li |

- Kiracı ve klinik kapsamı mevcut randevu uçlarıyla aynı. `AppointmentStatus` JSON'da mevcut biçimiyle döner, yeni değer `NoShow`.
- `VISITS_API_CONTRACT.md`: Bugün `appointmentStatus` alanına `NoShow` değeri ve "Gelmedi" grubu; `POST /visits` randevulu gelişte NoShow davranışı. Randevu sözleşmesi dokümanı (varsa) aynı pakette güncellenir.

## 6) Test planı

- **Birim:** `Appointment.MarkNoShow/RevertNoShow` (gelecek randevu, Completed/Cancelled, idempotency); `ApplyWriteUpdate` yeni bypass kuralı; komut handler'ları (404, 409, yetki bağlama, audit); Bugün sıralama (Gelmedi grubu); rapor toplam/etiket; dashboard sayıları.
- **LocalDB entegrasyon:** uç yetkisi (izinsiz 403), idempotency, `HasVisit` 409, gelecek randevu 400, geri alma gerekçesiz 400, audit kaydı, tenant/klinik izolasyonu, geç gelen hasta (Visit → randevu Scheduled), Bugün satırı, rapor `noShow` sayısı + CSV/XLSX, gecikmiş uyarıdan düşme, **PUT/POST ile Completed/Cancelled/NoShow geçişinin reddi**, eşzamanlı çift istek.
- Query DB: projeksiyon işleyici birim testi ve rebuild testi (bayraklar kapalı; parity yalnızca proje açıldığında).

## 7) Büyüklük ve commit sırası

Tahmin: **orta** (yaklaşık 1,5–2 gün). Commit sırası:
1. Sözleşme (randevu ve Visits dokümanları).
2. Bypass kapatma (`PUT`/`POST` durum kuralı) + testler (bağımsız, ilk teslim edilebilir).
3. Domain: enum + `MarkNoShow/RevertNoShow` + testler.
4. İzin (`Appointments.NoShow`) + komutlar/uçlar + audit + testler.
5. Tüketiciler: rapor + CSV/XLSX, dashboard, Bugün, muayene, Visit oluşturma + testler.
6. Query DB olayı/projeksiyon/rebuild + testler.

## Karar gereken noktalar

1. **B onayı** ve `NoShow = 3` numarası (öneri: evet).
2. **Geç gelen hasta:** geliş randevuyu otomatik `Scheduled`'a döndürsün (öneri) mü, engellensin mi?
3. **İzin:** yeni `Appointments.NoShow` (öneri) mü, mevcut bir izin mi?
4. **Bypass kapatma kapsamı:** `PUT` ve `POST /appointments` üzerinden `Completed/Cancelled` verilemesin (öneri: evet; frontend bu alanı yolluyorsa birlikte güncellenir).
