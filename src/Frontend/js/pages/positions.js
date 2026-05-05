// Pozisyonlar — Açık & Kapalı tab + satır-satır tablo (Loop 30).

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
                    <p class="page-sub">Açık pozisyonların canlı kâr/zararı ve kapalı işlemlerin detaylı kayıtları — satır satır tablo.</p>
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
                    <div v-if="!openList" class="skeleton" style="height:280px; border-radius:14px"></div>
                    <div v-else-if="openList.length === 0" class="empty-state">
                        <span class="emoji">◎</span>
                        Açık pozisyon bulunmuyor. Sinyal geldiğinde burada görünecek.
                    </div>
                    <div v-else class="data-table-wrap">
                        <table class="data-table">
                            <thead>
                                <tr>
                                    <th>Sembol</th>
                                    <th>Yön</th>
                                    <th class="num">Miktar</th>
                                    <th class="num">Giriş</th>
                                    <th class="num">Giriş Tutarı ($)</th>
                                    <th class="num">Mark</th>
                                    <th class="num">PnL</th>
                                    <th class="num">PnL %</th>
                                    <th class="num">TP</th>
                                    <th class="num">SL</th>
                                    <th class="num">Süre</th>
                                    <th>Durum</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="p in openList" :key="p.id">
                                    <td class="sym">
                                        <span class="row-sym">
                                            <SymbolLogo :symbol="p.symbol" :size="16" />
                                            {{ p.symbol }}
                                        </span>
                                    </td>
                                    <td>
                                        <span class="badge" :class="p.direction === 'Long' ? 'up' : 'down'">
                                            {{ p.direction === 'Long' ? 'LONG' : 'SHORT' }}
                                        </span>
                                    </td>
                                    <td class="num">{{ fmt.num4(p.quantity) }}</td>
                                    <td class="num">{{ fmt.price(p.averageEntryPrice) }}</td>
                                    <td class="num">{{ fmt.money(entryNotional(p)) }}</td>
                                    <td class="num">{{ fmt.price(p.markPrice) }}</td>
                                    <td class="num" :class="fmt.sign(p.unrealizedPnl)">
                                        {{ fmt.moneySigned(p.unrealizedPnl) }}
                                    </td>
                                    <td class="num" :class="fmt.sign(p.unrealizedPnl)">
                                        {{ pnlPctLabel(p) }}
                                    </td>
                                    <td class="num">{{ p.takeProfit ? fmt.price(p.takeProfit) : '—' }}</td>
                                    <td class="num">{{ p.stopPrice ? fmt.price(p.stopPrice) : '—' }}</td>
                                    <td class="num">{{ fmt.duration(p.openedAt) }}</td>
                                    <td>
                                        <span class="badge open">AKTİF</span>
                                    </td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </section>

                <!-- KAPALI İŞLEMLER -->
                <section class="block" v-if="tab === 'closed'">
                    <div v-if="!closedList" class="skeleton" style="height:320px; border-radius:14px"></div>
                    <div v-else-if="closedList.length === 0" class="empty-state">
                        <span class="emoji">∅</span>
                        Kapalı işlem yok.
                    </div>
                    <div v-else class="data-table-wrap">
                        <table class="data-table">
                            <thead>
                                <tr>
                                    <th>Sembol</th>
                                    <th>Yön</th>
                                    <th class="num">Miktar</th>
                                    <th class="num">Giriş</th>
                                    <th class="num">Giriş Tutarı ($)</th>
                                    <th class="num">Çıkış</th>
                                    <th class="num">Çıkış Tutarı ($)</th>
                                    <th class="num">Net PnL</th>
                                    <th class="num">PnL %</th>
                                    <th class="num">Komisyon</th>
                                    <th class="num">Süre</th>
                                    <th class="num">Kapanma</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="p in closedListSorted" :key="p.id">
                                    <td class="sym">
                                        <span class="row-sym">
                                            <SymbolLogo :symbol="p.symbol" :size="16" />
                                            {{ p.symbol }}
                                        </span>
                                    </td>
                                    <td>
                                        <span class="badge" :class="p.direction === 'Long' ? 'up' : 'down'">
                                            {{ p.direction === 'Long' ? 'LONG' : 'SHORT' }}
                                        </span>
                                    </td>
                                    <td class="num">{{ fmt.num4(p.quantity) }}</td>
                                    <td class="num">{{ fmt.price(p.averageEntryPrice) }}</td>
                                    <td class="num">{{ fmt.money(entryNotional(p)) }}</td>
                                    <td class="num">{{ p.exitPrice ? fmt.price(p.exitPrice) : '—' }}</td>
                                    <td class="num">{{ p.exitPrice ? fmt.money(exitNotional(p)) : '—' }}</td>
                                    <td class="num" :class="fmt.sign(p.realizedPnl)">
                                        {{ fmt.moneySigned(p.realizedPnl) }}
                                    </td>
                                    <td class="num" :class="fmt.sign(p.realizedPnl)">
                                        {{ closedPnlPct(p) }}
                                    </td>
                                    <td class="num">{{ fmt.money(commissionTotal(p)) }}</td>
                                    <td class="num">{{ fmt.duration(p.openedAt, p.closedAt) }}</td>
                                    <td class="num">{{ fmt.dateShort(p.closedAt) }}</td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
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

        const closedListSorted = computed(() => {
            if (!closedList.value) return [];
            const arr = closedList.value.slice();
            arr.sort((a, b) =>
                new Date(b.closedAt || b.updatedAt || 0) - new Date(a.closedAt || a.updatedAt || 0));
            return arr;
        });

        function costBasis(p) {
            return Number(p.averageEntryPrice || 0) * Number(p.quantity || 0);
        }

        function entryNotional(p) {
            return Number(p.averageEntryPrice || 0) * Number(p.quantity || 0);
        }

        function exitNotional(p) {
            return Number(p.exitPrice || 0) * Number(p.quantity || 0);
        }

        // Loop 106 — backend artık totalCommission (entry+exit) döndürüyor.
        // Geri uyumluluk: eski cache'lenmiş response'larda field'lar olmayabilir,
        // entry+exit toplamı veya tek field'dan fallback.
        function commissionTotal(p) {
            const total = Number(p.totalCommission ?? 0);
            if (total > 0) return total;
            const entry = Number(p.entryCommission ?? 0);
            const exit = Number(p.exitCommission ?? 0);
            if (entry > 0 || exit > 0) return entry + exit;
            return Number(p.commissionPaid ?? p.commission ?? 0);
        }

        function pnlPctLabel(p) {
            const cb = costBasis(p);
            if (!cb) return "—";
            const frac = Number(p.unrealizedPnl || 0) / cb;
            return fmt.pctFracSigned(frac);
        }

        function closedPnlPct(p) {
            const cb = costBasis(p);
            if (!cb) return "—";
            const frac = Number(p.realizedPnl || 0) / cb;
            return fmt.pctFracSigned(frac);
        }

        return {
            tab, openPoll, closedPoll, openList, closedList, closedListSorted,
            costBasis, entryNotional, exitNotional, commissionTotal,
            pnlPctLabel, closedPnlPct, fmt,
        };
    },
};

createApp(App).mount("#app");
