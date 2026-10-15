# Dashboard design and technical audit

Date: 2026-10-14. Scope: `lucia-dashboard` (React 19, Vite, Tailwind). The audit itself changed no code; see the Fix status section of the review README for the fixes made afterwards.

## How this was done

Two independent read-only reviews, then merged:

- **Design review (critique).** Source reading plus one desktop and one 375px look at the Activity page in the built-in browser. All other pages were reviewed from source only.
- **Technical audit.** Impeccable detector, `tsc`, `eslint`, code greps, and `dist/` bundle sizes.

Not done: authenticated pages were not rendered or clicked through, there was no axe or Lighthouse run, and contrast ratios were computed by hand from token hex values. Counts from grep are signals, not itemized failures, and are marked as such below.

## Scores

| Review | Result |
|---|---|
| Nielsen heuristics | 22/40 (Acceptable) |
| Accessibility | 2/4 |
| Performance | 1/4 |
| Responsive | 3/4 |
| Theming | 3/4 |
| Implementation integrity | 3/4 |
| **Audit total** | **12/20 (Acceptable)** |

Verdict on design specificity: the brand layer (tokens, amber restraint, Outfit/DM Sans, agent mesh, trace timeline) is specific to Lucia. The page layer is mostly two generic shapes, card stacks and tables, and many pages ignore the design system's own button and input spec.

Positive: `tsc` is clean, there is no `any`, no leftover `console.log`, and SSE and interval effects clean up on unmount.

## P1: fix before release

1. **No route code splitting.** `src/App.tsx:4-35` imports about 28 pages eagerly. `dist/assets/index-*.js` is 1,027,413 bytes and the CSS is 123,710 bytes. `@xyflow/react` ships on first paint. This hurts first load on the Jetson/LAN target. Fix: `React.lazy` per page behind one `<Suspense>`, plus `manualChunks` for react and xyflow. Start with Activity/MeshGraph, VoicePlatform, Installer, Setup.
2. **Hand-rolled modals lack dialog semantics.** Div-based modals with no focus trap, Esc, or focus return: `AutoAssignPreviewModal.tsx:25`, `PluginRepoDialog.tsx:94`, `AgentsPage.tsx:612`, `AlarmsPage.tsx:599,821`, `ResponseTemplatesPage.tsx:145` (has `role=dialog` but no Esc or trap). Only `ConfirmDialog.tsx` is correct. WCAG 2.1.2, 2.4.3, 4.1.2. Fix: one shared `Modal` built on the `ConfirmDialog` `<dialog>` pattern.
3. **Destructive actions use native `confirm()` at about 20 call sites.** Examples: `AgentDefinitionsPage.tsx:92`, `AgentsPage.tsx:721`, `ModelProvidersPage.tsx:143`, `PromptCachePage.tsx:144,163`, `EntityLocationPage.tsx:363,407,504,518`, `VoicePlatformPage.tsx:664-826`, `UserMemoriesPage.tsx:126`, `ConfigurationPage.tsx:680`. These ignore the theme, default focus to OK, and give terse messages with no stated consequence. Bulk actions such as "Set all entities to hidden" read as routine. Fix: a `useConfirm()` hook returning a promise on top of `ConfirmDialog`, with a consequence line and type-the-name confirm for provider and agent deletion.
4. **Form labels not associated with controls, and no global focus style.** 21 of 29 page files have no `htmlFor` or `aria-label` (for example `ModelProvidersPage.tsx:366-372`, `TraceListPage.tsx:102-111`). No global `:focus-visible` rule exists. There are 19 `focus:outline-none` uses, and inputs use an off-palette `focus:ring-blue-500` (7 places) against the amber ring in DESIGN.md. WCAG 1.3.1, 2.4.7, 4.1.2. Fix: a `Field` component using `useId`, one base `:focus-visible { outline: 2px solid var(--color-amber); outline-offset: 2px }`, and audit the 19 outline removals.
5. **Trace rows are mouse-only, and live feedback is silent.** `TraceListPage.tsx:181-184` (and the command-trace list) use `<tr onClick>` with no link, role or tabindex, so keyboard and screen-reader users cannot open a trace, the core inspection task. Toasts (`ToastContainer.tsx`) and most error banners have no `role="alert"` or `aria-live`; only `AppliancePage` does. Fix: make the first cell a `<Link>`, add `role="status"` or `role="alert"`.

