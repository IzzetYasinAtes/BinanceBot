// Ana Panel — Hero KPI + sembol carousel + son işlemler + hızlı metrikler.
// Polling: portfolio/summary + positions + strategies/signals/latest.

import { createApp, computed } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";
import { SymbolCarousel } from "../components/symbolCarousel.js";

const SYMBOLS = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT"];

const App = {
    components: { Sidebar, ErrorBanner, SymbolCarousel },
    template: `
        <div class="app">
            <Sidebar active="dashboard" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Ana Panel</h1>
                    <p class="page-sub">Anlık portföy durumu, sembol hareketleri ve son işlem sonuçları.</p>
                </div>

                <ErrorBanner :error="summaryPoll.error.value" />

                <!-- HERO: toplam net K/Z -->
                <section class="hero fade-in" v-if="summary">
                    <div class="hero-grid">
                        <div>
                            <div class="hero-label">Toplam Net Kâr / Zarar</div>
                            <div class="hero-value" :class="pnlClass(summary.netPnl)">
                                {{ fmt.moneySigned(summary.netPnl) }}
                            </div>
                            <div class="hero-sub">
                                <span class="badge" :class="pctBadge(summary.netPnl)">
                                    {{ fmt.pctFracSigned(summary.netPnlPct) }}
                                </span>
                                <span>başlangıç bakiyesi {{ fmt.money(summary.startingBalance) }}</span>
                            </div>
                        </div>

                        <div class="hero-side">
                            <div class="hero-label">Mevcut Bakiye</div>
                            <div class="val">{{ fmt.money(summary.currentCash) }}</div>
                            <div class="muted small mt-2">Gerçekleşmiş (nakit)</div>
                        </div>

                        <div class="hero-side">
                            <div class="hero-label">Gerçek Özkaynak</div>
                            <div class="val">{{ fmt.money(summary.trueEquity) }}</div>
                            <div class="muted small mt-2">
                                Açık pozisyon değeri {{ fmt.money(summary.openPositionsValue) }}
                            </div>
                        </div>
                    </div>
                </section>
                <section class="hero" v-else>
                    <div class="skeleton" style="height:120px"></div>
                </section>

                <!-- HIZLI METRİKLER (4 lü kart) -->
                <section class="block">
                    <h2 class="section-title">Hızlı Metrikler</h2>
                    <div class="kpi-grid">
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

                        <div class="card card-static">
                            <div class="card-head">
                                <h3 class="card-title">Kazanma Oranı</h3>
                            </div>
                            <div class="card-value" :class="winRateClass">
                                {{ summary ? fmt.pct(summary.winRate) : '—' }}
                            </div>
                            <div class="card-hint">Tüm kapalı işlemler üzerinden</div>
                        </div>

                        <div class="card card-static">
                            <div class="card-head">
                                <h3 class="card-title">Ödenen Komisyon</h3>
                            </div>
                            <div class="card-value metric-warn">
                                {{ summary ? fmt.money(summary.totalCommissionPaid) : '—' }}
                            </div>
                            <div class="card-hint">0.10% taker — paper modeli</div>
                        </div>

                        <div class="card card-static">
                            <div class="card-head">
                                <h3 class="card-title">Açık Pozisyon</h3>
                            </div>
                            <div class="card-value">{{ summary ? fmt.int(summary.openPositionCount) : '—' }}</div>
                            <div class="card-hint">Canlı MTM değeri yukarıda</div>
                        </div>
                    </div>
                </section>

                <!-- SEMBOL CAROUSEL -->
                <section class="block">
                    <h2 class="section-title">
                        Canlı Piyasa
                        <span class="tools muted tiny">1 dakikalık bar · son 60 dk</span>
                    </h2>
                    <SymbolCarousel :symbols="symbols" :interval-ms="10000" :on-select="goKlines" />
                </section>

                <!-- SON İŞLEMLER (kart grid) -->
                <section class="block">
                    <h2 class="section-title">
                        Son İşlemler
                        <a href="/positions.html" class="btn btn-ghost btn-sm">Tümünü gör →</a>
                    </h2>

                    <div v-if="closedPoll.error.value" class="alert">
                        Kapalı işlemler alınamadı.
                    </div>

                    <div v-else-if="!closedPositions" class="card-grid">
                        <div v-for="i in 3" :key="i" class="skeleton" style="height:180px; border-radius:16px"></div>
                    </div>

                    <div v-else-if="closedPositions.length === 0" class="empty-state">
                        <span class="emoji">∅</span>
                        Henüz kapalı işlem yok. İlk pozisyon kapandığında burada görünecek.
                    </div>

                    <div v-else class="card-grid">
                        <div v-for="p in recentClosed" :key="p.id" class="trade-card fade-in">
                            <div class="t-head">
                                <div class="trade-sym">
                                    <span class="sym-dot">{{ fmt.baseAsset(p.symbol).slice(0,3) }}</span>
                                    <span>{{ p.symbol }}</span>
                                </div>
                                <span class="badge" :class="p.side === 'Long' ? 'up' : 'down'">
                                    {{ p.side === 'Long' ? 'LONG' : 'SHORT' }}
                                </span>
                            </div>
                            <div class="t-body">
                                <div class="kv">
                                    <div class="k">Miktar</div>
                                    <div class="v">{{ fmt.num4(p.quantity) }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Giriş</div>
                                    <div class="v">{{ fmt.price(p.averageEntryPrice) }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Çıkış</div>
                                    <div class="v">{{ p.exitPrice ? fmt.price(p.exitPrice) : '—' }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Süre</div>
                                    <div class="v">{{ fmt.duration(p.openedAt, p.closedAt) }}</div>
                                </div>
                            </div>
                            <div class="t-foot">
                                <div class="kv">
                                    <div class="k">Net K/Z</div>
                                    <div class="pnl-big" :class="pnlClass(p.realizedPnl)">
                                        {{ fmt.moneySigned(p.realizedPnl) }}
                                    </div>
                                </div>
                                <div class="col" style="align-items:flex-end; gap:2px;">
                                    <span class="badge closed">KAPALI</span>
                                    <span class="muted tiny">{{ fmt.dateShort(p.closedAt) }}</span>
                                </div>
                            </div>
                        </div>
                    </div>
                </section>

            </main>
        </div>
    `,
    setup() {
        const summaryPoll = usePolling(() => api.portfolio.summary(), 5000);
        const closedPoll  = usePolling(() => api.positions.list({ status: "Closed" }), 15000);

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
            return arr.slice(0, 6);
        });

        const winRateClass = computed(() => {
            const wr = summary.value?.winRate;
            if (wr == null) return "metric-neutral";
            if (wr >= 0.5) return "metric-good";
            if (wr >= 0.35) return "metric-warn";
            return "metric-bad";
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
            winRateClass, pnlClass, pctBadge, goKlines,
            symbols: SYMBOLS, fmt,
        };
    },
};

createApp(App).mount("#app");
