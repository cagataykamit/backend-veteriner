# CQRS-11B — Appointment CQRS Operations Runbook

Production-ready operator guide for deployment, mode cutover, incident response, rollback, rebuild, and data validation.

**Audience:** operators who must act without reading application code.

**Related:** [`cqrs-11a-staging-rollout.md`](cqrs-11a-staging-rollout.md), [`appointment-projection-operations.md`](appointment-projection-operations.md), [`appointment-cqrs-load-test.md`](appointment-cqrs-load-test.md)

---

## 1. Amaç ve kapsam

Bu runbook şu durumlarda güvenli, sıralı müdahale sağlar:

- Normal deployment ve migration
- Mode A / B / C geçişi ve rollback
- Query DB arızası
- Projection backlog (`pendingCount > 0`)
- Retry-waiting kuyruğu
- Dead-letter kayıtları
- Command/Query parity mismatch
- Query DB rebuild
- Failed deployment
- Recovery sonrası staged rollout

**Kapsam dışı:** multi-instance claim/lease (CQRS-11D), production-specific host adları, gerçek RTO/RTO sayıları, SQL backup otomasyonu (harici ops sorumluluğu).

---

## 2. Sistem özeti

```text
Write path:  Client → API → Command DB → OutboxMessages
Read path:   Mode A → Command DB (appointments + dashboard)
             Mode B → Query DB (appointments), Command DB (dashboard)
             Mode C → Query DB (appointments + dashboard)
Projection:  OutboxMessages → AppointmentProjectionProcessor → Query DB
Health:      GET /health/ready (appointment-projection, query-sql, sql, outbox)
```

**Authoritative source:** Command DB. Query DB yeniden üretilebilir (rebuild ile).

**Kritik tasarım kararı:** Query read path'te **otomatik Command DB fallback yoktur**. Query DB veya projection arızasında Mode B/C read endpoint'leri başarısız olabilir; operasyonel rollback Mode A/B'ye dönmeyi gerektirir.

---

## 3. Roller ve sorumluluklar

| Rol | Sorumluluk |
|-----|------------|
| **Application operator** | API deploy/restart, feature flag, health/smoke, rollout script çalıştırma |
| **Database operator** | Backup doğrulama, `migrate` / `migrate-query`, SQL erişim, restore |
| **Release owner** | Deploy penceresi, go/no-go, rollback kararı, change kaydı |
| **Incident commander** | Severity, iletişim, escalation, recovery sırası onayı |
| **Developer escalation** | Dead-letter root cause, schema/contract bug, kod düzeltmesi |

Organizasyon yapısı repoda tanımlı değildir; yukarıdaki roller incident sırasında atanmalıdır.

---

## 4. Ortam ön koşulları

- [ ] Command ve Query DB **farklı catalog** adlarında
- [ ] Connection string'ler secret store / env var üzerinden (repo'da değil)
- [ ] `ASPNETCORE_ENVIRONMENT` hedef ortama uygun (`Staging` / `Production`)
- [ ] **Tek aktif API instance** projector ile (`AppointmentProjection__Enabled=true`)
- [ ] DbMigrator erişimi (Command + Query connection)
- [ ] `/health/ready` operatör erişimi
- [ ] Rollback owner ve escalation kanalı tanımlı

**Startup doğrulama (secret yok):** API loglarında `CQRS startup configuration` satırı — environment, flag'ler, `CommandDbCatalog`, `QueryDbCatalog`.

---

## 5. Mode A / B / C tanımları

### Mode A — Command read (güvenli varsayılan)

```text
QueryReadModels__AppointmentsEnabled=false
QueryReadModels__DashboardAppointmentsEnabled=false
AppointmentProjection__Enabled=true
```

| Read surface | Source |
|--------------|--------|
| Appointment list/calendar | Command DB |
| Dashboard appointment metrics | Command DB |

### Mode B — Appointment query read

```text
QueryReadModels__AppointmentsEnabled=true
QueryReadModels__DashboardAppointmentsEnabled=false
AppointmentProjection__Enabled=true
```

