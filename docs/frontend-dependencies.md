# Frontend dependencies / Dependency frontend

Checked on **18/11/2026**. Vite is pinned at **7.3.8**, Vitest at **4.1.12** and JSDOM at **27.4.0**, with npm lockfile integrity entries. These releases support the existing Node22 build; this is a compatible toolchain update within the existing major versions, not a claim that every dependency is the newest release. See the upstream [Vite release](https://github.com/vitejs/vite/releases/tag/v7.3.8) and [Vitest release](https://github.com/vitest-dev/vitest/releases/tag/v4.1.12).

VI: Audit trước cập nhật báo6 package có cảnh báo, gồm Vitest mức critical và Vite mức high. Sau cập nhật còn **5 package mức high**, cùng bắt nguồn từ ReDoS của `picomatch`: picomatch → fdir/tinyglobby → Vite/Vitest. Đây là số package được npm gắn cảnh báo, không phải5 lỗi độc lập. `npm audit --omit=dev` không báo cảnh báo production tại thời điểm kiểm tra.

The [picomatch advisory GHSA-fc5q-39xc-vjpf](https://github.com/advisories/GHSA-fc5q-39xc-vjpf) lists versions through4.0.7 as affected with no patched version. The [upstream releases](https://github.com/micromatch/picomatch/releases) and npm registry also expose4.0.7 as the latest available version at this check. The remaining issue concerns crafted glob patterns in Node tooling. The repository supplies its own build/test patterns, and `npm run dev` binds127.0.0.1. The packaged production image contains .NET and built static assets, not the Node development server. These limits do not make the upstream finding fixed.

## CI policy / Quy tắc CI

`npm run audit:ci` runs a fresh full npm audit, saves its unmodified vulnerability data to `web/artifacts/npm-audit.json`, and fails on unavailable/invalid reports or unexpected findings. The only accepted advisory URL is the one above, propagated through exactly the documented package-name set; every affected installed node must have `dev: true` in the lockfile, and critical findings are never accepted. A different advisory in the same package also fails. No package-version renaming, fake override or blanket successful exit conceals a finding. If the upstream fix removes the finding, a clean report passes; review/remove this exception when updating that dependency.

GitHub Actions uploads the full audit report even when a later step fails. Frontend DOM/transport tests and TypeScript/Vite production builds run after the gate. Local production audit output and the full report are retained in the handover. This is a dependency-audit checkpoint, not a penetration test or a guarantee that undisclosed vulnerabilities do not exist.

```powershell
npm --prefix web ci
npm --prefix web run audit:ci
npm --prefix web audit --omit=dev
npm --prefix web test
npm --prefix web run build
```
