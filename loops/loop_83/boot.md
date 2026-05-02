# Loop 83 Boot — BE Offset 0.001→0.002 + Trail 0.0025→0.0050 (2026-05-02 17:08 TR)

## Pivot Sebebi
Loop 82 yeni param yine 3 ardışık küçük loss (peak %0.23-0.27 hep eşik altı). binance-expert root-cause matematik analizi:
- BE armed → SL = entry × 1.001 (Loop 82 OffsetPct=0.001)
- Net win = +%0.08 (komisyon sonra), SL loss = -%0.42
- **Asimetri 5.25x → Breakeven WR %84 imkansız**
- P(BE triggered) %71 → expectancy -%0.063 (negatif)

## Loop 83 Spec — Sıfır Kod, Sadece Param

| Parametre | Loop 82 | **Loop 83** | Etki |
|-----------|---------|-------------|------|
| BreakEven.OffsetPct | 0.0010 | **0.0020** | BE armed → SL = entry × 1.002 (entry + %0.20) |
| TrailingStop.TrailPct | 0.0025 | **0.0050** | Peak'ten %0.50 buffer (high-vol bonus) |
| BeMoveOffsetPct (5 strateji) | 0.001 | **0.002** | Per-strateji aynı |

### Matematik Kazanç
- BE-stop net = %0.20 - %0.02 slippage = **+%0.18** (Loop 82'de +%0.08)
- Asimetri: 5.25x → **2.33x**
- Expectancy: -%0.063 → **+%0.009** (POZİTİF geçiş)

### L82 3 Trade Retroaktif Test
| Symbol | Peak | Loop 82 PnL | **Loop 83 PnL Beklentisi** |
|--------|------|-------------|----------------------------|
| ETH | +%0.25 | -$0.069 | **+$0.10** (BE-stop %0.20'de armed) |
| BTC | +%0.23 | -$0.060 | **+$0.10** (BE-stop) |
| ADA | +%0.27 | -$0.090 | **+$0.10** (BE-stop) |

**3/3 pozitif** → +$0.30 toplam yerine -$0.22

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 8628 |
| Port | 5188 |
| 5 Pattern Strateji | Active |
| **CB Reset** | ✓ (Counter 3→0, Healthy) |
| Açık Pozisyon | 0 |
| VirtualBalance | $655.99 (UI hesabı bozuk, gerçek -$14.57 cumulative) |

## Loop 83 KPI (binance-expert)
| Metrik | Hedef | Halt |
|--------|-------|------|
| WR | ≥%30 (10+ trade) | <%20 → halt |
| Realized 4h | ≥-$0.30 | < -$1.50 → halt |
| Avg/trade | ≥-$0.03 | < -$0.10 → halt |
| BE-stop pozitif | ≥2/3 | 0/3 → spec wrong |

## L80/L81/L82/L83 Stack
| Loop | Ana Değişiklik | Realized |
|------|----------------|----------|
| L80 | ADX gate + BBR vol + counter fix | -$0.52 (3 close) |
| L81 | Pattern-based scalping pivot | -$0.38 (4 close) |
| L82 | Trailing 0.0015→0.0025, BE 0.0010→0.0020 | -$0.22 (3 close) |
| **L83** | **BE Offset 0.001→0.002, Trail 0.0025→0.0050** | **HEDEF +$0.10+** |

## Halt Eşikleri
- Realized < -$1.50 4h → halt + Loop 84
- 4+ ardışık küçük loss → spec yanlış
- 5+ ardışık SL → CB tripped (auto)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (17:38 TR)**

— PM 2026-05-02 Loop 83 boot (BE offset 0.001→0.002 + trail 0.0025→0.0050, sıfır kod, asimetri 5.25x→2.33x)
