# CHECKIN-010 — "Gelmedi" (no-show) tasarım önerisi

> **Durum:** Öneri. **Kod yazılmadı; onay bekliyor.** Ürün tarafı (ADR) kararı gerektirir (aşağıda "Karar gereken noktalar").
> Kaynak: ADR-009 (Appointment plan olarak değişmez), `VISITS_API_CONTRACT.md`. Kod keşfi bu dalda yapıldı.

## Sorun

`AppointmentStatus` yalnızca `Scheduled / Completed / Cancelled`; `NoShow` bilinçli kaldırıldı. Randevusuna gelmeyen hasta bugün:

- Bugün'de (`Visits/today`) saati geçse de "Planlı" satırı olarak kalır.
- Dashboard "gecikmiş planlı randevu" uyarısında (`DashboardOverdueScheduledAppointmentsCountSpec`: `Scheduled` ve `ScheduledAtUtc < now`) süresiz sayılır.
- Raporlanabilir bir "gelmedi" bilgisi yoktur.

## Koddan çıkan bağımlılıklar (neyi bozmamalıyız)

Aşağıdakilerin hepsi randevu durumunun üç değerli olduğunu varsayar:

| Yer | Varsayım |
|---|---|
| Randevu raporu (`GetAppointmentsReportQueryHandler`, status breakdown, CSV/XLSX, `AppointmentStatusTurkishDisplay`) | `total = scheduled + completed + cancelled`; bilinmeyen durum toplama **sessizce girmez** |
| Dashboard özet/uyarı sayıları (`Dashboard*CountSpec`, `DashboardTodayAppointmentStatusCountsReader`) | `Scheduled/Completed/Cancelled` ayrı sayılar |
| Slot/çakışma (`AppointmentOverlappingAtClinicSpec`, `...ForPetSpec`) | yalnızca `Scheduled` slot tutar |
| Hatırlatma (`ReminderProcessorService`) | `Scheduled` ve gelecekteki randevu |
| Randevu yazma kuralları (`Appointment.Cancel/Complete/RescheduleTo/UpdateDetails`, `EnsureCanApplyStatus`) | yalnızca `Scheduled` değişebilir; diğerleri terminal |
| Query DB projeksiyonları (`AppointmentProjectionProcessor`, rebuild, read model okuyucuları, dashboard read model) | üç durum sayılır |
| Muayene oluşturma (`CreateExaminationCommandHandler`) | `Cancelled` reddeder, `Scheduled` ise `Completed` yapar |
| Visit (`CreateVisitCommandHandler`, Bugün okuyucusu) | `Scheduled` randevuya geliş; Bugün planlı satırlar `!= Cancelled` ve Visit'siz |

## Seçenekler

| # | Seçenek | Artı | Eksi |
|---|---|---|---|
| A | **Visit'te `NoShow` bakım durumu** | Yeni tablo yok | Visit "gerçek geliş"tir; gelmeyen hasta için sahte `arrivedAtUtc` gerekir. Hayvan başına tek aktif Visit indeksi (`CareStatus <> Completed`) gelmeyeni "aktif" sayar ve hastanın gerçek gelişini engeller; randevu başına tek Visit indeksi geç gelişi engeller. Filtreli indeksler, Bugün, geçişler yeniden yazılır. ADR-009 modelini bozar. **Önerilmez.** |
| B | **`AppointmentStatus.NoShow` geri getirmek** | En doğal raporlama | Yukarıdaki tablodaki tüm yerler (rapor toplamı, dashboard, projeksiyon, Query DB, CSV/XLSX, DTO enum'ları, yazma kuralları, `PUT /appointments` durum bypass'ı) değişir; ADR-009 K1-B'nin reddettiği yol. Eski `NoShow=3` kalıntısı riski geri döner. **Önerilmez.** |
| C | **Appointment'a nullable işaret** (`NoShowMarkedAtUtc`, işaretleyen, gerekçe) | Tek tablo, basit sorgu | Appointment (plan) şeması ve mutasyon/projeksiyon hattı değişir; durum `Scheduled` kalır, yani her `Scheduled` tüketicisi (dashboard, Bugün, rapor) işareti ayrıca dışlamalıdır. Plan modelini kirletir |
| D | **Ayrı kayıt `AppointmentNoShow`** (AppointmentId benzersiz; işaretlenme zamanı, işaretleyen, gerekçe) — **ÖNERİLEN** | Appointment ve Visit **hiç değişmez** (ADR-009 ilkesi: plan ≠ gerçeklik; Visit'in "gerçek geliş" örüntüsünün karşılığı olarak "gerçekleşmeyen geliş"). Varsayılan davranış korunur; işareti isteyen yerler kademeli eklenir. Geri alınabilir (kayıt silinir, audit'li) | Yeni tablo + migration. İşareti dışlaması istenen her yer (Bugün planlı satır, dashboard gecikmiş uyarısı, takvim) `NOT EXISTS` ile ayrıca bağlanmalı; unutulursa tutarsızlık |
| E | Randevu notuna metin | Sıfır şema | Raporlanamaz, filtrelenemez, Bugün'e yansımaz. **Reddedildi** |