| Read surface | Source |
|--------------|--------|
| Appointment list/calendar | Query DB |
| Dashboard appointment metrics | Command DB |

### Mode C — Full query read

```text
QueryReadModels__AppointmentsEnabled=true
QueryReadModels__DashboardAppointmentsEnabled=true
AppointmentProjection__Enabled=true
```

| Read surface | Source |
|--------------|--------|
| Appointment list/calendar | Query DB |
| Dashboard appointment metrics | Query DB |

**Flag değişikliği restart gerektirir** (`IOptions<>` startup binding).

**Rollback kuralı (zorunlu):**

```text
Rollback sırasında AppointmentProjection__Enabled=true kalmalı.
```

Projection kapatılırsa Query DB geride kalır; sonraki Mode B/C açılışında rebuild gerekebilir.

---

## 6. Normal deploy prosedürü

### 6.1 Doğrulanmış migration sırası

API startup **otomatik migration çalıştırmaz**. Şema değişikliği içeren deploy'larda:

```text
1. Deploy kararı ve bakım penceresi (Release owner)
2. Command DB backup doğrulandı (Database operator)
3. Query DB backup doğrulandı (Database operator)
4. Mevcut /health/ready snapshot kaydedildi (Application operator)
5. Mevcut Mode (A/B/C) kaydedildi
6. pending/retry/dead-letter = 0 doğrulandı
7. Command/Query appointment parity kontrol edildi
8. API instance sayısı = 1 doğrulandı
9. Query read flag'leri Mode A'ya alındı (zaten A ise atla)
10. API durduruldu veya bakım modunda tutuldu
11. Command DB migration
12. Query DB migration
13. API deploy + restart (Mode A, projector enabled)
14. Mode A preflight + smoke
15. Gerekliyse projection rebuild (bkz. Bölüm 13)
16. Mode B staged rollout + smoke
17. Mode C staged rollout + smoke
18. Post-deploy health + parity + queue kontrolü
```

**Neden migration deploy öncesi?** Yeni API kodu yeni şemaya bağımlı olabilir. Migration sonrası binary deploy güvenli sıradır. Geriye dönük uyumlu additive migration'larda eski API kısa süre çalışabilir; yine de Mode A + kontrollü pencere tercih edilir.

**Rebuild normal deploy'da zorunlu değildir.** Yalnızca Query DB boş/bozuk veya parity mismatch varsa.

### 6.2 Komutlar (placeholder)

```powershell
# Pre-deploy health (kayıt için)
curl -k -s https://<staging-url>/health/ready | jq .

# Mode A flag'leri (deploy platform env)
# QueryReadModels__AppointmentsEnabled=false
# QueryReadModels__DashboardAppointmentsEnabled=false
# AppointmentProjection__Enabled=true

# Migration (API durdurulmuşken)
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query

# Mode A smoke
.\tests\load\tools\Test-CqrsLoadPreflight.ps1 -BaseUrl https://<staging-url> -Mode command
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 `
  -BaseUrl https://<staging-url> `
  -Mode command-read `
  -CommandDatabase <command-db> `
  -QueryDatabase <query-db> `
  -Apply

# Mode B
# Set AppointmentsEnabled=true, DashboardAppointmentsEnabled=false, restart API
.\tests\load\tools\Test-CqrsLoadPreflight.ps1 -BaseUrl https://<staging-url> -Mode appointment-query
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl https://<staging-url> -Mode appointment-query -Apply ...

# Mode C
# Set DashboardAppointmentsEnabled=true, restart API
.\tests\load\tools\Test-CqrsLoadPreflight.ps1 -BaseUrl https://<staging-url> -Mode full-query
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl https://<staging-url> -Mode full-query -Apply ...
```

---

## 7. Normal rollback prosedürü

### 7.1 Feature flag rollback (en hızlı)

| From | To | Flag değişikliği |
|------|-----|------------------|
| Mode C | Mode B | `DashboardAppointmentsEnabled=false` |
| Mode B | Mode A | `AppointmentsEnabled=false` (+ dashboard zaten false) |
| Mode C | Mode A | Her iki flag `false` |

