# Loop 41 — HALT @ t=210dk (2026-04-24 14:09 TR)

## 🚨 HALT TETİKLENDİ — İKİ KRİTER AYNI ANDA

| Kriter | Eşik | Gerçek | Verdict |
|---|---|---|---|
| Realized PnL | < -$1.50 | **-$1.7985** | 🔴 HALT |
| Ardışık SL | ≥ 5 | **7 (LTC)** | 🔴 HALT |
| 8/8 Loss = 0% WR | (sayı az) | 0K / 8L | 🔴 |

API süreci durduruldu (PID 4776, port 5188 free).

## Trade Özeti
| Symbol | Trade | TP | SL | TimeStop | Realized | Avg Hold | Komisyon |
|---|---|---|---|---|---|---|---|
| BNBUSDT | 1 | 0 | 1 | 0 | -$0.3976 | 34m36s | $0.150 |
| **LTCUSDT** | **7** | **0** | **7** | **0** | **-$1.4009** | **67s** | $1.05 |
| **TOPLAM** | **8** | **0** | **8** | **0** | **-$1.7985** | — | $1.20 |

Final state: Cash $498.20 / Equity $498.20 / netPnl **-$1.7985 (-%0.36)**

## 🔥 KÖK NEDEN — LTCUSDT WHIPSAW LOOP

7 LTC trade detay:
```
T+0:00  Entry $56.21 → SL $56.13  (slippage)  hold 7m   (ilk uzun)
T+8:00  Entry $56.14 → SL $56.11  hold 8s    ← yeniden açıldı (cooldown YOK)
T+9:00  Entry $56.15 → SL $56.12  hold 7s
T+10:00 Entry $56.15 → SL $56.12  hold 7s
T+11:00 Entry $56.15 → SL $56.12  hold 7s
T+12:00 Entry $56.15 → SL $56.12  hold 8s
T+13:00 Entry $56.10 → SL $56.10  hold 8s
```

**Aynı SL/TP seviyesi 7 trade boyunca SABİT** ($56.1475 / $56.5413) — Volume + ATR statik kalmış, sinyal yeniden tetiklemesi engellenmedi.

### Neden Cooldown Çalışmadı
- AR-GE'de tasarım: `CooldownBarsAfterSignal=4` (60dk = 4 × 15m bar)
- backend-dev impl notu (loop 41 boot): *"Cooldown V1 stateless — evaluator-level enforcement yok, SRP kararı. RiskProfile.MaxOpenPositions=5 yaklaşık koruma."*
- Gerçek: Pozisyon SL'de kapanır kapanmaz `MaxOpenPositions` filtresi tekrar serbest, **bir sonraki 1m kline tetiğinde aynı sinyal yeniden açıldı**.
- Evaluator her 1m bar'da değerlendirildi (Donchian filtresi 15m bar kapanışı gerektirse de, evaluator çağrılma sıklığı 1m).
- LTC fiyatı $56.13-$56.15 dar bandında salınınca: **her bar'da Donchian üst kırılım + Volume Z spike + bar kapanış** koşulları hep tekrar tetiklendi.

### İkincil Neden — Volume Z & Donchian Threshold "Pegged"
- LTC 15m kline'ları aynı seviyede konsolide olunca:
  - Donchian Period 20-bar high sabit
  - Volume Z spike eşiği bir kez aşılınca → her bar'da sürekli aşıldı (volume gerçekten yüksek)
- Sonuç: Aynı sinyal mat. olarak emin tekrar üretti.

## Halt Diğer Kriterler
| Kriter | Durum |
|---|---|
| Zombie >270dk | ✓ (en uzun BNB 34dk) |
| WS disconnect | ✓ Streaming |
| CB Tripped | ✓ HEALTHY (CB henüz tetiklenmedi) |
| Console error | ✓ 0 |

CB değer-bazlı drawdown'a baktığı için 24h DD %20 eşik (-$100) çok geniş — single trade halt logic burada eksik. Ama PM-level halt kriterlerimiz tetikledi.

## Loop 42 — Fine-Tune Önerisi (kullanıcı onayı bekleniyor)

### Mutlak (P0) — Cooldown Enforcement
**backend-dev** task:
1. `Infrastructure/Strategies/CooldownService.cs` (yeni) — in-memory `Dictionary<(StrategyId, Symbol), DateTimeOffset LastSignalAt>`
2. Veya: `DonchianBreakoutEvaluator` evaluator-içi cooldown (ContextJson'dan parametre okunup state lookup)
3. Enforcement: yeni sinyal → `(now - lastSignalAt) < CooldownBarsAfterSignal × 15m` ise skip
4. SystemEvent SignalSkipped reason: "cooldown" ek alan
5. Test: 5 dakika içinde aynı sembolde 2. sinyal tetiklenmemeli

### Önerilen (P1) — LTC ve Düşük-vol Coin Blok
**Loop 41 verisinde LTC matematiksel olarak whipsaw'a açık** (dar band, statik ATR). 
- LTC seed Activate=false (BNB de Activate=false — single SL ama benzer pattern riski)
- Loop 42 başlangıç: 10 coin (LTC + BNB blok) → 1-2 hafta gözle, performans iyiyse geri ekle

### Önerilen (P2) — Per-Symbol Min Atr Pct Yükselt
- Mevcut `MinAtrPct=0.0006` (%0.06) — düşük vol coinleri kabul ediyor
- Loop 42: `0.0010` (%0.10) → düşük volatilite saatlerinde sinyal blok

### Önerilen (P3) — Position Entity'ye ClosedReason
**backend-dev** task:
- `Position.ClosedReason` enum (TP / SL / TimeStop / Manual / ForceCloseReset)
- `Position.Close(exitPrice, reason, time)` method'a parametre
- DB migration + UI display

### Önerilen (P4) — UI Backlog
- Pozisyonlar Açık tab: TP/SL kolon değer ata (Position.StopPrice/TakeProfit zaten DB'de)
- Pozisyonlar Kapalı tab: Komisyon kolonu doldur (Position.EntryCommission+ExitCommission)

## Yapılacak (PM)
1. ✅ API durduruldu
2. ✅ halt-t210.md yazıldı
3. ⏳ Kullanıcıya checkpoint + Loop 42 onayı bekle
4. ⏳ Onay alınınca: backend-dev → cooldown impl
5. ⏳ Sonra: Loop 42 boot (DB reset, $500, yeni config, smoke)

## Ham Veri
DB pozisyon detay: bkz `loops/loop_41/check-t150.md` + bu rapor

— PM 2026-04-24 t=210 (halt)
