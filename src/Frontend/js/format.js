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
    /** İşaretli para — örn. +$0.10 / -$0.45 */
    money: (v, decimals = 2) => {
        const n = safeNum(v);
        if (n === null) return "-";
        const f = decimals === 4 ? nf4 : nf2;
        return `$${f.format(Math.abs(n))}`;
    },
    moneySigned: (v, decimals = 2) => {
        const n = safeNum(v);
        if (n === null) return "-";
        const f = decimals === 4 ? nf4 : nf2;
        const sign = n > 0 ? "+" : n < 0 ? "-" : "";
        return `${sign}$${f.format(Math.abs(n))}`;
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
    price: (v) => {
        if (v == null) return "-";
        const n = Number(v);
        if (n >= 1000) return nf2.format(n);
        if (n >= 1) return nf4.format(n);
        return nf8.format(n);
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
};