```text
1. Rollback kararı (Release owner / Incident commander)
2. Hedef flag'leri ayarla
3. AppointmentProjection__Enabled=true KORU
4. Tek instance API restart
5. Test-CqrsLoadPreflight (hedef mode)
6. Invoke-CqrsStagedRollout -Apply smoke
7. Health + queue kontrolü
```

**Veri kaybı:** Command write path etkilenmez. Query projection arka planda güncel kalmaya devam eder.

### 7.2 Uygulama binary rollback

- Önceki API sürümüne dönüş **DB migration rollback değildir**
- Migration geri alma otomatik önerilmez; backward compatibility değerlendirmesi gerekir
- Binary rollback sonrası Mode A'da kalın; health + smoke

### 7.3 Rollback planı (script)

```powershell
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl https://<staging-url> -Mode command-read -ShowRollbackFrom full-query
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl https://<staging-url> -Mode command-read -ShowRollbackFrom appointment-query
```

---

## 8. Query DB outage müdahalesi

### Belirtiler

- `/health/ready` → `Unhealthy`
- `query-sql` entry failure veya "bekleyen migration"
- Mode B/C appointment list/calendar → 5xx
- Query connection timeout logları
- `appointment-projection` Unhealthy (Query unreachable)

### Beklenen davranış

- **Otomatik Command fallback yok**
- Write (create/reschedule/cancel) Command DB'de devam eder
- Outbox birikir; projector retry yapar
- Mode A appointment read'leri Command DB'den çalışmaya devam eder

### Müdahale sırası

| Adım | Sorumlu | Aksiyon |
|------|---------|---------|
| 1 | Incident commander | Incident aç, severity belirle (genelde SEV-2) |
| 2 | Application operator | Aktif mode doğrula (`/health/ready` flags) |
| 3 | Release owner | Mode C → B veya A kararı |
| 4 | Application operator | Query read flag'lerini kapat, **projector açık** |
| 5 | Application operator | API restart |
| 6 | Application operator | Command DB endpoint smoke (list, create) |
| 7 | Application operator | Health kontrolü (Command path Healthy olmalı) |
| 8 | Database operator | Query DB onarım / restore |
| 9 | Database operator | `migrate-query` |
| 10 | Application operator | Queue drain veya rebuild kararı |
| 11 | Database operator + App op | Rebuild + parity (gerekirse) |
| 12 | Application operator | Mode B staged rollout |
| 13 | Application operator | Mode C staged rollout (önceki hedefse) |

---

## 9. Projection backlog müdahalesi

### Belirtiler

- `pendingCount > 0`
- `oldestPendingAgeSeconds` yükseliyor
- Health `Degraded` veya `Unhealthy`
- Log: `Appointment projection batch completed` throughput düşük

### Kontrol sırası

```text
[ ] AppointmentProjection__Enabled = true ?
[ ] API instance sayısı = 1 ?
[ ] Query DB erişilebilir mi (query-sql Healthy) ?
[ ] retryWaitingCount > 0 ?
[ ] deadLetterCount > 0 ?
[ ] Batch loglarında ProcessedCount > 0 ?
[ ] Aynı OutboxMessage Id sürekli hata veriyor mu (LastError) ?
```

### Yanlış müdahaleler (YAPMAYIN)

- Outbox event'lerini doğrudan silmek
- `ProcessedAtUtc` manuel set etmek
- Query read açıkken kontrolsüz rebuild
- İkinci projector worker başlatmak (multi-instance)
- Dead-letter'ı görmeden "queue temiz" saymak

### Doğru müdahale

1. Geçici Query/network ise: root cause düzelene kadar bekle; retry-waiting otomatik devreye girer
2. Sürekli hata: dead-letter incele (Bölüm 11)
3. Query DB down: Bölüm 8
4. Backlog yaşı `UnhealthyAfterSeconds` (varsayılan 60s) üzerinde: escalation + Mode A rollback değerlendir

---

## 10. Retry-waiting müdahalesi

### Mekanizma

