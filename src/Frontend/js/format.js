const nf0 = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const nf2 = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const nf4 = new Intl.NumberFormat("en-US", { minimumFractionDigits: 4, maximumFractionDigits: 4 });
const nf8 = new Intl.NumberFormat("en-US", { minimumFractionDigits: 8, maximumFractionDigits: 8 });

export const fmt = {
    int: (v) => (v == null ? "-" : nf0.format(Number(v))),
    num2: (v) => (v == null ? "-" : nf2.format(Number(v))),
    num4: (v) => (v == null ? "-" : nf4.format(Number(v))),
    num8: (v) => (v == null ? "-" : nf8.format(Number(v))),
    pct: (v) => (v == null ? "-" : `${nf2.format(Number(v) * 100)}%`),
    pctRaw: (v) => (v == null ? "-" : `${nf2.format(Number(v))}%`),
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
            return new Date(v).toISOString().replace("T", " ").substring(0, 19);
        } catch { return String(v); }
    },
    timeHms: (v) => {
        if (!v) return "-";
        try {
            const d = new Date(v);
            return d.toISOString().substring(11, 19);
        } catch { return String(v); }
    },
    sign: (v) => {
        const n = Number(v);
        if (!isFinite(n) || n === 0) return "metric-neutral";
        return n > 0 ? "metric-good" : "metric-bad";
    },
};
