// Emir Geçmişi — satır-satır tablo + status filtre (Loop 30).

import { createApp, ref, computed } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";
import { SymbolLogo } from "../components/symbolLogo.js";

const STATUS_FILTERS = [
    { id: "all",       label: "Tümü" },
    { id: "filled",    label: "Gerçekleşen" },
    { id: "new",       label: "Bekleyen" },
    { id: "cancelled", label: "İptal" },
    { id: "rejected",  label: "Reddedilen" },
];

const App = {
    components: { Sidebar, ErrorBanner, SymbolLogo },
    template: `
        <div class="app">
            <Sidebar active="orders" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Emir Geçmişi</h1>
                    <p class="page-sub">Bot tarafından gönderilen tüm emirler — satır satır tablo, miktar, fiyat ve durum.</p>
                </div>

                <ErrorBanner :error="poll.error.value" />

                <div class="block">
                    <div class="chip-group">
                        <button v-for="f in filters" :key="f.id"
                                class="chip" :class="{ active: active === f.id }"
                                @click="active = f.id">
                            {{ f.label }}
                            <span class="muted tiny">({{ countByFilter(f.id) }})</span>
                        </button>
                    </div>
                </div>

                <div v-if="!rows" class="skeleton" style="height:320px; border-radius:14px"></div>

                <div v-else-if="visible.length === 0" class="empty-state">
                    <span class="emoji">∅</span>
                    Bu filtre için emir yok.
                </div>

                <div v-else class="data-table-wrap">
                    <table class="data-table">
                        <thead>
                            <tr>
                                <th>ClientOrderId</th>
                                <th>Sembol</th>
                                <th>Yön</th>
                                <th>Tip</th>
                                <th class="num">Miktar</th>
                                <th class="num">Ortalama Fiyat</th>
                                <th class="num">Notional</th>
                                <th class="num">Komisyon</th>
                                <th>Durum</th>
                                <th class="num">Zaman</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="o in visible" :key="o.clientOrderId">
                                <td class="mono">{{ shortId(o.clientOrderId) }}</td>
                                <td class="sym">
                                    <span class="row-sym">
                                        <SymbolLogo :symbol="o.symbol" :size="16" />
                                        {{ o.symbol }}
                                    </span>
                                </td>
                                <td>
                                    <span class="badge" :class="sideClass(o)">
                                        {{ o.side === 'Buy' ? 'ALIŞ' : 'SATIŞ' }}
                                    </span>
                                </td>
                                <td>{{ o.type }}<span class="muted tiny"> · {{ o.timeInForce }}</span></td>
                                <td class="num">{{ fmt.num4(o.quantity) }}</td>
                                <td class="num">{{ avgPrice(o) }}</td>
                                <td class="num">{{ fmt.money(o.cumulativeQuoteQty) }}</td>
                                <td class="num">{{ commissionLabel(o) }}</td>
                                <td>
                                    <span class="badge" :class="statusBadge(o.status)">
                                        {{ statusLabel(o.status) }}
                                    </span>
                                </td>
                                <td class="num">{{ fmt.dateShort(o.createdAt) }}</td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </main>
        </div>
    `,
    setup() {
        const active = ref("all");
        const poll = usePolling(() => api.orders.history({ take: 100 }), 10000);

        const rows = computed(() => {
            const d = poll.data.value;
            if (!d) return null;
            if (Array.isArray(d)) return d;
            if (d.items && Array.isArray(d.items)) return d.items;
            return [];
        });

        function matches(o, f) {
            if (f === "all") return true;
            const s = String(o.status || "").toLowerCase();
            return s.includes(f);
        }

        const visible = computed(() => {
            if (!rows.value) return [];
            return rows.value.filter(o => matches(o, active.value));
        });

        function countByFilter(f) {
            if (!rows.value) return 0;
            return rows.value.filter(o => matches(o, f)).length;
        }

        function sideClass(o) {
            return o.side === "Buy" ? "up" : "down";
        }

        function statusLabel(s) {
            const map = {
                Filled: "GERÇEKLEŞTİ",
                PartiallyFilled: "KISMİ",
                New: "BEKLİYOR",
                Cancelled: "İPTAL",
                Canceled: "İPTAL",
                Rejected: "RED",
                Expired: "ZAMANAŞIMI",
            };
            return map[s] || s;
        }

        function statusBadge(s) {
            const k = String(s || "").toLowerCase();
            if (k === "filled")       return "good";
            if (k === "partiallyfilled") return "warn";
            if (k === "new")          return "info";
            if (k === "cancelled" || k === "canceled") return "closed";
            if (k === "rejected" || k === "expired") return "bad";
            return "";
        }

        function avgPrice(o) {
            const q = Number(o.executedQuantity || 0);
            const nq = Number(o.cumulativeQuoteQty || 0);
            if (q <= 0 || nq <= 0) return "—";
            return fmt.price(nq / q);
        }

        function commissionLabel(o) {
            const c = o.commission ?? o.commissionPaid ?? 0;
            const n = Number(c);
            if (!isFinite(n) || n === 0) return "—";
            return fmt.money(n);
        }

        function shortId(id) {
            if (!id) return "—";
            const s = String(id);
            return s.length > 14 ? s.slice(0, 14) + "…" : s;
        }

        return {
            active, filters: STATUS_FILTERS, poll, rows, visible,
            countByFilter, sideClass, statusLabel, statusBadge, avgPrice,
            commissionLabel, shortId, fmt,
        };
    },
};

createApp(App).mount("#app");