| Alan | Anlam |
|------|--------|
| `RetryCount` | Başarısız deneme sayısı |
| `NextRetryAtUtc` | Sonraki otomatik deneme zamanı (UTC) |
| Backoff | `BaseDelaySeconds * 2^(retry-1)`, max 600s, jitter |
| `MaxRetryCount` | Varsayılan 10 (appsettings); aşılınca dead-letter |

**Otomatik işleme:** Evet. Projector `NextAttemptAtUtc <= now` olduğunda head event'i tekrar dener.

**Ordering:** Strict head-of-line. Head event retry beklerken veya dead-letter ise sonraki aynı aggregate event'leri işlenmez.

### Adımlar

```text
1. /health/ready → retryWaitingCount, nextRetryAtUtc kaydet
2. OutboxMessages head satırının LastError / Error incele (PII-safe)
3. Geçici DB/network mü? Query-sql ve connectivity kontrol
4. NextRetryAtUtc gelene kadar bekle (bounded, örn. 15 dk)
5. RetryCount artışını izle
6. MaxRetryCount - 1 yaklaşıyorsa developer escalation
7. Root cause düzelmeden manuel replay yapma
```

Health: `retryWaitingCount > 0` → **Degraded** (Healthy değil).

---

## 11. Dead-letter müdahalesi

### Mekanizma

- `RetryCount >= MaxRetryCount` → `DeadLetterAtUtc` set
- **Otomatik replay yok**
- `DeadLetterIsUnhealthy=true` (varsayılan) → health **Unhealthy**
- Head dead-letter strict ordering'i bloklar

### Desteklenmiş appointment replay aracı

```text
Appointment projection için desteklenmiş manuel replay CLI aracı mevcut değil.
```

Genel outbox admin API (`POST /api/v1/admin/outbox/retry/{id}`) tüm outbox tipleri için dead-letter reset yapabilir; appointment projection ordering riski nedeniyle **normal prosedür değildir**. Developer escalation ile değerlendirilir.

**Normal prosedür olarak önerilmez:** dead-letter SQL DELETE, `ProcessedAtUtc` manuel set, `retry-dead-all` kör kullanım.

### Adımlar

```text
1. Gerekirse Mode A'ya dön (read flag kapat, projector açık)
2. Dead-letter mesajı tanımla: Id, Type, CreatedAtUtc, LastError (PII-safe log)
3. Event type doğrula (appointment.created.v1, rescheduled.v1, ...)
4. Root cause: schema, Query state, payload, connectivity
5. Production bug ise düzeltme deploy (Developer)
6. Karar: fix + retry vs rebuild
7. Query DB parity doğrula
8. Mode B → Mode C yeniden aç
```

### Dead-letter varken rebuild

`rebuild-appointment-projections` **pending veya dead-letter varken çalışmaz** (exception). Önce dead-letter operasyonel olarak çözülmeli veya developer guidance alınmalı.

---

## 12. Parity mismatch müdahalesi

Count parity tek başına yeterli değildir. Kontrol seti:

| Kontrol | Command DB | Query DB |
|---------|------------|----------|
| Appointment row count | `COUNT(*)` Appointments | `COUNT(*)` AppointmentReadModels |
| Duplicate AppointmentId | — | `GROUP BY AppointmentId HAVING COUNT(*) > 1` |
| Invalid Guid | — | `TRY_CONVERT(uniqueidentifier, AppointmentId) IS NULL` |
| Schedule invariant | — | `ScheduledEndUtc < ScheduledAtUtc` |
| Daily stats invariant | — | Operasyonel SQL / dashboard parity testleri |
| Queue | pending/retry/dead-letter = 0 | — |

### Karar ağacı

```text
Queue aktif (pending > 0 veya retry > 0)?
  └─ Evet → Önce bounded drain bekle (PollTimeout). Dead-letter varsa Bölüm 11.

Queue temiz ama parity bozuk?
  └─ Mode A'ya dön
  └─ Projector/API rebuild çakışmasını engelle (rebuild sırasında projector durdur veya tek instance garanti)
  └─ rebuild-appointment-projections
  └─ Parity + integrity kontrolleri
  └─ Health Healthy
  └─ Mode B rollout
  └─ Mode C rollout
```

