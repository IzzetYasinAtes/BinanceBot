const baseUrl = window.location.origin;

async function request(path, { method = "GET", query, body, headers, signal } = {}) {
    const url = new URL(path, baseUrl);
    if (query) {
        for (const [k, v] of Object.entries(query)) {
            if (v === undefined || v === null || v === "") continue;
            url.searchParams.append(k, v);
        }
    }

    const init = {
        method,
        headers: { "Accept": "application/json" },
        signal,
    };
    if (body !== undefined) {
        init.headers["Content-Type"] = "application/json";
        init.body = JSON.stringify(body);
    }
    if (headers) {
        for (const [k, v] of Object.entries(headers)) {
            if (v === undefined || v === null) continue;
            init.headers[k] = v;
        }
    }

    let response;
    try {
        response = await fetch(url, init);
    } catch (e) {
        throw new ApiError(0, "Network error", e?.message ?? String(e));
    }

    const text = await response.text();
    const data = text ? safeJson(text) : null;

    if (!response.ok) {
        throw new ApiError(response.status, response.statusText,
            data?.errors ?? data?.detail ?? text);
    }

    return data;
}

function safeJson(text) {
    try { return JSON.parse(text); }
    catch { return text; }
}

export class ApiError extends Error {
    constructor(status, statusText, detail) {
        super(`${status} ${statusText}`);
        this.status = status;
        this.detail = detail;
    }
}

const ADMIN_KEY_STORAGE = "adminKey";
const DEFAULT_ADMIN_KEY_HINT = "dev-admin-key-change-me";

export function getAdminKey({ prompt: doPrompt = true, promptMessage } = {}) {
    let key = null;
    try { key = localStorage.getItem(ADMIN_KEY_STORAGE); } catch { /* ignore */ }
    if (key) return key;
    if (!doPrompt || typeof window === "undefined" || typeof window.prompt !== "function") return null;
    const message = promptMessage ?? "Admin key (ilk kez):";
    const entered = window.prompt(message, DEFAULT_ADMIN_KEY_HINT);
    if (!entered) return null;
    try { localStorage.setItem(ADMIN_KEY_STORAGE, entered); } catch { /* ignore */ }
    return entered;
}

export function clearAdminKey() {
    try { localStorage.removeItem(ADMIN_KEY_STORAGE); } catch { /* ignore */ }
}

export const api = {
    raw: request,

    health: () => request("/health/ready"),

    systemStatus: () => request("/api/system/status"),
    logsTail: (since, level, limit = 200) =>
        request("/api/logs/tail", { query: { since, level, limit } }),

    instruments: () => request("/api/instruments"),
    symbolFilters: (symbol) => request(`/api/instruments/${encodeURIComponent(symbol)}/filters`),

    klines: (symbol, interval = "1m", limit = 500) =>
        request("/api/klines", { query: { symbol, interval, limit } }),
    bookTicker: (symbol) => request("/api/ticker/book", { query: { symbol } }),
    depth: (symbol, depth = 20) =>
        request("/api/depth", { query: { symbol, depth } }),
    marketSummary: (symbols) =>
        request("/api/market/summary", { query: { symbols: symbols.join(",") } }),

    orders: {
        open: (symbol) => request("/api/orders/open", { query: { symbol } }),
        history: (q = {}) => request("/api/orders/history", { query: q }),
        byClientId: (id) => request(`/api/orders/${encodeURIComponent(id)}`),
    },

    balances: {
        list: () => request("/api/balances"),
        resetPaper: (startingBalance, adminKey) =>
            request("/api/papertrade/reset", {
                method: "POST",
                body: { startingBalance },
                headers: { "X-Admin-Key": adminKey },
            }),
    },

    positions: {
        list: (q = {}) => request("/api/positions/", { query: q }),
        pnlToday: () => request("/api/positions/pnl/today"),
        pnlFor: (symbol) => request(`/api/positions/${encodeURIComponent(symbol)}/pnl`),
    },

    portfolio: {
        // GET /api/portfolio/summary — Loop 19 yeni endpoint.
        // Backend henüz hazır olmayabilir; çağıran graceful fallback yapsın.
        summary: () => request("/api/portfolio/summary"),
    },

    strategies: {
        list: (status) => request("/api/strategies/", { query: { status } }),
        detail: (id) => request(`/api/strategies/${id}`),
        signals: (id, from, to) =>
            request(`/api/strategies/${id}/signals`, { query: { from, to } }),
        latestSignals: (limit = 12) =>
            request("/api/strategies/signals/latest", { query: { limit } }),
    },

    risk: {
        profile: () => request("/api/risk/profile"),
        circuitBreaker: () => request("/api/risk/circuit-breaker"),
        drawdownHistory: (days = 30) =>
            request("/api/risk/drawdown-history", { query: { days } }),
    },

    backtests: {
        list: (q = {}) => request("/api/backtests/", { query: q }),
        result: (id) => request(`/api/backtests/${id}`),
    },
};
