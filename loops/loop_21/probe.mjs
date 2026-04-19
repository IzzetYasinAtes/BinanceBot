import { readdirSync, statSync, existsSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const cacheDir = "C:/Users/iyasi/AppData/Local/npm-cache/_npx";
const dirs = readdirSync(cacheDir)
    .map(d => join(cacheDir, d, "node_modules", "playwright"))
    .filter(p => { try { return statSync(p).isDirectory(); } catch { return false; } });
const { chromium } = await import(pathToFileURL(join(dirs[0], "index.mjs")).href);

const pw = "C:/Users/iyasi/AppData/Local/ms-playwright";
const candidates = [
    `${pw}/chromium-1208/chrome-win64/chrome.exe`,
    `${pw}/chromium-1208/chrome-win/chrome.exe`,
    `${pw}/chromium-1134/chrome-win64/chrome.exe`,
    `${pw}/chromium-1134/chrome-win/chrome.exe`,
];
let exe;
for (const c of candidates) { if (existsSync(c)) { exe = c; break; } }

const browser = await chromium.launch({ headless: true, executablePath: exe });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });

page.on("response", async (r) => {
    const u = r.url();
    if (r.status() >= 400) {
        console.log(`[HTTP ${r.status()}] ${u}`);
    }
});
page.on("request", (r) => {
    const u = r.url();
    console.log(`[REQ] ${u}`);
});
page.on("requestfailed", (req) => {
    console.log(`[NETFAIL] ${req.url()} ${req.failure()?.errorText || ""}`);
});
page.on("console", (m) => {
    if (m.type() === "error" || m.type() === "warning") {
        console.log(`[${m.type().toUpperCase()}] ${m.text()}`);
    }
});
page.on("pageerror", (e) => console.log(`[PAGEERROR] ${e.message}`));

await page.goto("http://localhost:5188/index.html", { waitUntil: "networkidle", timeout: 20000 });
await page.waitForTimeout(2500);

const hero = await page.textContent(".hero").catch(() => null);
console.log("\nHERO:", hero?.slice(0, 200));

const ticker = await page.locator(".price-ticker-item").count().catch(() => 0);
console.log("Ticker items:", ticker);

await browser.close();
