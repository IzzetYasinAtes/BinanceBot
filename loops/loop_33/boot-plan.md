# Loop 33 — Boot Plan

**Tarih:** 2026-04-22
**Önceki Loop:** 32 (FAIL — PnL bug + zombie pos + sessizlik)
**Reform Kapsamı:** fee-aware domain + sizing 4x + UI hero 3-kart + yeni strateji (AR-GE)

## 1. Hazırlık Checkliste (Boot Öncesi)

- [x] PnL teşhisi (`loops/loop_32/diagnosis-pnl-discrepancy.md`)
- [x] Sessizlik teşhisi (`loops/loop_32/diagnosis-silence.md`)
- [x] Fix A — netPnl cash-grounded (commit ce3e87...)
- [x] ADR-0020 — Fee-aware Position + cash-symmetric simulator (doc)
- [x] ADR-0020 impl — Domain + migration + simulator + handler + tests 236/236 yeşil (commit c85227a)
- [ ] ADR-0021 — Sizing %20 / $20.10 / MaxOpenPositions=3 (backend-dev async)
- [ ] Dashboard hero 3 kart (frontend-dev async)
- [ ] Strateji AR-GE raporu (binance-expert async)
- [ ] Yeni Strategy Evaluator (AR-GE şampiyonuna göre)
- [ ] appsettings Seeds[] güncelleme (yeni strateji params)
- [ ] tester Playwright + DB sanity + API contract
- [ ] reviewer SOLID/security/WS resiliency scan

## 2. Boot Sıralaması (Adım Adım)

### Step 1 — API durdurma ✔ (zaten durduruldu)
API PID 15632 terminate edilmiş. Hiç BinanceBot.Api process çalışmıyor.

### Step 2 — Migration uygulama
```bash
cd D:\repos\BinanceBot
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```
Beklenen: `20260422191320_Loop33AdrZeroZeroTwentyFeeAware` migration applied. Positions tablosuna `EntryCommission` + `ExitCommission` kolonları eklenir, backfill SQL çalışır (Loop 32 closed 25 pozisyonu için quote-eq fee hesabı).

### Step 3 — DB Reset
```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d BinanceBot -i loops/loop_33/db-reset.sql
```
Script: `loops/loop_33/db-reset.sql` (zaten hazırlandı). Reset scope:
- Orders/OrderFills/Positions (Paper only)/StrategySignals/SystemEvents temizle
- Strategies UPDATE (Status=3 Active reset)
- VirtualBalance Paper: Cash=Equity=$100, ResetCount++
- RiskProfile: CB=Normal, Losses=0, PeakEquity=100, DD=0, **MaxOpenPositions=3**

### Step 4 — API başlatma
```bash
cd D:\repos\BinanceBot
dotnet run --project src/Api --no-build > loops/loop_33/api-startup.log 2>&1 &
```
Process background. Seeder appsettings Seeds[] okuyup Strategies tablosuna yazar.

### Step 5 — Sağlık kontrolleri
- `GET /api/health/ready` → 200
- `GET /api/strategies` → 4 coin × Type=<yeni> × Status=Active
- `GET /api/portfolio/summary` → startingBalance=100, currentCash=100, netPnl=0, 0 açık/kapalı
- `GET /api/risk/profile` → MaxOpenPositions=3, CB=Normal

### Step 6 — Loop 33 Health Check Cycle (4h)
- t30 / t90 / t150 / t210 / t240
- ScheduleWakeup ile self-triggering
- Halt kriterleri:
  - 3 ardışık zarar → PAUSE (kullanıcıya sor)
  - CB Tripped → halt
  - WS disconnect 5+ dk → halt
  - Error flood (>5 distinct error/30dk) → halt
  - net < -$0.05 → halt + fine-tune

## 3. Sonlandırılacak Sorular (AR-GE sonrası)

- Şampiyon strateji hangi: Breakout / OFI / 5m VWAP / Altcoin / MeanReversion?
- TP/SL/MaxHold parametreleri (AR-GE önerileri)
- KlineInterval: 1m koru veya 5m geç?
- Sembol seçimi: BTC/ETH/BNB/XRP devam veya değişim (SOL/ADA/DOGE altcoin rotasyonu mümkün mü)?

## 4. Loop 33 Başarı Kriteri

- İlk 4 saat içinde:
  - Min 10 kapalı trade (strateji canlı)
  - WR ≥ %55 veya net > $0 (break-even+)
  - Hero "Toplam Net" ≥ +$0.10
  - 0 zombie pozisyon (TimeStop çalışıyor)
  - 0 CB trip
- 4h başarılı → Loop 34 (iteratif ince ayar)
- 4h fail → Loop 33 halt + diagnose + Loop 34 reform

## 5. Riskler

- **Migration backfill time-window ±60s best-effort** — eski 25 closed pos için EntryCommission/ExitCommission kaba tahmin. UI'da çok eski trade'ler için doğruluk düşük, yeni trade'lerde eksiksiz. Kabul edilir.
- **Monitor silent fail bug** (Loop 32 tespit — 3 zombie 26h açık rağmen StopPrice+MaxHoldDuration dolu):
  - StopLossMonitorService kod doğru, parametre doğru — ama API 18:53-19:03 arası 10dk aktif iken `TickOnceAsync` hiç SystemEvent üretmedi (TimeStopTriggered log yok)
  - Muhtemelen: MediatR.Send silent exception yutuyor, log sadece Console'a gidiyor DB iz yok
  - **Loop 33 t30 health check**: eğer ilk pozisyonlar MaxHold aşar ve kapanmazsa → acil halt + teşhis + Loop 34 monitor fix scope
  - Savunma: Monitor'ün her tick sonunda "pulse" SystemEvent atması düşünülebilir (Loop 34 iyileştirme)
- **Sizing %20 ile drawdown riski artar** — MaxConsecutiveLosses=8 (appsettings) ile auto CB-trip eşik: 8 × $20 × 0.15% SL = $0.24 = %0.24 drawdown. Yeterli buffer.