## Öneri: D

Kurallar (öneri):

1. Yalnızca **`Scheduled`** randevu için işaretlenir; `ScheduledAtUtc` geçmiş olmalı (gelecekteki randevu "gelmedi" olamaz). `Completed/Cancelled` randevuda `Appointments.InvalidStatusTransition` benzeri hata.
2. Randevu için (yanlış geliş olmayan) **Visit varsa işaretlenemez** (hasta zaten gelmiş) — `Visits` tarafı tek doğruluk.
3. İşaretleme ve geri alma gerekçeli ve audit'li (`IAuditableRequest`); izin: mevcut `Visits.Update` (resepsiyon zaten kullanıyor) veya ayrı `Visits.NoShow` — onayınıza bırakıyorum, öneri `Visits.Update`.
4. **Geç gelen hasta:** işaretli randevu için Visit açılırsa işaret aynı işlemde kaldırılır (geliş gerçeği önceliklidir). Alternatif: geliş reddedilir; hasta fiilen geldiği için önerilmez.
5. Etkiler (kademeli, hepsi opsiyonel çıktı):
   - **Bugün:** `TodayItemDto`'ya `isNoShow` (planlı satır gelmedi işaretliyse); sıralamada "Planlı"dan sonra veya ayrı grup (frontend karar).
   - **Dashboard gecikmiş planlı uyarısı:** işaretli randevular dışlanır (aksi halde "gelmedi" süresiz uyarı olarak kalır).
   - **Randevu raporu:** ayrı `noShowCount` alanı; mevcut `scheduled/completed/cancelled` ve toplam kuralı **değişmez** (gelmedi, `scheduled` içinde kalır ya da ayrılır — aşağıdaki karar 2).
   - **Slot, hatırlatma, Appointment yazma kuralları, Query DB projeksiyonları:** değişmez.
6. Randevu durumu `Scheduled` kalır; takvim geçmiş gün için `isNoShow` ile etiketleyebilir (isteğe bağlı, ayrı iş).

## Karar gereken noktalar (ADR gerektirir)

Bu iş raporlama anlamını (dashboard/randevu raporu) ve klinik iş akışını etkilediği için kısa bir **ADR (öneri: ADR-010)** önerilir; WORKFLOW'a göre "birden fazla modülü etkileyen" karar.

1. **Mimari:** D (öneri) mi, C mi? (A ve B önerilmez.)
2. **Rapor semantiği:** "Gelmedi" `scheduled` sayısından ayrı mı sayılsın, yoksa `scheduled` içinde bir alt sayı mı? (Önerilen: ayrı `noShowCount`, mevcut üç sayı ve toplam kuralı aynı kalır, yani gelmedi `scheduled` içinde sayılmaya devam eder; böylece geçmiş raporlar bozulmaz.)
3. **Slot:** Gelmedi işaretlenen randevu slotu serbest bırakmaz (geçmiş saatte anlamsız); onay?
4. **Geç gelen hasta:** işaret otomatik kalkar (öneri) mı, geliş engellenir mi?
5. **İzin:** `Visits.Update` mi, ayrı bir izin mi?
6. **Otomasyon:** Otomatik "gün sonu gelmedi" **yapılmaz** (yalnızca açık işaretleme) — onay?
7. **Müşteri düzeyi takip** (tekrar gelmeyen müşteri) bu kapsamda değil; ayrı backlog.

## Uygulama kapsamı (onaydan sonra, sırayla)

1. Sözleşme: `VISITS_API_CONTRACT.md` ek bölümü (`POST /appointments/{id}/no-show`, `DELETE`/geri alma, Bugün `isNoShow`).
2. Domain: `AppointmentNoShow` (kural entity içinde), konfigürasyon, benzersiz indeks, migration (dosya üretimi; uygulama onayla).
3. Komutlar + validator + audit + izin; Bugün okuyucusu `isNoShow`; dashboard gecikmiş uyarısı dışlama; rapor `noShowCount`.
4. Birim + LocalDB entegrasyon testleri (idempotency, 409, yetki, tenant/klinik izolasyonu).
