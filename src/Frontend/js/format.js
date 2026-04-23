const nf0 = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const nf2 = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const nf4 = new Intl.NumberFormat("en-US", { minimumFractionDigits: 4, maximumFractionDigits: 4 });
const nf8 = new Intl.NumberFormat("en-US", { minimumFractionDigits: 8, maximumFractionDigits: 8 });

function safeNum(v) {
    const n = Number(v);
    return isFinite(n) ? n : null;
}

export const fmt = {
    int: (v) => (v == null ? "-" : nf0.format(Number(v))),
    num2: (v) => (v == null ? "-" : nf2.format(Number(v))),
    num4: (v) => (v == null ? "-" : nf4.format(Number(v))),
    num8: (v) => (v == null ? "-" : nf8.format(Number(v))),
    pct: (v) => (v == null ? "-" : `${nf2.format(Number(v) * 100)}%`),
    pctRaw: (v) => (v == null ? "-" : `${nf2.format(Number(v))}%`),
    /** Default 4 küsürat para — örn. $0.1234 / $99.9113 (Loop 30: her yerde 4 basamak) */
    money: (v, decimals = 4) => {
        const n = safeNum(v);
        if (n === null) return "-";
        const f = decimals === 2 ? nf2 : decimals === 8 ? nf8 : nf4;
        return `$${f.format(Math.abs(n))}`;
    },
    moneySigned: (v, decimals = 4) => {
        const n = safeNum(v);
        if (n === null) return "-";
        const f = decimals === 2 ? nf2 : decimals === 8 ? nf8 : nf4;
        const sign = n > 0 ? "+" : n < 0 ? "-" : "";
        return `${sign}$${f.format(Math.abs(n))}`;
    },
    /** 4 basamak sade para — prefix YOK (örn. 0.0023 / 100.0000) */
    money4: (v) => {
        const n = safeNum(v);
        if (n === null) return "-";
        return nf4.format(Math.abs(n));
    },
    /** İşaretli yüzde — örn. +%0.10 / -%5.43 (raw input zaten yüzde değeri) */
    pctSigned: (v) => {
        const n = safeNum(v);
        if (n === null) return "-";
        const sign = n > 0 ? "+" : n < 0 ? "-" : "";
        return `${sign}%${nf2.format(Math.abs(n))}`;
    },
    /** Fraction (0.0010) -> +%0.10 */
    pctFracSigned: (v) => {
        const n = safeNum(v);
        if (n === null) return "-";
        const p = n * 100;
        const sign = p > 0 ? "+" : p < 0 ? "-" : "";
        return `${sign}%${nf2.format(Math.abs(p))}`;
    },
    /** Default fiyat gösterimi — her zaman 4 basamak (Loop 30 kuralı). */
    price: (v) => {
        if (v == null) return "-";
        const n = Number(v);
        if (!isFinite(n)) return "-";
        return nf4.format(n);
    },
    /** Eski magnitüd bazlı fiyat — sadece klines/orderbook çok küçük altcoin için kalıyor. */
    price4: (v) => {
        if (v == null) return "-";
        const n = Number(v);
        if (!isFinite(n)) return "-";
        return n.toFixed(4);
    },
    timeIso: (v) => {
        if (!v) return "-";
        try {
            return new Date(v).toLocaleString("tr-TR", {
                timeZone: "Europe/Istanbul",
                year: "numeric", month: "2-digit", day: "2-digit",
                hour: "2-digit", minute: "2-digit", second: "2-digit",
                hour12: false,
            }).replace(",", "");
        } catch { return String(v); }
    },
    timeHms: (v) => {
        if (!v) return "-";
        try {
            return new Date(v).toLocaleTimeString("tr-TR", {
                timeZone: "Europe/Istanbul",
                hour12: false,
            });
        } catch { return String(v); }
    },
    /** "10:18" şeklinde dk gösterim — Açıldı: 10:18:01 */
    timeHm: (v) => {
        if (!v) return "-";
        try {
            return new Date(v).toLocaleTimeString("tr-TR", {
                timeZone: "Europe/Istanbul",
                hour: "2-digit", minute: "2-digit", hour12: false,
            });
        } catch { return String(v); }
    },
    /** İki tarih arası süre — "4dk 28sn" / "12dk" / "2s 14dk" */
    duration: (fromIso, toIso) => {
        if (!fromIso) return "-";
        const from = new Date(fromIso).getTime();
        const to = toIso ? new Date(toIso).getTime() : Date.now();
        if (!isFinite(from) || !isFinite(to)) return "-";
        const sec = Math.max(0, Math.floor((to - from) / 1000));
        if (sec < 60) return `${sec}sn`;
        const min = Math.floor(sec / 60);
        const remSec = sec % 60;
        if (min < 60) return remSec === 0 ? `${min}dk` : `${min}dk ${remSec}sn`;
        const hr = Math.floor(min / 60);
        const remMin = min % 60;
        if (hr < 24) return `${hr}sa ${remMin}dk`;
        const day = Math.floor(hr / 24);
        return `${day}g ${hr % 24}sa`;
    },
    sign: (v) => {
        const n = Number(v);
        if (!isFinite(n) || n === 0) return "metric-neutral";
        return n > 0 ? "metric-good" : "metric-bad";
    },
    /** "BTCUSDT" -> "BTC" */
    baseAsset: (symbol) => (symbol ? String(symbol).replace(/USDT$/i, "") : ""),
    /** Kısa tarih — "19 Nis 14:23" */
    dateShort: (v) => {
        if (!v) return "-";
        try {
            return new Date(v).toLocaleString("tr-TR", {
                timeZone: "Europe/Istanbul",
                day: "2-digit", month: "short",
                hour: "2-digit", minute: "2-digit", hour12: false,
            });
        } catch { return String(v); }
    },
};
