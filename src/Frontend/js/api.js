const baseUrl = window.location.origin;

async function request(path, { method = "GET", query, body, signal } = {}) {
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

    positions: {
        list: (q = {}) => request("/api/positions/", { query: q }),
        pnlToday: () => request("/api/positions/pnl/today"),
        pnlFor: (symbol) => request(`/api/positions/${encodeURIComponent(symbol)}/pnl`),
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