## P2: next pass

6. **Navigation is a flat list of 22 with labels that differ from page titles** (`App.tsx:44-67`). Examples: "Cmd Traces", "Agents" vs "Agent Registry", "Providers" vs "Model Providers", "Voice Platform" vs "Wyoming control room", "Matcher Debug" (unexplained). Configuration, the setup entry, sits last. Fix: group under headers (Monitor, Agents, Home, Tuning, System) and make titles match nav labels.
7. **Page chrome drifts.** Five h1 styles, subtitles on some pages only, three card treatments, four loading treatments, `rounded` (4px) buttons against the 12px spec, and a bare "← Back" on edit forms with no dirty check (`ModelProvidersPage.tsx:347-352`). Fix: extract `PageHeader`, `Card`, `Button`, `Field`, `EmptyState`, `LoadingState`, `ErrorState`. This also removes much of P1 items 2-4.
8. **Raw Tailwind palette colors bypass the token system.** 41 hits for the red/green/blue/orange/yellow pattern in the design review, 87 with the broader pattern in the audit. Examples: `ActivityPage.tsx:117`, `AgentDefinitionsPage.tsx:188-197,549`, `AgentsPage.tsx:148-152,336`, `ConfigurationPage.tsx:836,951`, `ModelProvidersPage.tsx:813-909`, `SetupPage.tsx:691`, `PresencePage.tsx:13`. They are tuned for dark and ignore the light-theme overrides at `index.css:62`. Light-mode contrast for `text-orange-400` and `bg-yellow-900/50` is a risk, not measured. Fix: map to `info`, `sage`, `rose`, `amber` tokens.
9. **Activity (the home route) is the weakest page.** Nine same-weight KPI tiles (`ActivityPage.tsx:37-47`) leave an orphaned row at `xl`. Three cache tiles are all amber, though amber is meant to be reserved. Fetch failures are swallowed (`:193-195,212`), so an outage reads as "0 requests / 0.0%". The tiles do not link anywhere, there is no empty state, and the idle mesh is a blank 420px field. Fix: about 4 tiles, tiles as links, an error or stale banner with Retry, an empty state, and no emoji.
10. **Reduced motion not honored for runtime animation.** The only `prefers-reduced-motion` rules are `index.css:599,642` (installer and appliance). `animate-bounce` dots at `AgentsPage.tsx:549` and `ConversationPage.tsx:393` (the detector's only two findings, both true) and the `animate-pulse` and `animate-spin` uses are unguarded. WCAG 2.3.3. Fix: `motion-safe:` variants or a static "…", not a global kill.
11. **Nested `<main>`.** `App.tsx:311` and `ConfigurationPage.tsx:901`. Change the inner one to `<section>`.
12. **Lint errors.** `EntityMultiSelect.tsx:39,69` (`react-hooks/set-state-in-effect`). `VoicePlatformPage.tsx:1350` disables a rule for a plugin that is not installed (`jsx-a11y/media-has-caption`), which also hides a real question about captions on that media element. Three `exhaustive-deps` warnings: `PluginRepoDialog.tsx:42`, `EntityLocationPage.tsx:286,319`. `tsc` is clean.
13. **Emoji carry meaning.** `ActivityPage.tsx:44-45,100,116-123`, `ConfigurationPage.tsx:827`. They are not theme-aware or `aria-hidden`, and they clash with the Observatory tone. Status also leans on color alone (rose and sage numbers).
14. **Errors and recovery.** `TraceListPage.tsx:164` says only "Failed to load traces." with no retry. Many API errors drop the server's response body (`api.ts:45-91`). Empty filtered tables say "No traces found." with no "Clear filters". Capability-check failures retry every 2s forever with no UI (`App.tsx:159-163`).
15. **No efficiency accelerators.** No shortcuts or command palette across 22 routes. Trace filters live in component state only, so they are not shareable URLs and are lost on Back. "Export" covers only the current page. Pagination is Previous/Next only, with no sort or saved views.
16. **Live feed not announced.** The activity feed (`ActivityPage.tsx:143-147`) has no `role="log"` or `aria-live`, and there is no `document.title` update on route change.

## P3: polish

17. About 70 uses of text below 12px (`text-[9-11px]`), concentrated in `AgentsPage.tsx:146,188,193,226,344`. Raise anything that is not a decorative badge.
18. Polling cadence: `TaskTracker.tsx:126` polls every 2s alongside the SSE stream, and `RestartBanner.tsx:23` polls every 5s with no abort or visibility check. Pause when `document.hidden`, and poll only while SSE is disconnected. The network log showed duplicate requests, likely StrictMode in dev.
19. Touch targets: 193 sites pair small icons (`h-4` to `h-8`) with `onClick` or `p-1`, for example `ResponseTemplatesPage.tsx:152`. Probably meets WCAG 2.5.8 (24px) but not the 44px target. Likely gap, not measured.
20. Mobile top bar shows only "Lucia", not the current page. Nav items pop in late (`App.tsx:250`), shifting the list. "Reset All" sits permanently beside a benign toggle (`ConfigurationPage.tsx:800-832`); move it to a danger zone. The "Show secrets" label is duplicated in the label and toggle text.
21. `key={index}` appears 31 times; check lists that insert or delete. No virtualization, but lists are bounded today (paged traces, activity capped at 100).
22. Unlabeled icon-button count is unverified: 37 `aria-label`s on 294 buttons is a signal. Run an axe or Playwright pass (Playwright is already a devDependency) to enumerate.

## Persona red flags

- **Alex (power user):** native confirms break flow, no shortcuts, filters not in the URL, export is current page only.
- **Sam (keyboard/screen reader):** unlabeled inputs, mouse-only trace rows, missing focus rings, unannounced toasts and live feed, modals with no trap or Esc.
- **Jordan (first-timer):** the home page is all zeros with no next step, 22 ungrouped nav items, unexplained terms (Router cache, Matcher Debug, Wyoming, MCP, Skill Optimizer), raw error strings.

## What is working

- Token system with real light and dark parity (`index.css:34-71`); self-hosted fonts with `font-display: swap`.
- `ConfirmDialog.tsx` (native `<dialog>`, focus restore, cancel-first focus) and `CustomSelect.tsx` (proper combobox/listbox) are the right primitives. Reuse them.
- Shell semantics: labelled nav landmark, mobile drawer with 44px targets, theme selector.
- Good operational copy where it exists, for example `ModelProvidersPage.tsx:414` and the memory delete confirm at `UserMemoriesPage.tsx:126`.
- Installer and appliance animations already respect reduced motion.

## Suggested order

1. Shared primitives: `Modal`, `useConfirm`, `Field`, global `:focus-visible` (resolves P1 items 2-4).
2. Route code splitting (P1 item 1).
3. Linkable trace rows plus `aria-live` on toasts and errors (P1 item 5).
4. Token cleanup and `PageHeader`/`Card`/`Button` extraction (P2 items 7-8).
5. Nav grouping and title alignment, then an Activity rework.
6. Axe/Playwright pass to confirm accessibility fixes and catch the unlabeled controls.

Suggested Impeccable commands: `/impeccable harden` (modals, errors, empty states), `/impeccable optimize` (code splitting, polling), `/impeccable layout` and `/impeccable shape` (Activity, nav grouping), `/impeccable extract` (shared primitives), `/impeccable polish` last.