**Script:**

```powershell
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 `
  -BaseUrl https://<staging-url> `
  -Mode command-read `
  -CommandDatabase <command-db> `
  -QueryDatabase <query-db> `
  -Apply
```

---

## 13. Query DB rebuild prosedürü

### Zorunlu koşullar

- Query DB boş, corrupt veya parity mismatch
- **pending appointment outbox = 0**
- **dead-letter appointment outbox = 0**
- Command ve Query catalog farklı

### Güvenli sıra

```text
1. Mode A'ya dön (read flag'ler kapalı)
2. API/projector rebuild ile çakışmayacak şekilde hazırla (tercihen tek instance, rebuild öncesi projector durdurulabilir)
3. pending/retry/dead-letter = 0
4. dead-letter varsa Bölüm 11 önce
5. Query DB backup (Database operator)
6. migrate-query
7. rebuild-appointment-projections
8. Count parity
9. Duplicate AppointmentId = 0
10. Invalid Guid = 0
11. ScheduledEndUtc >= ScheduledAtUtc
12. Daily stats invariant (ops SQL veya dashboard parity)
13. /health/ready Healthy
14. API/projector start (projector enabled)
15. Mode B rollout + smoke
16. Mode C rollout + smoke
```

### Komutlar

```powershell
# Önkoşul: appointment outbox temiz
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
dotnet run --project src/Backend.Veteriner.DbMigrator -- rebuild-appointment-projections --batch-size 1000
```

Rebuild `ProcessedProjectionEvents` temizler; sonrası live outbox normal işlenir.

**YAPMAYIN:** Query read flag açıkken rebuild; pending/dead-letter varken rebuild zorlamak.

---

## 14. Backup / restore prosedürü

### Repo durumu

SQL Server backup/restore **repoda otomatik değildir**. Deployment altyapısı / DBA sorumluluğundadır.

### Öncelikler

| DB | RPO önceliği | Not |
|----|--------------|-----|
| **Command DB** | Veri kaybı kabul edilemez | Authoritative source; write path |
| **Query DB** | Yeniden üretilebilir | Rebuild ile Command'dan türetilir; restore operasyon süresini kısaltır |

**Kesin RTO/RPO sayıları organizasyon SLA'sında tanımlanmalıdır** — bu dokümanda sayı verilmez.

### Kurallar

- Backup doğrulanmadan destructive işlem yapılmaz (rebuild, restore)
- Query DB kaybında: Command DB yeterli → rebuild ile recovery mümkün
- Query DB restore sonrası: `migrate-query` + parity + queue drain

---

## 15. Failed deployment prosedürü

| Senaryo | Rollback noktası | Not |
|---------|------------------|-----|
| API başlamıyor | Binary rollback + Mode A flags | Migration durumunu kontrol et |
| Command migration başarısız | Deploy durdur; migration fix | DB rollback otomatik değil |
| Query migration başarısız | Deploy durdur; Mode A'da kal | Command path çalışabilir |
| Mode B smoke başarısız | Flag → Mode A, restart | Projection açık kalsın |
| Mode C smoke başarısız | Flag → Mode B veya A | Dashboard-only sorun ise B yeterli olabilir |
| Health Degraded | Backlog/retry incele | Mode rollback değerlendir |
| Health Unhealthy | Bölüm 8/9/11 | Query down veya dead-letter |
| Parity bozuk | Mode A + rebuild (Bölüm 13) | |

**Üç ayrı rollback ekseni:**

1. **Feature flag rollback** — dakikalar; en hızlı kullanıcı etkisi azaltma
2. **Application binary rollback** — önceki sürüm; migration uyumluluğu gerekir
3. **DB migration rollback** — otomatik önerilmez; forward-fix veya restore değerlendirilir

---

## 16. Recovery sonrası staged rollout

Query DB onarımı veya failed deploy recovery sonrası:

