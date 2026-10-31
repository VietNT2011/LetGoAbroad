import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";

// VI: Dùng npm CLI của chính npm run, không ghép shell command hoặc tải công cụ ngoài. EN: Reuse npm run's CLI without constructing shell commands or downloading audit helpers.
if (!process.env.npm_execpath) throw new Error("Run npm run audit:ci from web.");
const result = spawnSync(process.execPath, [process.env.npm_execpath, "audit", "--json"], {
  encoding: "utf8", timeout: 60_000, maxBuffer: 8 * 1024 * 1024,
});
if (result.error || ![0, 1].includes(result.status)) throw new Error("npm audit could not complete.", { cause: result.error });
const report = JSON.parse(result.stdout);
if (report.error || report.auditReportVersion !== 2 || !report.vulnerabilities || !report.metadata) throw new Error("Invalid npm audit report.");
mkdirSync("artifacts", { recursive: true });
writeFileSync("artifacts/npm-audit.json", JSON.stringify(report, null, 2) + "\n");
const lock = JSON.parse(readFileSync("package-lock.json", "utf8"));
// VI: Ngoại lệ duy nhất là ReDoS picomatch chưa có bản vá; kiểm cả graph và cờ dev. EN: Allow only the documented, currently unpatched picomatch ReDoS and its dev-only propagation graph.
const advisory = "https://github.com/advisories/GHSA-fc5q-39xc-vjpf";
const affected = new Set(["picomatch", "fdir", "tinyglobby", "vite", "vitest"]);
const unexpected = Object.entries(report.vulnerabilities).filter(([name, value]) =>
  !affected.has(name) || value.severity === "critical" ||
  !Array.isArray(value.nodes) || value.nodes.length === 0 ||
  !value.nodes.every((node) => lock.packages[node]?.dev === true) ||
  !Array.isArray(value.via) || value.via.length === 0 ||
  !value.via.every((via) => typeof via === "string" ? affected.has(via) : via.url === advisory),
);
if (unexpected.length) {
  console.error("Dependency audit rejected:", unexpected.map(([name]) => name).join(", "));
  process.exitCode = 1;
} else if (Object.keys(report.vulnerabilities).length) {
  console.warn(`Known dev-only picomatch advisory remains in ${Object.keys(report.vulnerabilities).length} reported packages; see docs/frontend-dependencies.md. Full report: web/artifacts/npm-audit.json`);
} else {
  console.log("Dependency audit: no reported advisories.");
}
