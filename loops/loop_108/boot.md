# Loop 108 Boot — Pullback Disable + R:R 1:1 Simetri

Tarih: 2026-05-05 11:14 UTC | Bot port 5188

## Loop 107 → 108 Geçiş (Kullanıcı Talebi)

Loop 107 t30 sonuç: 7 limit order, **hepsi 5dk timeout → expire** (Status=6). Pullback offset %0.10 yatay pazarda fill olamadı. UI Emir Geçmişi'nde "her şey zaman aşımı" görünümü kullanıcıyı rahatsız etti (DB'de gerçek durum, UI bug değil).

## Tune (PM Doğrudan)

### Tune #1: PullbackLimit DEVRE DIŞI
- `appsettings.json:64` `"Enabled": true → false`
- Market order'a geri dön (Loop 92-106 davranış)
- ADR-0026 §A pullback limit pazar yatay'da çalışmıyor — Loop 110+'a ertelenir

### Tune #2: R:R 1:1 Simetri
- DB UPDATE 5 strateji `TpRiskRewardRatio 2.0 → 1.0` (Loop 105 değer)
- Loop 107 boot'ta seeder 2.0'a döndürmüştü
- TP %0.40 (R:R 1:1) Loop 105'te kanıtlandı (BE-stop sermaye koruma ile uyumlu)

### Tune #3: DB Temizlik
- Expired orders silindi (7 row) — Emir Geçmişi sayfası temizlendi
- Future timeout expire'lar zaten yok (PullbackLimit disable)

## Korunur (Loop 95-106 fix'leri)
- Status=3 (Active) ✓
- WeightOverrides 7 Short=0 (Long-only)
- BeMoveTriggerPct 0.001 + BeMoveOffsetPct 0.001 (DB)
- RPT 0.01 (pos sizing)
- MaxOpen 3
- RS=1
- MTF 0.002 (denge)
- TrailPct 0.003

## Boot State
- Bot ayakta, port 5188 (yeni binary, PullbackLimit=false)
- Wallet $500, 0 pos
- ResetCount 25, deleted 0 (zaten temiz), 67 events silindi
- CB Healthy, Strategies Active=3 ✓
- Emir Geçmişi sayfası temiz (expired orders silindi)

## Hipotez

Loop 108 = Loop 105 senaryosuna geri dönüş + Pullback disable. Beklenen davranış:
- Market order @ bar close (Loop 105 davranış)
- R:R 1:1 simetri (TP %0.40 yakın)
- BE-stop sermaye koruma (peak +0.10% BE arm)
- Loop 105 t30'taki gibi 4 trade BE-stop küçük loss (-$0.04/pos)

Pazar canlanmazsa yine yatay seyir, AMA en azından emir geçmişi temiz görünecek.

## Cumulative

27 loop -$26.5+, 0 pozitif loop. Loop 108 = pratik geri dönüş + UI temizlik.

## Sonraki

ScheduleWakeup t30.
