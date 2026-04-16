# Plan Review Notes — Master Plan Kalite Kapısı

**Reviewer:** `reviewer` agent · **Task:** `adhoc-master-plan-5-review` · **Durum:** 🚫 BLOCKER (7 blocker + 14 nit)

Architect adım 6 (final sentez / `docs/plan.md`) öncesi 7 blocker çözülmeli; 14 nit plan.md'de "Bilinen Boşluk" olarak not düşülmeli.

## Özet Tablosu

| Bölüm | BLOCKER | NIT | PASS |
|---|---|---|---|
| A. Kural uyumu (CLAUDE.md) | 1 | 3 | 7 |
| B. SOLID | 1 | 2 | 4 |
| C. WS resiliency | 0 | 2 | 9 |
| D. Security | 1 | 3 | 8 |
| E. TODO / muğlak | 0 | 3 | — |
| F. Kapsama tutarlılığı | 4 | 1 | — |
| G. Ek teknik | 1 | 1 | — |
| **Toplam** | **7** | **14** | **28+** |

---

## A. Kural Uyumu (reviewer-diff-review)

### BLOCKER

- 🚫 **A1 / architecture-notes.md:494 · frontend-design.md:7,233** — CLAUDE.md yasaklar listesinde SignalR yok; frontend-design "SignalR yasak" diyor → çelişki. **Karar:** `NOT_IN_SCOPE (MVP'de polling yeterli; SignalR ikinci faz ADR)` olarak yumuşat veya CLAUDE.md güncelle.

### NIT

- ⚠️ **A2 / backend-design.md:757** — `appsettings.json` template'inde gerçek görünümlü `ConnectionStrings.Default` default taşıyor. ADR-0004 §4.3 boş string istiyor. Kaza ile dev master DB'sine bağlantı riski → boş string'e çek.
- ⚠️ **A3 / frontend-design.md:362** — `localStorage.getItem('admin.key')` önerisi XSS riski (bkz. D1). Yazım "öneri" değil "uyarı"ya dönmeli; auth ADR netleşince burası kesinleşsin.
- ⚠️ **A4 / backend-design.md:272** — `NetEscapades.AspNetCore.SecurityHeaders` "MVP opsiyonel" işaretli; CSP+HSTS baseline sayılır, opsiyonel olmamalı. Paket major <1 stabilite dipnotu da plan'a not düşsün.

### PASS

- ✅ Clean Architecture 4 layer + dependency yönü (architecture-notes §2); Domain import'suz.
- ✅ Aggregate-per-klasör (backend-design §1.3).
- ✅ Lazy loading yasağı + explicit Include (backend-design §1146).
- ✅ Exception-for-control-flow yasağı + Result<T> (backend-design §1147).
- ✅ Repository-per-entity yasağı + aggregate-root-based (backend-design §1148).
- ✅ Otomatik migration + manuel `dotnet ef database update` yasağı (ADR-0001).
- ✅ npm/bundler/SFC/Pinia yasakları + gerekçe (frontend-design §1).

---

## B. SOLID (reviewer-solid-check)

### BLOCKER

- 🚫 **B1 / backend-design.md:407-413, 582** — ISP ihlali / çelişkili interface hiyerarşisi. Paragraf "Tüm typed client'lar `IBinanceRestClient` **tek** port olarak Application'a expose" diyor; hemen altında 3 ayrı interface listeliyor (`IBinanceMarketData`, `IBinanceTrading`, `IBinanceAccount`). "Alt port" terimi muğlak. **Karar:** 3 segregated interface + handler'lar yalnız ilgili port'u alır → ISP. "Tek port" ifadesi silinmeli.

### NIT

- ⚠️ **B2 / architecture-notes.md:211,223** — `StrategyParameters` VO JSON-backed + her Strategy type için ayrı schema → switch-by-type OCP riski. Discriminated union / sealed hierarchy daha OCP-uyumlu; plan.md "bilinen borç" notu.
- ⚠️ **B3 / architecture-notes.md §1.5** — Strategy aggregate hem config hem signals hem lifecycle hem auto-deactivate taşıyor → SRP patlaması + aggregate boyut riski. `StrategySignal` ayrı aggregate / append-only log olmalı mı? Architect karar.

### PASS

- ✅ Trade ayrı aggregate (TX sınır farkı, volume büyük) gerekçeli.
- ✅ BookTicker read-model (anemic kaçınma) doğru.
- ✅ Order + OrderFill composition + state machine.
- ✅ RiskProfile singleton + circuit breaker state machine — DDD textbook.

---

## C. WS Resiliency (reviewer-ws-resiliency)

### NIT

