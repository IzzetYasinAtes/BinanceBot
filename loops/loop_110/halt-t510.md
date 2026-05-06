# Loop 110 Halt — t510 Pozitif Tepe Görüldü AMA Realize Edilemedi

Tarih: 2026-05-06 01:28 UTC | Boot: 2026-05-05 16:41 UTC | Süre: 8h47m

## Loop 110 Tepe Noktası

| t-Time | netPnl | Notlar |
|---|---|---|
| t30 | +$0.034 (realized) | ADA winner |
| t60 | -$0.45 | 2W |
| t90 | -$0.32 | 5 close (1W/4L) |
| t120 | -$0.52 | BTC BE armed |
| t195 | **+$0.30 (UPnL)** | İlk pozitif net |
| t225 | **+$1.58** | trueEquity $501.58 |
| t255 | +$1.73 | ADA mark $0.2640 (entry+%2.0) |
| **t315** | **+$2.42** ← TEPE | trueEquity $502.42 |
| t345 | +$1.40 | mark çekildi |
| t390 | +$0.89 | eridi |
| t420 | +$0.35 | eridi |
| t465 | +$0.11 | nearly breakeven |
| **t510** | +$0.78 (force close öncesi) | manuel reset yapıldı |

## Force Close Sonucu (Beklenmiyor)

PaperTrade reset endpoint çağrıldı (force-close 2 pos beklendi + realize):
- forceClosedPositionCount: 2
- deletedPositionCount: 7
- deletedOrderCount: 13
- deletedSystemEventCount: 144

**SONUÇ**: PaperTrade reset force-close değil **DELETE** yapıyor. Loop 110'un cumulative realized -$0.17 + ADA +$1.17 + BTC -$0.11 ≈ **+$0.89 NET REALIZED OLMADI** — sermaye $500'de yeniden sıfırlandı (reset bug).

## 30 Loop Pozitif Gözlemler

- ✅ İlk gerçek BE-armed pozisyon (BTC + ADA)
- ✅ İlk gerçek trailing aktif
- ✅ Peak entry+%2.5 ADA pos
- ✅ netPnl +$2.42 tepe noktası (trueEquity $502.42)
- ✅ Loop 91 BE-stop matematiği gerçek pazarda doğrulandı

## 30 Loop Tespit Edilen Bug'lar

1. **Long pos için SL hit semantik**: Mark < SL Long pos için exit etmeli AMA BE armed sonrası SL entry üstüne çıkıyor, mark altta hata kaldıkça pos açık kalıyor (Loop 108 ETH, Loop 110 BTC örneği)
2. **MaxHold timeout**: 60min limit aşıldığında pos kapanmıyor (Loop 108-110 7+ saat hold)
3. **Hard MaxHold safety net**: 120dk eşik tetiklemiyor (Loop 109 commit deploy edildi AMA çalışmıyor)
4. **Trailing peak update**: Peak güncellenip de SL'i yukarı taşımıyor (ADA peak $0.26540, SL $0.25934 sabit kaldı)
5. **PaperTrade reset force-close**: Realize değil delete yapıyor (cumulative kayboluyor)

## Loop 111 Spec

Backend-dev'e büyük delegasyon — Position lifecycle bug fix:

1. **SL hit semantic Long+Short**: Long mark <= SL exit (BE armed sonrası bile), Short mark >= SL exit
2. **MaxHold timeout**: pos.OpenedAt + MaxHoldDuration <= now → CloseSignalPositionCommand
3. **Hard MaxHold safety net çalışsın**: 120dk eşik tetiklensin (Loop 109 fix deployed AMA aktif değil)
4. **Trailing peak update**: peak güncellendiğinde SL = peak × (1 - TrailPct) yansısın
5. **PaperTrade reset force-close**: Realize → delete YERINE realize → keep (Position.Close)

## Sonraki

Bot restart, RP+Strategies cleanup, Loop 111 boot.md, backend-dev delegasyon.
