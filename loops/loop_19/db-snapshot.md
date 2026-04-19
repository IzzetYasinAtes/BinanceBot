# Loop 19 DB Snapshot (reform öncesi)

Tarih: 2026-04-19
Amaç: DB sıfırlama öncesi mevcut verilerden algoritma davranışını analiz et, reform kararlarına input ver.

## Özet Metrikler

| Metrik | Değer |
|---|---|
| Closed positions | 16 |
| Wins / Losses | 7 / 9 (**%43.75 WR**) |
| Toplam Realized PnL | **-$0.3938** |
| Ortalama PnL / trade | -$0.0246 |
| En iyi trade | +$0.1120 (XRP short 622s) |
| En kötü trade | -$0.2715 (XRP short 622s, aynı sembol!) |
| Virtual balance | $100.00 → **$98.69** (-%1.31 net) |
| Ortalama hold | **9.82 dk** |
| Min / Max hold | 6 dk / 10 dk (**622s = 10.37dk time-stop**) |
| Toplam signal | 48 |
| Toplam Filled order | 39 |
| Aktif stratejiler | 3 (BTC/BNB/XRP Pattern-Scalper, Status=3=Paused) |

## Kritik Bulgu — TP Asla Tetiklenmemiş

**16 closed pozisyonun TAMAMI time-stop ile kapanmış** (hold 622s = 10dk civarı, sadece 1 tanesi 360s, birkaçı 480/531s — muhtemelen stop-loss).

→ Mevcut pattern detector tasarımı **take-profit'e ulaşacak hareketleri predict edemiyor.**
→ Time-stop kazananı da kaybeden de kesiyor; formasyon hedefi abartılı/yanlış.
→ Kartopu için TP hit oranı şart; şu anda %0.

## Sembol Bazında

| Symbol | Closed | Wins | Sum PnL | Avg PnL |
|---|---|---|---|---|
| BTCUSDT | 4 | 2 | -$0.0758 | -$0.0190 |
| BNBUSDT | 7 | 2 | -$0.1305 | -$0.0186 |
| XRPUSDT | 5 | 3 | -$0.1874 | -$0.0375 |

XRP %60 WR olmasına rağmen net en zararlı — **tek büyük kayıp (-$0.27) küçük 3 kazancı yedi** (R:R asimetrisi ters).

## En Kötü 5 Trade (Side: 1=Long, 2=Short)

| Symbol | Side | Qty | Entry | Exit | PnL | Hold(s) |
|---|---|---|---|---|---|---|
| XRPUSDT | Short | 112.6 | 1.4176 | 1.4200 | **-$0.2715** | 622 |
| BNBUSDT | Long | 0.064 | 620.82 | 619.06 | **-$0.1129** | 621 |
| XRPUSDT | Long | 28.0 | 1.4269 | 1.4237 | **-$0.0920** | 622 |
| BTCUSDT | Long | 0.00053 | 75284.32 | 75198.96 | **-$0.0452** | 621 |
| BTCUSDT | Short | 0.00053 | 75112.19 | 75196.41 | **-$0.0446** | 360 |

## Çıkarılan Dersler (reform için)

1. **Time-stop hegemonyası:** 10dk pencere algoritmanın asıl "performans" metriği değil, zorla çıkış. Yeni algoritma ya TP'yi daha gerçekçi mesafeye koymalı ya da timeframe içinde gerçekleşebilir bir edge sunmalı.

2. **R:R ters:** Best +$0.11 vs Worst -$0.27 → ortalama kazanç ortalama kaybın yarısından az. %55+ WR zorunlu; şu anda %43.75.

3. **Sizing tutarsız:** Pozisyonlar 0.064 BNB sabit miktar — kullanıcının yeni kuralı `max(bakiye × %20, $20)` ile dinamik olmalı, kartopu için elzem.

4. **Fee drag gözle görünür:** 39 fill × ~$20 notional × %0.1 taker = ~$0.78 fee → -$0.39 net'in içinde yarısı fee. Yeni algoritma %1-2 net hedefini sağlamak için taker fee'yi hesaba katıp entry/TP mesafesini ayarlamalı.

5. **Short trade'ler spot'ta olmamalı:** Side=2 (Short) 5 pos var, spot long-only olduğu için bu paper sim'de oluşmuş olabilir ama mainnet'e geçişte anlamsız. Yeni algoritma **long-only** olmalı.

## DB Boyutu

- 15 tablo mevcut (Orders, Positions, VirtualBalances, Strategies, StrategySignals, Klines, BookTickers, OrderBookSnapshots, Instruments, RiskProfiles, BacktestRuns, BacktestTrades, SystemEvents, OrderFills)
- Klines/BookTickers potansiyel büyük; reset'te DROP CREATE yapılacak.

## Reform'a Aktarılacaklar

- %20 dinamik sizing min $20 kuralı (PositionSizingService yeni)
- TP realize oranı ≥ %50 hedef (algoritma revizyonu)
- WR hedef ≥ %55, ortalama R:R ≥ 1.3
- Long-only spot
- Time-stop sadece fallback (primary exit TP veya SL olmalı)

---
Kaynak: `BinanceBot` localdb 2026-04-19 09:34 UTC snapshot.