- ⚠️ **C1 / backend-design.md:478 · ADR-0002 §5** — `KeepAliveInterval = 30s` ile "biz ek ping göndermeyiz" çelişkili gibi. Netleştir: Binance 20s server-ping → client pong otomatik; client-initiated ping gereksiz → `KeepAliveInterval = TimeSpan.Zero` mi?
- ⚠️ **C2 / backend-design.md §5.5 · ADR-0002** — User Data Stream reconnect sonrası yeni `listenKey` + subscribe akışı eksik. 60 dk expire + restart sonrası replay disiplini yazılmalı.

### PASS

- ✅ Exponential backoff + jitter (1s→30s cap, ±20% jitter).
- ✅ 20s ping / 60s pong (research §2.3 + ADR-0002 §5 + backend §5.3 tutarlı).
- ✅ Missed-pong detection (75s local timer watchdog).
- ✅ Subscription replay on reconnect.
- ✅ Depth U/u resync 7-adım (research §2.4 + backend §5.4 + ADR-0002 §7).
- ✅ At-least-once + idempotent (ADR-0003 UNIQUE matrix).
- ✅ Graceful shutdown 4-adım (channel complete → close 1000 → drain → dispose).
- ✅ 23h preemptive reconnect (ADR-0002 §6).
- ✅ 5msg/sn + 1024 stream + 300conn/5dk IP limit farkındalığı.

---

## D. Security (reviewer-security-scan)

### BLOCKER

- 🚫 **D1 / frontend-design.md:362** — Admin API key'i browser `localStorage`'da = XSS ile tam exfil riski. Trading admin endpoint'leri para transfer potansiyeli → yetersiz. Seçenekler: (a) HttpOnly secure cookie + CSRF double-submit; (b) short-lived JWT + refresh rotation; (c) solo-dev local kiosk mode (UI'da admin endpoint yok, Swagger/.http file). Architect **ADR-0007 admin-auth-model** üretmeli; MVP için (c) en pragmatik.

### NIT

- ⚠️ **D2 / backend-design.md:272, appsettings.json:755** — CORS policy explicit değil; `AllowedHosts: "*"` tehlikeli. Production explicit origin kısıtı zorunlu.
- ⚠️ **D3 / backend-design.md §10** — CSP + HSTS değerleri dokümante edilmemiş. Minimum: `default-src 'self'; script-src 'self' cdn.jsdelivr.net unpkg.com; style-src 'self' 'unsafe-inline'`. Frontend CDN (`unpkg.com`, `cdn.jsdelivr.net`) whitelist zorunlu.
- ⚠️ **D4 / frontend-design.md §9.6** — "v-html YASAK" kuralı açık yazılmalı (interpolation escape otomatik ama disiplin açık olsun).

### PASS

- ✅ Secret yönetimi 3 katman (ADR-0004).
- ✅ Testnet-first 3 kapı (ADR-0006).
- ✅ API key redaction (HmacSignatureDelegatingHandler REDACTED).
- ✅ Request body log edilmez + max 1KB response body (backend §9.4).
- ✅ EF parameterized (raw SQL yok).
- ✅ Auth policy 4 seviyeli (User/Admin/Anon/Internal).
- ✅ Correlation ID middleware.
- ✅ DTO over-posting riski düşük (explicit command mapping + FluentValidation whitelist).

---

## E. TODO / Muğlak (grep: `ileride|TODO|şimdilik|sonraki aşama|belki|sanırım`)

### NIT

- ⚠️ **E1 / architecture-notes.md:101** — "Eğer ileride book-ticker farkı bazlı domain davranışı çıkarsa..." → senaryo tahmini. BookTicker aggregate-olmama kararı sabit; sil veya `docs/features/` slice'ına taşı.
- ⚠️ **E2 / architecture-notes.md:327** — "İleride fleet büyürse ayrı csproj'lara bölün" → ADR konusu; MVP scope dışı olarak net not.
- ⚠️ **E3 / architecture-notes.md:498** — "İleride PostgreSQL'e geçiş ihtiyacı olursa" → senaryo tahmini; ayrı ADR (0008?) konusu.

### PASS (gerekçeli NOT_IN_SCOPE)

- ✅ Futures / Triangular arbitrage (research.md:317,326).
- ✅ Transactional outbox (ADR-0003:67 + architecture-notes:490 → "MediatR in-memory MVP yeterli, crash-recovery ADR-0008").
- ✅ SRI / integrity (frontend §1 → supply-chain ADR sonra).
- ✅ Light theme / keyboard nav / JSON schema / heroicons (frontend §8.3/9.2/10.2/8.4 → faz 2).
- ✅ Prometheus metrics (backend §11 → MVP `/health/*` yeterli).

---

## F. Kapsama Tutarlılığı (kopuk zincir / hayali endpoint)

### BLOCKER

