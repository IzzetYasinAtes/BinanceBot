# Loop 3 — Research: Equity-Aware Sizing + Min Notional + Fee

**Agent:** binance-expert
**Tarih:** 2026-04-18

## Testnet ExchangeInfo (Canlı Doğrulama)

| Sembol | minQty | maxQty | stepSize | minNotional | tickSize |
|---|---|---|---|---|---|
| BTCUSDT | 0.00001 | 9000 | 0.00001 | **5 USDT** | 0.01 |
| BNBUSDT | 0.001 | 900000 | 0.001 | **5 USDT** | 0.01 |
| XRPUSDT | 0.1 | 9222449 | 0.1 | **5 USDT** | 0.0001 |

## Fee
- VIP 0 (default yeni kullanıcı): **maker %0.1, taker %0.1**
- BNB indirimi: %0.075
- PaperFillSimulator zaten `TakerFeeRate = 0.001m` ✓

## Doğru Sizing Formülü
```
1) qty_risk     = (equity × riskPct) / stopDistance
2) notional_cap = equity × maxPositionPct
3) qty_capped   = min(qty_risk × entryPrice, notional_cap) / entryPrice
4) qty_final    = floor(qty_capped / stepSize) × stepSize
5) notional_final = qty_final × entryPrice
6) IF notional_final < minNotional → SKIP (trade atla)
```

## 100$ Portföyde Pratik Önerge
- maxPositionPct = **%15** → her sembol $15 tavan
- riskPct = **%1** (gerçek risk genelde cap'le küçük kalır)
- 3 pozisyon eş zamanlı = $45 deployed, %55 reserve
- Slippage: testnet'te depth zayıf, **%0.05 sabit slippage** eklenmesi önerilir

## KEŞFEDİLEN 2 BUG (Loop 3'e dahil)

### BUG-A: `PaperFillSimulator.ValidateFilters` MARKET order için minNotional kontrolü YOK
- src/Infrastructure/Trading/Paper/PaperFillSimulator.cs satır 192-208
- Sadece Limit/LimitMaker için filter check; MARKET geçer
- $2 notional MARKET order paper'da "başarılı", mainnet'te reject edilir → distorsiyon

### BUG-B: `StrategySignalToOrderHandler` hardcode `0.001m BTC` (satır 56)
- BNBUSDT için 0.001 BNB ≈ $0.63 notional → minNotional altı → reject
- XRPUSDT için 0.001 XRP geçersiz (stepSize 0.1 ile uyumsuz)
- **Bu bug Loop 2'deki "0.0290 BNB" gözlemine taban — sinyal bazlı sizing değil, fixed**

## Stop-Loss Spot Desteği
- STOP_LOSS, STOP_LOSS_LIMIT, OCO Binance Spot'ta var
- Paper modda price tracker yeterli (gerçek order tipi gerekmez)
- Mevcut `StrategySignalToOrderHandler` Exit signal ignored (satır 31-34) — **Loop 3 #4 fix**

## Kaynaklar
- https://testnet.binance.vision/api/v3/exchangeInfo
- https://binance-docs.github.io/apidocs/spot/en/#filters
- https://www.binance.com/en/fee/schedule