```text
1. Mode A doğrula (health, queue, Command smoke)
2. Parity / rebuild tamamlandı mı?
3. Invoke-CqrsStagedRollout -ShowSequence ile plan
4. Mode B: preflight + Apply
5. Kısa gözlem (queue, error rate)
6. Mode C: preflight + Apply
7. Post-deploy checklist
8. Incident kapanış notları
```

---

## 17. Incident severity tablosu

Organizasyon SLA'sı tanımlanmamıştır. Aşağıdaki sınıflandırma escalation içindir; sayısal SLA uydurulmamıştır.

### SEV-1

- Command DB unavailable
- Appointment write kaybı riski
- Yaygın API outage
- Data corruption şüphesi

### SEV-2

- Query DB unavailable (Mode B/C aktifken)
- `deadLetterCount > 0`
- Parity mismatch (kullanıcı etkili)
- Projection backlog kritik (`Unhealthy` eşiği aşıldı)

### SEV-3

- Retry-waiting (geçici)
- Kısa süreli pending artışı
- Mode C gerektirmeyen dashboard-only sapma
- Performans degradation (p95 yükselmesi, HTTP error artışı)

---

## 18. RTO / RPO varsayımları

| Metrik | Durum |
|--------|--------|
| Command DB RPO | Organizasyon tarafından tanımlanmalı — **veri kaybı kabul edilemez** hedefi |
| Query DB RPO | Yeniden üretilebilir; rebuild süresi operasyonel RTO'ya bağlı |
| Mode A rollback RTO | Flag + restart: dakika düzeyi (ortam bağımlı) |
| Full rebuild RTO | Appointment count ve batch size'a bağlı — ölçüm gerekir |
| Kesin sayılar | **Bu dokümanda tanımlı değil** |

---

## 19. Güvenlik ve secret kuralları

**Dokümana veya ticket'lara yazmayın:**

