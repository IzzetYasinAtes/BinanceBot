import { ref, computed, onBeforeUnmount } from "vue";
import { api, ApiError } from "./api.js";

const NAV_ITEMS = [
    { id: "dashboard",  href: "/index.html",      label: "Ana Panel",        ic: "\u25A3" },
    { id: "positions",  href: "/positions.html",  label: "Pozisyonlar",      ic: "\u25A0" },
    { id: "orders",     href: "/orders.html",     label: "Emir Geçmişi",     ic: "\u2630" },
    { id: "strategies", href: "/strategies.html", label: "Stratejiler",      ic: "\u25B2" },
    { id: "risk",       href: "/risk.html",       label: "Risk",             ic: "\u25CF" },
    { id: "klines",     href: "/klines.html",     label: "Mum Grafikleri",   ic: "\u25EB" },
    { id: "orderbook",  href: "/orderbook.html",  label: "Emir Defteri",     ic: "\u25A4" },
    { id: "logs",       href: "/logs.html",       label: "Sistem Olayları",  ic: "\u2630" },
];

export const Sidebar = {
    template: `
        <aside class="sidebar">
            <div class="brand">BinanceBot</div>
            <nav class="side-nav">
                <a v-for="i in items" :key="i.id" :href="i.href" :class="{ active: active === i.id }">
                    <span class="ic">{{ i.ic }}</span>
                    <span>{{ i.label }}</span>
                </a>
            </nav>
            <div class="sidebar-footer">
                <span class="testnet-pill" v-if="testnetOnly">TESTNET</span>
                <div class="status-row">
                    <span class="status-dot" :class="statusClass"></span>
                    <span>WS: {{ wsState }}</span>
                </div>
                <div class="status-row" style="color:var(--fg-3);">drift {{ drift }}ms</div>
            </div>
        </aside>
    `,
    props: {
        active: { type: String, default: "" },
    },
    setup() {
        const testnetOnly = ref(true);
        const wsState = ref("?");
        const drift = ref(0);
        const statusClass = ref("warn");

        async function poll() {
            try {
                const s = await api.systemStatus();
                testnetOnly.value = s.testnetOnly;
                wsState.value = s.wsState;
                drift.value = s.clockDriftMs;
                statusClass.value = s.wsState === "Streaming" ? "good"
                    : s.wsState === "Reconnecting" ? "warn" : "bad";
            } catch {
                statusClass.value = "bad";
                wsState.value = "offline";
            }
        }

        poll();
        const t = setInterval(poll, 5000);
        onBeforeUnmount(() => clearInterval(t));

        return { items: NAV_ITEMS, testnetOnly, wsState, drift, statusClass };
    },
};

export const ErrorBanner = {
    template: `<div v-if="error" class="alert">{{ message }}</div>`,
    props: {
        error: { type: Object, default: null },
    },
    setup(props) {
        const message = computed(() => {
            const e = props.error;
            if (!e) return "";
            if (e instanceof ApiError) {
                const detail = typeof e.detail === "string"
                    ? e.detail
                    : JSON.stringify(e.detail);
                return `${e.status} ${e.message} — ${detail}`;
            }
            return e.message || String(e);
        });
        return { message };
    },
};

export const Skeleton = {
    template: `<div class="skeleton" :style="{ height: height + 'px' }"></div>`,
    props: { height: { type: Number, default: 20 } },
};

export function usePolling(fn, intervalMs) {
    const data = ref(null);
    const loading = ref(false);
    const error = ref(null);
    let timer = null;
    let stopped = false;

    async function tick() {
        if (stopped) return;
        loading.value = data.value === null;
        try {
            const result = await fn();
            if (stopped) return;
            data.value = result;
            error.value = null;
        } catch (e) {
            if (stopped) return;
            error.value = e;
        } finally {
            loading.value = false;
        }
    }

    function start() {
        stopped = false;
        tick();
        timer = setInterval(tick, intervalMs);
    }

    function stop() {
        stopped = true;
        if (timer) clearInterval(timer);
        timer = null;
    }

    start();
    onBeforeUnmount(stop);

    return { data, loading, error, refresh: tick, stop };
}
