// Ana Panel — Portföy Durumu (7 kart) + canlı trade feed + saat-başı sayaç
// + kartopu mini chart + sembol carousel + son işlemler.
// Loop 23: hero tamamen kaldırıldı, 3 portföy KPI'si normal kart grid'ine taşındı.

import { createApp, computed, ref, watch } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";
import { SymbolCarousel } from "../components/symbolCarousel.js";
import { AnimatedNumber } from "../components/animatedNumber.js";
import { PriceTicker } from "../components/priceTicker.js";
import { SymbolLogo } from "../components/symbolLogo.js";
import { LiveTradeFeed } from "../components/liveTradeFeed.js";
import { TradeCounter } from "../components/tradeCounter.js";
import { SnowballChart } from "../components/snowballChart.js";

const SYMBOLS = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT"];
const TICKER_SYMBOLS = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT", "SOLUSDT", "DOGEUSDT"];
const HOURLY_TRADE_TARGET = 150;
const EQUITY_HISTORY_MAX = 120; // ~10 dk @ 5s polling

const App = {
    components: {
        Sidebar, ErrorBanner, SymbolCarousel, AnimatedNumber, PriceTicker, SymbolLogo,
        LiveTradeFeed, TradeCounter, SnowballChart,
    },
    template: `
        <div class="app">
            <Sidebar active="dashboard" />
            <main>
                <PriceTicker :symbols="tickerSymbols" :interval-ms="10000" />

                <div class="page-header">
                    <h1 class="page-title">Ana Panel</h1>
                    <p class="page-sub">Anlık portföy durumu, sembol hareketleri ve son işlem sonuçları.</p>
                </div>

                <ErrorBanner :error="summaryPoll.error.value" />

                <!-- PORTFÖY DURUMU — 7 KPI kartı + mini snowball chart -->
                <section class="block">
                    <h2 class="section-title">Portföy Durumu</h2>
                    <div class="portfolio-grid">
                        <div class="kpi-grid portfolio-kpis">
                            <!-- 1. Toplam Net K/Z -->
                            <div class="card card-static">
                                <div class="card-head">
                                    <h3 class="card-title">Toplam Net K/Z</h3>
                                </div>
                                <div class="card-value" :class="pnlClass(summary?.netPnl)">
                                    <AnimatedNumber
                                        v-if="summary"
                                        :value="summary.netPnl"
                                        :decimals="2"
                                        prefix="$"
                                        :signed="summary.netPnl !== 0"
                                        :duration-ms="800" />
                                    <span v-else>—</span>
                                </div>
                                <div class="card-hint" v-if="summary">
                                    <span class="badge" :class="pctBadge(summary.netPnl)">
                                        {{ fmt.pctFracSigned(summary.netPnlPct) }}
                                    </span>
                                    <span class="muted" style="margin-left:6px;">
                                        başlangıç {{ fmt.money(summary.startingBalance) }}
                                    </span>
                                </div>
                            </div>

                            <!-- 2. Mevcut Bakiye -->
                            <div class="card card-static">
                                <div class="card-head">
                                    <h3 class="card-title">Mevcut Bakiye</h3>
                                </div>
                                <template v-if="cashClamped">
                                    <div class="card-value metric-warn">$0.00</div>
                                    <div class="card-hint">
                                        <span class="badge bad" :title="cashTooltip">Limit aşıldı</span>
                                    </div>
                                </template>
                                <template v-else>
                                    <div class="card-value">
                                        <AnimatedNumber
                                            v-if="summary"
                                            :value="summary.currentCash"
                                            :decimals="2"
                                            prefix="$"
                                            :duration-ms="800" />
                                        <span v-else>—</span>
                                    </div>
                                    <div class="card-hint">Gerçekleşmiş nakit</div>
                                </template>
                            </div>

                            <!-- 3. Gerçek Özkaynak -->
                            <div class="card card-static">
                                <div class="card-head">
                                    <h3 class="card-title">Gerçek Özkaynak</h3>
                                </div>
                                <div class="card-value">
                                    <AnimatedNumber
                                        v-if="summary"
                                        :value="summary.trueEquity"
                                        :decimals="2"
                                        prefix="$"
                                        :duration-ms="800" />
                                    <span v-else>—</span>
                                </div>
                                <div class="card-hint" v-if="summary">
                                    Açık pozisyon {{ fmt.money(summary.openPositionsValue) }}
                                </div>
                            </div>

                            <!-- 4. İşlem Sayısı -->
                            <div class="card card-static">
                                <div class="card-head">
                                    <h3 class="card-title">İşlem Sayısı</h3>
                                </div>
                                <div class="card-value">{{ summary ? fmt.int(summary.closedTradeCount) : '—' }}</div>
                                <div class="card-hint">
                                    <span class="metric-good">{{ summary?.winningTrades ?? 0 }} kazanan</span>
                                    · <span class="metric-bad">{{ summary?.losingTrades ?? 0 }} kaybeden</span>
                                </div>
                            </div>

                            <!-- 5. Kazanma Oranı -->
                            <div class="card card-static">
                                <div class="card-head">
                                    <h3 class="card-title">Kazanma Oranı</h3>
                                </div>
                                <div class="card-value" :class="winRateClass">
                                    {{ summary ? fmt.pct(summary.winRate) : '—' }}
                                </div>
                                <div class="card-hint">Tüm kapalı işlemler</div>
                            </div>

                            <!-- 6. Ödenen Komisyon -->
                            <div class="card card-static">
                                <div class="card-head">
                                    <h3 class="card-title">Ödenen Komisyon</h3>
                                </div>
                                <div class="card-value metric-warn">
                                    {{ summary ? fmt.money(summary.totalCommissionPaid) : '—' }}
                                </div>
                                <div class="card-hint">0.10% taker — paper</div>
                            </div>

                            <!-- 7. Açık Pozisyon -->
                            <div class="card card-static">
                                <div class="card-head">
                                    <h3 class="card-title">Açık Pozisyon</h3>
                                </div>
                                <div class="card-value">{{ summary ? fmt.int(summary.openPositionCount) : '—' }}</div>
                                <div class="card-hint">
                                    MTM {{ summary ? fmt.money(summary.openPositionsValue) : '—' }}
                                </div>
                            </div>
                        </div>

                        <!-- Kartopu mini chart — 7 kartın yanında -->
                        <SnowballChart
                            :equity-history="equityHistory"
                            :starting-balance="summary?.startingBalance ?? 100"
                            :current="summary?.trueEquity" />
                    </div>
                </section>

                <!-- SAAT-BAŞI İŞLEM SAYACI — hedef 150/sa -->
                <section class="block">
                    <h2 class="section-title">Saat-Başı İşlem Hacmi</h2>
                    <TradeCounter
                        :actual-count="tradesLastHour"
                        :target-count="hourlyTarget"
                        :total-closed="summary?.closedTradeCount ?? 0" />
                </section>

                <!-- SEMBOL CAROUSEL -->
                <section class="block">
                    <h2 class="section-title">
                        Canlı Piyasa
                        <span class="tools muted tiny">1 dakikalık bar · son 60 dk</span>
                    </h2>
                    <SymbolCarousel :symbols="symbols" :interval-ms="10000" :on-select="goKlines" />
                </section>

                <!-- CANLI TRADE FEED — son kapalı işlemler, üstten kayar -->
                <section class="block">
                    <h2 class="section-title">
                        Canlı İşlem Akışı
                        <a href="/positions.html" class="btn btn-ghost btn-sm">Tümünü gör →</a>
                    </h2>

                    <div v-if="closedPoll.error.value" class="alert">
                        Kapalı işlemler alınamadı.
                    </div>

                    <div v-else-if="!closedPositions" class="card-grid">
                        <div v-for="i in 3" :key="i" class="skeleton" style="height:92px; border-radius:12px"></div>
                    </div>

                    <div v-else-if="closedPositions.length === 0" class="empty-illust">
                        <svg width="120" height="88" viewBox="0 0 120 88" fill="none" xmlns="http://www.w3.org/2000/svg">
                            <defs>
                                <linearGradient id="emptyGrad" x1="0" y1="0" x2="1" y2="1">
                                    <stop offset="0%" stop-color="#6366f1" stop-opacity="0.4" />
                                    <stop offset="100%" stop-color="#06b6d4" stop-opacity="0.2" />
                                </linearGradient>
                            </defs>
                            <rect x="6" y="18" width="108" height="58" rx="10" fill="url(#emptyGrad)" opacity="0.35" />
                            <path d="M16 58 L36 42 L52 52 L74 28 L98 40" stroke="#818cf8" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" fill="none" />
                            <circle cx="36" cy="42" r="3.5" fill="#818cf8" />
                            <circle cx="52" cy="52" r="3.5" fill="#818cf8" />
                            <circle cx="74" cy="28" r="3.5" fill="#06b6d4" />
                            <circle cx="98" cy="40" r="3.5" fill="#06b6d4" />
                            <path d="M10 76 L110 76" stroke="#64748b" stroke-width="1" stroke-dasharray="3 3" opacity="0.4" />
                        </svg>
                        <div class="title">Henüz kapalı işlem yok</div>
                        <div class="sub">İlk pozisyon kapandığında burada kart olarak görünecek. Bot aktif olarak sinyal izliyor.</div>
                        <div class="hint-row">
                            <span class="pulse-dot"></span>
                            <span>Strateji motoru çalışıyor</span>
                        </div>
                    </div>

                    <LiveTradeFeed v-else :trades="recentClosed" />
                </section>

            </main>
        </div>
    `,
    setup() {
        const summaryPoll = usePolling(() => api.portfolio.summary(), 5000);
        const closedPoll  = usePolling(() => api.positions.list({ status: "Closed" }), 5000);

        const summary = computed(() => summaryPoll.data.value);
        const closedPositions = computed(() => {
            const d = closedPoll.data.value;
            return Array.isArray(d) ? d : [];
        });

        const recentClosed = computed(() => {
            const arr = closedPositions.value.slice();
            arr.sort((a, b) => {
                const ta = new Date(b.closedAt || b.updatedAt || 0).getTime();
                const tb = new Date(a.closedAt || a.updatedAt || 0).getTime();
                return ta - tb;
            });
            return arr.slice(0, 12);
        });

        // Saat-başı closed trade sayımı — son 60 dk içinde kapanan pozisyonlar.
        const tradesLastHour = computed(() => {
            const arr = closedPositions.value;
            if (!Array.isArray(arr) || arr.length === 0) return 0;
            const threshold = Date.now() - 60 * 60 * 1000;
            let n = 0;
            for (const p of arr) {
                const t = new Date(p.closedAt || p.updatedAt || 0).getTime();
                if (isFinite(t) && t >= threshold) n++;
            }
            return n;
        });

        // Equity history — in-memory snapshot (backend endpoint olmayabilir, polling üzerinden build).
        const equityHistory = ref([]);
        watch(
            () => summary.value?.trueEquity,
            (eq) => {
                if (typeof eq !== "number" || !isFinite(eq)) return;
                equityHistory.value.push({ ts: Date.now(), equity: eq });
                if (equityHistory.value.length > EQUITY_HISTORY_MAX) {
                    equityHistory.value = equityHistory.value.slice(-EQUITY_HISTORY_MAX);
                }
            },
            { immediate: true },
        );

        const winRateClass = computed(() => {
            const wr = summary.value?.winRate;
            if (wr == null) return "metric-neutral";
            if (wr >= 0.5) return "metric-good";
            if (wr >= 0.35) return "metric-warn";
            return "metric-bad";
        });

        // Cash negatif ise UI clamp — backend düzeltilene kadar yanıltıcı gösterimi engelle.
        const cashClamped = computed(() => {
            const c = summary.value?.currentCash;
            return typeof c === "number" && c < 0;
        });
        const cashTooltip = computed(() => {
            const s = summary.value;
            if (!s) return "";
            const locked = Math.max(0, Number(s.openPositionsValue || 0));
            const shortfall = Math.max(0, -(Number(s.currentCash || 0)));
            return `Açık pozisyonlarda ${fmt.money(locked)} bağlı · limit aşımı ${fmt.money(shortfall)}`;
        });

        function pnlClass(v) {
            return fmt.sign(v);
        }
        function pctBadge(v) {
            const n = Number(v);
            if (!isFinite(n) || n === 0) return "";
            return n > 0 ? "up" : "down";
        }
        function goKlines(sym) {
            window.location.href = `/klines.html?symbol=${encodeURIComponent(sym)}`;
        }

        return {
            summaryPoll, closedPoll,
            summary, closedPositions, recentClosed,
            tradesLastHour, hourlyTarget: HOURLY_TRADE_TARGET,
            equityHistory,
            winRateClass, pnlClass, pctBadge, goKlines,
            cashClamped, cashTooltip,
            symbols: SYMBOLS, tickerSymbols: TICKER_SYMBOLS, fmt,
        };
    },
};

createApp(App).mount("#app");
