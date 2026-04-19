import { readdirSync, statSync, existsSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const cacheDir = "C:/Users/iyasi/AppData/Local/npm-cache/_npx";
const dirs = readdirSync(cacheDir)
    .map(d => join(cacheDir, d, "node_modules", "playwright"))
    .filter(p => { try { return statSync(p).isDirectory(); } catch { return false; } });
const { chromium } = await import(pathToFileURL(join(dirs[0], "index.mjs")).href);

const pw = "C:/Users/iyasi/AppData/Local/ms-playwright";
const exe = existsSync(`${pw}/chromium-1208/chrome-win64/chrome.exe`)
    ? `${pw}/chromium-1208/chrome-win64/chrome.exe`
    : undefined;

const browser = await chromium.launch({ headless: true, executablePath: exe });
const page = await browser.newPage({ viewport: { width: 1600, height: 500 } });
await page.goto("http://localhost:5188/index.html", { waitUntil: "networkidle", timeout: 20000 });
await page.waitForTimeout(2500);
await page.screenshot({
    path: "D:/repos/BinanceBot/loops/loop_21/screenshots/hero-closeup.png",
    clip: { x: 240, y: 0, width: 1360, height: 500 },
});
await browser.close();
console.log("done");