- JWT key, SQL password, access token, tam connection string
- Production host/IP (placeholder kullanın)
- Tenant / clinic / user ID (incident log'da PII-safe özet)

**Placeholder:**

```text
<staging-url>
<command-db>
<query-db>
```

**Token dosyası:** `tests/load/.tokens/` — `.gitignore`'da; commit edilmez.

---

## 20. Tek instance kısıtı

```text
Appointment projector claim/lease kullanmıyor.
Mode B/C rollout sırasında tek aktif projector instance zorunludur.
```

Multi-instance tespit edilirse:

```text
Rollout durdurulur.
CQRS-11D (claim/lease) tamamlanmadan Mode B/C devam edilmez.
```

Doğrulama: localhost'ta `Invoke-CqrsStagedRollout.ps1 -Apply` single-instance probe; production'da deployment platform instance count.

---

## 21. Operasyon checklist'leri

### Pre-deploy

```text
[ ] Command DB backup doğrulandı
[ ] Query DB backup doğrulandı
[ ] Mevcut Mode kaydedildi
[ ] /health/ready snapshot alındı
[ ] pendingCount = 0
[ ] retryWaitingCount = 0
[ ] deadLetterCount = 0
[ ] Command/Query appointment parity OK
[ ] Tek instance doğrulandı
[ ] Migration planı onaylandı
[ ] Rollback owner belirlendi
[ ] Query read flag'leri Mode A
```

### Post-deploy

```text
[ ] Mode A smoke geçti
[ ] Mode B smoke geçti
[ ] Mode C smoke geçti
[ ] Queue temiz
[ ] Parity sağlandı
[ ] HTTP error rate normal
[ ] CQRS startup log flag'leri beklenen değerde
[ ] Incident/notlar güncellendi
```

### Incident recovery

```text
[ ] Command read çalışıyor
[ ] Query DB onarıldı
[ ] migrate-query tamam
[ ] Rebuild tamamlandı (gerekliyse)
[ ] Parity + integrity OK
[ ] /health/ready Healthy
[ ] Mode B geçti
[ ] Mode C geçti (hedefse)
[ ] Incident notları kaydedildi
```

---

## 22. Komut referansı

### Güvenli (read-only / doğrulama)

| Komut | Açıklama |
|-------|----------|
| `GET /health/ready` | Health + projection flags + queue |
| `Test-CqrsLoadPreflight.ps1` | Mode + queue preflight |
| `Invoke-CqrsStagedRollout.ps1` (dry-run) | Plan, değişiklik yapmaz |
| `Invoke-CqrsStagedRollout.ps1 -Apply` | Smoke + parity (hedef API yapılandırılmış olmalı) |
| `Test-CqrsStagedRollout.ps1` | Script/readiness otomasyon |
| `DbMigrator migrate` | Command şema (planlı pencere) |
| `DbMigrator migrate-query` | Query şema |

### Dikkatli (durumsal)

| Komut | Risk |
|-------|------|
| `rebuild-appointment-projections` | Query projection tablolarını yeniden yazar |
| `POST /api/v1/admin/outbox/retry/{id}` | Ordering riski; escalation gerekir |
| `Invoke-CqrsLoadDataReset.ps1` | **Yalnızca LoadTest DB** |

### Destructive / önerilmez

| Aksiyon | Risk |
|---------|------|
| `ProcessedAtUtc` manuel UPDATE | Permanent drift |
| Outbox DELETE | Event kaybı |
| Query DB DROP | Operasyonel kesinti |
| `retry-dead-all` kör kullanım | Ordering bozulması |
| Projection rollback sırasında kapatma | Query DB geride kalır |
| İkinci projector instance | Duplicate work, retry noise |

### API / projector durdurma

| Durum | API | Projector |
|-------|-----|-----------|
| Normal flag rollback | Restart | **Açık kalır** |
| Full rebuild | Durdur önerilir | Durdur önerilir |
| Failed binary rollback | Önceki sürüm | Açık (Mode A) |
| Query outage | Restart (Mode A) | **Açık kalır** |

---

## 23. Kalan bilinmeyenler

| Konu | Durum |
|------|--------|
| Gerçek staging/production host | Repo dışı |
| Backup/restore otomasyonu | Harici ops |
| Kesin RTO/RPO sayıları | SLA tanımlanmalı |
| Multi-instance deployment | CQRS-11D claim/lease bekleniyor |
| Appointment-specific dead-letter replay CLI | Mevcut değil |
| Query DB outage otomatik fallback | Bilinçli olarak yok |
| Staging ortamında canlı A→B→C doğrulama | CQRS-11A araçları hazır; ortam bekliyor |

---

## Ek: İnceleme özeti (15 soru)

| # | Soru | Cevap |
|---|------|-------|
| 1 | Migration sırası? | Command `migrate` → Query `migrate-query` → API deploy (Mode A) |
| 2 | Command/Query migration ayrı mı? | **Evet**, ayrı DbContext ve komutlar |
| 3 | API öncesi kontroller? | Backup, health snapshot, mode kaydı, queue=0, parity, tek instance, Mode A |
| 4 | Rebuild ne zaman zorunlu? | Query boş/bozuk, parity mismatch; normal deploy'da değil |
| 5 | Query outage fallback? | **Yok** |
| 6 | Retry-waiting otomatik? | **Evet**, `NextAttemptAtUtc` sonrası |
| 7 | Dead-letter otomatik? | **Hayır** |
| 8 | Manuel replay aracı? | Appointment-specific CLI **yok**; genel admin API escalation ile |
| 9 | Projector kapalıyken event? | Outbox'ta birikir (`pendingCount` artar) |
| 10 | API vs DB migration rollback? | **Bağımsız**; migration auto-rollback önerilmez |
| 11 | Query kaybında Command yeterli mi? | **Evet** + rebuild |
| 12 | Backup/restore? | **Harici ops** sorumluluğu |
| 13 | Güvenli komutlar? | Health, preflight, staged rollout dry-run/apply, migrate |
| 14 | Destructive? | Rebuild, outbox DELETE, manuel ProcessedAtUtc, Query drop |
| 15 | API/projector stop? | Rebuild önerilir; flag rollback'te projector **açık** |
