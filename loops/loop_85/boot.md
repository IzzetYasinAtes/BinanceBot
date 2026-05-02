# Loop 85 Boot — UI Cash Fix + Tick 30s→5s + Paper Realism (2026-05-02 23:35 TR)

## Pivot Sebebi (Kullanıcı 3 Kritik Sorun)
1. **UI bug yine** — "Toplam Net K/Z +$155.58" phantom (gerçek -$0.61 realized)
2. **Zaman aşımı (MaxHold) olmamalı** — live'da yok, paper'da da olmamalı
3. **SL/TP iyi ayarla** — paper canlıyı birebir simüle etmeli

## Loop 85 Değişiklikler (Sıfır Cod Code Path Bug Fix + Realism)

### 1. UI Cash Bug Fix (backend-dev)
- `GetPortfolioSummaryQuery.cs` refactor: VirtualBalance.CurrentBalance snapshot artık **otorite değil**
- Yeni hesap: `ledgerCash = Start + Σclosed.Realized - Σopen.notional - Σopen.commission`
- Snapshot drift'ten bağımsız (Loop 18 → 32 → şimdi 3. defa fix)
- 1 yeni test (LedgerCash_DerivesFromPositions), 321/321 PASS
- DB UPDATE manuel cash düzeltme: $355.14 → $198.64 (Equity $499.05 = gerçek -$0.95)

### 2. MaxHold Kaldırıldı
- 5 strateji ParametersJson `MaxHoldMinutes`: 60 → **0**
- Pattern composer Position.MaxHoldDurationSeconds=0 yazar
- Position'lar artık SL/TP/Trailing/BE-stop ile kapanır (zaman aşımı yok)
- Live davranışı ile birebir

### 3. Tick Interval 30s → 5s (binance-expert spec P1-A)
| Service | Eski | **Yeni** |
|---------|------|----------|
| StopLossMonitorService.TickInterval | 30s | **5s** |
| TakeProfitMonitorService.TickInterval | 30s | **5s** |
| MarkToMarketWorker.Cycle | 30s | **5s** |

→ Volatil pencerede SL/TP **6x daha hızlı** trigger. Live exchange-side yakınlık.

### 4. Paper Realism Konservatifleştirme (binance-expert spec P1-B + P2-A)
| Param | Eski | **Yeni** |
|-------|------|----------|
| FixedSlippagePct | 0.0001 (1bp) | **0.0005 (5bp)** |
| SimulatedLatencyMs | 100 | **120** |
| UseBnbFeeDiscount | true (%0.075) | **false (%0.10)** |

→ Konservatif simülasyon: BNB indirimsiz tam fee + spike slippage hedge.

## Loop 84 Carryover Status
- 8 close (Realized -$0.61), 3 açık (UPnL -$0.16)
- Açık 3 pozisyon BTC/ETH/XRP devam edecek (mevcut SL/TP/Trailing ile kapanır)
- Yeni emit'ler Loop 85 spec ile gelir

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 19636 |
| Port | 5188 |
| Build | 0/0 ✓ |
| Tests | 321/321 PASS |
| 5 Pattern Strateji | Active (MaxHold=0) |
| CB | Healthy (Counter 0/4 reset) |
| **VirtualBalance Cash** | **$198.64** (DB UPDATE düzeltildi) |
| **VirtualBalance Equity** | **$499.05** (gerçek) |

## L80→L85 Stack
| Loop | Ana Değişiklik | Net |
|------|----------------|-----|
| L80 | ADX gate + BBR vol + counter fix | -$0.52 |
| L81 | Pattern-based scalping pivot | -$0.38 |
| L82 | Trailing 0.0015→0.0025, BE Trigger 0.0010→0.0020 | -$0.22 |
| L83 | BE Offset 0.001→0.002, Trail 0.0025→0.0050 | $0 |
| L84 | Composer hard-gate skip kaldırıldı | -$0.004 |
| **L85** | **Cash bug fix + tick 5s + paper realism + MaxHold 0** | **HEDEF +$** |

## L85 KPI
| Metrik | Hedef |
|--------|-------|
| Realized 4h | ≥$0 (ilk pozitif loop hedef) |
| WR | ≥%30 (10+ trade) |
| BE-stop pozitif | %30+ (Loop 84'te %22 peak hedef altı, Loop 85 5s tick + 5bp slippage daha gerçekçi) |
| Frekans | 4-8 emit/h |

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 86
- 4+ ardışık küçük loss → spec yanlış
- 5+ ardışık SL → CB tripped

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (00:05 TR)**

— PM 2026-05-02 Loop 85 boot (UI cash fix + 5s tick + 5bp slippage + MaxHold 0, kullanıcı 3 sorunu çözüldü)