- 🚫 **F1 — Frontend referans ettiği 6 endpoint backend tablosunda YOK:**

  | Frontend atfı | Backend tablosu | Durum |
  |---|---|---|
  | `GET /api/market/summary` (frontend:56,446,479) | Yok | **Hayali** |
  | `GET /api/positions/pnl/today` (frontend:56,448,479) | Yok | **Hayali** |
  | `GET /api/risk/drawdown-history?days=30` (frontend:168,485) | Yok | **Hayali** |
  | `GET /api/logs/tail` (frontend:186,486) | Yok | **Hayali** |
  | `GET /api/system/status` (frontend:398,479,715 + ADR-0006:75) | Yok | **Hayali** |
  | `GET /api/strategies/{id}` detail (frontend:148) | Sadece list var | **Hayali** |

  Architect seçimi: (a) backend §11.1 + architecture-notes §3 CQRS envanterine 6 slice ekle; (b) frontend existing endpoint'lere daralt (örn. summary = 3 tekli `GET /api/ticker/book` + 3 `GET /api/klines?limit=1` client agrega).

- 🚫 **F2 — Endpoint adı uyuşmazlığı:** Backend `POST /api/risk/profile/override` vs frontend `POST /api/risk/override-caps`. Tek kaynak.

- 🚫 **F3 — Positions filter query yok:** Frontend `GET /api/positions?status=open|closed&from=&to=&symbol=`. Backend `GetOpenPositionsQuery()` parametresiz. Genişlet (`ListPositionsQuery`) veya ayrı `ListClosedPositionsQuery` slice (CQRS envanteri 37 olur).

- 🚫 **F4 — `SystemEvents` tablosu aggregate envanterinde yok:** ADR-0006 §6.2 Kapı 3 `DbContext.SystemEvents` referans ediyor; architecture-notes §1 9-aggregate listesinde yok. Architect karar: 10. aggregate `SystemEvent` mi, infra audit tablosu mu (aggregate-altı).

### NIT

- ⚠️ **F5 / frontend-design.md:168** — `POST /api/risk/override-caps` body shape'i dokümante edilmemiş. Backend `OverrideRiskCapsCommand` alanları (`RiskPerTradeCap`, `MaxPositionCap`, `AdminNote`) frontend DTO'ya yansımalı.

### PASS

- ✅ Aggregate × DomainEvent × Handler zinciri (architecture-notes §4.1 27 event + fan-out matrix, backend §1.3/1.1 Notifications klasör yapısı tutarlı).
- ✅ CQRS slice × Handler path × Validator × Auth (architecture-notes §3 tablo + backend §6 representative slice'lar aynı şablon).

---

## G. Ek Teknik Bulgular

### BLOCKER

- 🚫 **G1 / backend-design.md:761** — `WsBaseUrl = "wss://stream.testnet.binance.vision:9443"`. Testnet için `:9443` port eklemek bağlantı hatası riski; doğru URL `wss://stream.testnet.binance.vision` (default 443). Research + ADR-0006 §6.1 port yok → çelişki. Düzelt.

### NIT

- ⚠️ **G2 / backend-design.md:469** — ASCII state machine grafiğinde "ping miss / 60s no pong" + "24h near" aynı kenarda. 24h preemptive reconnect planlı transition; ping-miss unplanned. Ayrı arrow'lara böl.

---

## Verdict

**🚫 BLOCKER — 7 blocker + 14 nit.** Architect adım 6 (final sentez) öncesi öncelik sırası:

1. **F1-F3** — Frontend-backend endpoint zinciri kapat (6 hayali endpoint + isim uyuşmazlığı + Positions filter).
2. **F4** — `SystemEvents` 10. aggregate mi audit tablosu mu karar (yeni mini-ADR veya architecture-notes güncelleme).
3. **G1** — Testnet WS URL port düzeltmesi.
4. **B1** — `IBinanceRestClient` ISP çelişkisi (3 segregated, "tek port" ifadesini sil).
5. **D1** — Admin auth model: yeni **ADR-0007** (tercih: solo-dev local kiosk, frontend admin endpoint yok, Swagger/.http).
6. **A1** — SignalR ifadesi CLAUDE.md ile hizala veya `NOT_IN_SCOPE` yap.
7. **A2** — Connection string default boş string.

Nit'ler plan.md "Bilinen Boşluk + Faz-2 Kayıt" bölümünde listelenmeli.

## Kaynak Dosyalar

- `docs/research/binance-research.md`
- `docs/architecture-notes.md`
- `docs/adr/0001-auto-migration-on-startup.md`
- `docs/adr/0002-binance-ws-supervisor-pattern.md`
- `docs/adr/0003-idempotent-handler-discipline.md`
- `docs/adr/0004-secret-management.md`
- `docs/adr/0005-risk-limit-policy.md`
- `docs/adr/0006-testnet-first-policy.md`
- `docs/backend-design.md`
- `docs/frontend-design.md`
- `docs/glossary.md`
