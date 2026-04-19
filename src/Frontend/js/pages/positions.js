// Pozisyonlar — Açık & Kapalı tab + trade card grid.

import { createApp, ref, computed } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";
import { SymbolLogo } from "../components/symbolLogo.js";

const App = {
    components: { Sidebar, ErrorBanner, SymbolLogo },
    template: `
        <div class="app">
            <Sidebar active="positions" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Pozisyonlar</h1>
                    <p class="page-sub">Açık pozisyonların canlı kâr/zararı ve kapalı işlemlerin detaylı kayıtları.</p>
                </div>

                <ErrorBanner :error="openPoll.error.value" />

                <div class="block">
                    <div class="chip-group">
                        <button class="chip" :class="{ active: tab === 'open' }" @click="tab = 'open'">
                            Açık Pozisyonlar
                            <span class="muted tiny" v-if="openList">({{ openList.length }})</span>
                        </button>
                        <button class="chip" :class="{ active: tab === 'closed' }" @click="tab = 'closed'">
                            Kapalı İşlemler
                            <span class="muted tiny" v-if="closedList">({{ closedList.length }})</span>
                        </button>
                    </div>
                </div>

                <!-- AÇIK POZİSYONLAR -->
                <section class="block" v-if="tab === 'open'">
                    <div v-if="!openList" class="card-grid">
                        <div v-for="i in 3" :key="i" class="skeleton" style="height:220px; border-radius:16px"></div>
                    </div>
                    <div v-else-if="openList.length === 0" class="empty-state">
                        <span class="emoji">◎</span>
                        Açık pozisyon bulunmuyor. Sinyal geldiğinde burada görünecek.
                    </div>
                    <div v-else class="card-grid-2">
                        <div v-for="p in openList" :key="p.id" class="trade-card fade-in">
                            <div class="t-head">
                                <div class="trade-sym">
                                    <SymbolLogo :symbol="p.symbol" :size="28" />
                                    <span>{{ p.symbol }}</span>
                                </div>
                                <span class="badge" :class="p.side === 'Long' ? 'up' : 'down'">
                                    {{ p.side === 'Long' ? 'LONG' : 'SHORT' }}
                                </span>
                            </div>

                            <div class="kv" style="gap:4px;">
                                <div class="k">Canlı Kâr / Zarar</div>
                                <div class="pnl-big" :class="fmt.sign(p.unrealizedPnl)">
                                    {{ fmt.moneySigned(p.unrealizedPnl) }}
                                </div>
                                <div class="muted tiny">
                                    {{ pnlPctLabel(p) }} · piyasa fiyatı {{ fmt.price(p.markPrice) }}
                                </div>
                            </div>

                            <div class="t-body">
                                <div class="kv">
                                    <div class="k">Miktar</div>
                                    <div class="v">{{ fmt.num4(p.quantity) }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Giriş Fiyatı</div>
                                    <div class="v">{{ fmt.price(p.averageEntryPrice) }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Maliyet</div>
                                    <div class="v">{{ fmt.money(costBasis(p)) }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Tutulma Süresi</div>
                                    <div class="v">{{ fmt.duration(p.openedAt) }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Hedef Fiyat (TP)</div>
                                    <div class="v metric-good" v-if="p.takeProfit">
                                        {{ '$' + fmt.price(p.takeProfit) }}
                                    </div>
                                    <div class="v" v-else>—</div>
                                    <div class="muted tiny">{{ tpDistance(p) }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Stop-Loss (SL)</div>
                                    <div class="v metric-bad" v-if="p.stopPrice">
                                        {{ '$' + fmt.price(p.stopPrice) }}
                                    </div>
                                    <div class="v" v-else>—</div>
                                    <div class="muted tiny">{{ slDistance(p) }}</div>
                                </div>
                            </div>

                            <div class="t-foot">
                                <span class="badge open">AKTİF</span>
                                <span class="muted tiny">Açıldı: {{ fmt.dateShort(p.openedAt) }}</span>
                            </div>
                        </div>
                    </div>
                </section>

                <!-- KAPALI İŞLEMLER -->
                <section class="block" v-if="tab === 'closed'">
                    <div v-if="!closedList" class="card-grid">
                        <div v-for="i in 6" :key="i" class="skeleton" style="height:200px; border-radius:16px"></div>
                    </div>
                    <div v-else-if="closedList.length === 0" class="empty-state">
                        <span class="emoji">∅</span>
                        Kapalı işlem yok.
                    </div>
                    <template v-else>
                        <div v-for="g in closedGroups" :key="g.day" class="block">
                            <h3 class="section-title">
                                {{ g.day }}
                                <span class="tools small">
                                    <span class="muted">Net:</span>
                                    <span :class="fmt.sign(g.sum)" style="font-weight:700;">
                                        {{ fmt.moneySigned(g.sum) }}
                                    </span>
                                    <span class="muted">· {{ g.items.length }} işlem</span>
                                </span>
                            </h3>
                            <div class="card-grid">
                                <div v-for="p in g.items" :key="p.id" class="trade-card fade-in">
                                    <div class="t-head">
                                        <div class="trade-sym">
                                            <SymbolLogo :symbol="p.symbol" :size="28" />
                                            <span>{{ p.symbol }}</span>
                                        </div>
                                        <span class="badge" :class="p.side === 'Long' ? 'up' : 'down'">
                                            {{ p.side === 'Long' ? 'LONG' : 'SHORT' }}
                                        </span>
                                    </div>
                                    <div class="t-body">
                                        <div class="kv">
                                            <div class="k">Giriş</div>
                                            <div class="v">{{ fmt.price(p.averageEntryPrice) }}</div>
                                        </div>
                                        <div class="kv">
                                            <div class="k">Çıkış</div>
                                            <div class="v">{{ p.exitPrice ? fmt.price(p.exitPrice) : '—' }}</div>
                                        </div>
                                        <div class="kv">
                                            <div class="k">Miktar</div>
                                            <div class="v">{{ fmt.num4(p.quantity) }}</div>
                                        </div>
                                        <div class="kv">
                                            <div class="k">Süre</div>
                                            <div class="v">{{ fmt.duration(p.openedAt, p.closedAt) }}</div>
                                        </div>
                                    </div>
                                    <div class="t-foot">
                                        <div class="kv">
                                            <div class="k">Net K/Z</div>
                                            <div class="pnl-big" :class="fmt.sign(p.realizedPnl)">
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
                        </div>
                    </template>
                </section>
            </main>
        </div>
    `,
    setup() {
        const tab = ref("open");

        const openPoll   = usePolling(() => api.positions.list({ status: "Open" }), 4000);
        const closedPoll = usePolling(() => api.positions.list({ status: "Closed" }), 15000);

        const openList = computed(() => {
            const d = openPoll.data.value;
            return Array.isArray(d) ? d : null;
        });

        const closedList = computed(() => {
            const d = closedPoll.data.value;
            return Array.isArray(d) ? d : null;
        });

        const closedGroups = computed(() => {
            if (!closedList.value) return [];
            const byDay = new Map();
            for (const p of closedList.value) {
                const d = p.closedAt || p.updatedAt;
                if (!d) continue;
                const day = new Date(d).toLocaleDateString("tr-TR", {
                    timeZone: "Europe/Istanbul",
                    weekday: "long", day: "2-digit", month: "long",
                });
                if (!byDay.has(day)) byDay.set(day, []);
                byDay.get(day).push(p);
            }
            const arr = [...byDay.entries()].map(([day, items]) => {
                items.sort((a, b) =>
                    new Date(b.closedAt || b.updatedAt) - new Date(a.closedAt || a.updatedAt));
                const sum = items.reduce((s, p) => s + Number(p.realizedPnl || 0), 0);
                return { day, items, sum };
            });
            // gün sırasını son -> geçmiş yap
            arr.sort((a, b) =>
                new Date(b.items[0].closedAt || b.items[0].updatedAt)
                - new Date(a.items[0].closedAt || a.items[0].updatedAt));
            return arr;
        });

        function costBasis(p) {
            return Number(p.averageEntryPrice || 0) * Number(p.quantity || 0);
        }

        function pnlPctLabel(p) {
            const cb = costBasis(p);
            if (!cb) return "—";
            const frac = Number(p.unrealizedPnl || 0) / cb;
            return fmt.pctFracSigned(frac);
        }

        function tpDistance(p) {
            const mark = Number(p.markPrice || 0);
            const tp   = Number(p.takeProfit || 0);
            if (!mark || !tp) return "—";
            const diff = ((tp - mark) / mark) * 100;
            const sign = diff >= 0 ? "+" : "";
            return `${sign}${diff.toFixed(2)}% uzak`;
        }

        function slDistance(p) {
            const mark = Number(p.markPrice || 0);
            const sl   = Number(p.stopPrice || 0);
            if (!mark || !sl) return "—";
            // Mark'a göre SL'nin ne kadar altında/üstünde olduğu
            const diff = ((sl - mark) / mark) * 100;
            const sign = diff >= 0 ? "+" : "";
            return `${sign}${diff.toFixed(2)}% uzak`;
        }

        return {
            tab, openPoll, closedPoll, openList, closedList, closedGroups,
            costBasis, pnlPctLabel, tpDistance, slDistance, fmt,
        };
    },
};

createApp(App).mount("#app");
