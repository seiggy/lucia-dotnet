# Review 01: App shell and Monitor pages

Scope: app shell and shared components, Activity, Traces, Command traces, Exports. React 19 + Vite + Tailwind, `lucia-dashboard/src`. Read-only review, 2026-10-14.

## Method and the one big caveat

Every line of source for every page and component in scope was read. The impeccable detector returned `[]` for all 14 files (no anti-pattern hits), so every finding below comes from reading the code or from the live browser.

**Live testing was mostly blocked, and the block is itself finding S1.** The app at `localhost:7233` is the Vite dev server (HTTP/1.1). Seven other tabs on the same origin were open, each holding at least one permanent SSE connection. Chrome allows 6 connections per host, so my tab starved:

- First load: the shell rendered once, the Activity page showed 4 skeleton tiles, "No agents registered", a skeleton agent table and a red **Disconnected / Retry** badge. A `fetch()` to `/api/activity/summary`, `/api/traces/stats`, `/api/exports` and five other endpoints all hung and aborted after 8s. `curl` to the same endpoints returned 401 in under 30ms, so the backend is fine; the browser queue is the problem. `netstat` showed 28 ESTABLISHED entries on port 7233.
- Navigating to `/does-not-exist` then hung for 300s (tool timeout). The document went `interactive`, only 36 of roughly 70 dev modules loaded, `#root` stayed empty and the page was a blank dark rectangle for over 3 minutes.

So I could not open a real trace, a real command trace or the export history, could not run mobile, tablet, light-theme or drawer tests, and could not check the console on any data page. Anything labelled **observed live** below was seen in the first 30 seconds or from the network and tab data. Everything else is **inferred from source** and tagged that way. Widths I computed from class names are labelled "computed".

Not rediscovered here: the audit's native `confirm()`, unlabeled inputs, no global `focus-visible`, raw palette colors and emoji-as-icons are cited by `file:line` where they occur on these pages. One audit claim needs correcting: `ToastContainer.tsx:18` does have `role="status" aria-live="polite"`. The real problems with it are listed under S8.

---

## App shell and shared components (`App.tsx`, `index.css`, `components/*`, `theme/*`)

- **Purpose and primary task:** get an operator to any of 22 pages quickly, show background-task and error state, keep theme and sign-out at the edge.
- **Scores (source-based, Nielsen):** 22/40. Visibility 2, Match to real world 2, User control 2, Consistency 3, Error prevention 2, Recognition 3, Flexibility 1 (no shortcuts, no palette, flat nav), Aesthetic 3, Error recovery 2, Help 2. Flexibility scored 1: nothing speeds up a 22-item nav.
- **Cognitive-load failures: 4/8.** Fails: too many nav choices (22 ungrouped, well past 4 plus or minus 1), no grouping or chunking, no progressive disclosure, no current-location title on mobile. Passes: single focus per page, consistent chrome, clear active state, visible theme control.

### Desktop findings
- **Observed live (1440x900, dark):** sidebar shows 15 of 22 items. Response Templates, Plugins, Voice Platform, User memories, Matcher Debug, Configuration and the conditional Appliance entry are below the fold behind a 6px scrollbar (`index.css:131-146`). Configuration, the setup entry, is the one hardest to find. The "Appearance" label, 3-button theme control and Sign Out take about 150px of the 900px height permanently (`App.tsx:276-291`).
- **Observed live:** `document.title` stays "Lucia — Home Automation" on `/` and `/does-not-exist`. No code calls `document.title`.
- **Inferred:** active state is correct. `NavLink` sets `aria-current="page"`, `/traces` does not match `/command-traces`, and `/` uses `end`. Hidden drawer is removed from the tab order on mobile via `invisible` (`App.tsx:225`), which is good.

### Mobile findings (inferred from source; mobile pass was blocked)
- Top bar shows only the logo and word "Lucia" (`App.tsx:305-308`). The user never sees which page they are on.
- Drawer: `App.tsx:211-227`, `297-309`. No Esc handler, no focus move into the drawer on open, no trap, no focus return to the hamburger on close, no `aria-expanded`/`aria-controls` on the hamburger, no `role="dialog"`/`aria-modal`, no scroll lock (the `main` behind it still scrolls), and the `aside` comes before the `header` in the DOM so Tab from the hamburger goes to page content, not the open drawer. The overlay is `aria-hidden` and click-only.
- Close animation likely never plays: `invisible` flips `visibility` at once while only `transform` and `opacity` are transitioned (`index.css:201-204`, `App.tsx:225`). The drawer snaps shut. Not verified live.
- Tap targets: hamburger and close are 44px (good, `App.tsx:240,300`). Nav rows are `py-2.5` with 18px icons, about 40px (computed). `ToastContainer` dismiss is about 14px (`ToastContainer.tsx:33-38`). `CustomSelect` trigger is `py-2 text-sm`, about 38px (`CustomSelect.tsx:156`).

### Pain points and friction

- **S1. P1. One tab uses 2 permanent connections, and 3 or more tabs can freeze the app.** `TaskTracker.tsx:67` opens `/api/tasks/background/stream` on every route. It also polls `/api/tasks/background` every 2s (`:126-133`) while SSE is connected, and the comment at `:125` says 5s. Activity adds `/api/activity/live` (`useActivityStream.ts:35`), command traces adds a third stream (`useCommandTraceStream`). Over HTTP/1.1 (dev server, plain LAN deployment, reverse proxies) the 6-connection limit is shared across tabs, so a user with 3 dashboard tabs open can no longer load pages, fetch data or even navigate. Observed live (see top). Fix: poll only while SSE is closed, pause polling and close SSE when `document.hidden`, share one stream across tabs (`BroadcastChannel` or `SharedWorker`), and serve the dashboard over HTTP/2. Impeccable: `optimize`, then `harden`.
- **S2. P1. [live, CONFIRMED] Mobile drawer is not an accessible dialog.** `App.tsx:211-227,297-309`, details above. WCAG 2.1.2, 2.4.3, 4.1.2. Fix: reuse the native `<dialog>`/`ConfirmDialog` pattern or add `inert` on the page behind, move focus to the first nav link on open, restore to the hamburger on close, add `aria-expanded`, Esc to close, `overflow-hidden` on `body` while open. Impeccable: `harden`.
- **S3. P2. 22 flat nav items, labels differ from page titles.** `App.tsx:44-67`. "Cmd Traces" is a Lucia-internal abbreviation. "Matcher Debug" and "Skill Optimizer" are unexplained. Observed: seven items sit below the fold at 900px. Fix: group under Monitor (Activity, Traces, Command traces), Agents, Home, Tuning, System, with sticky group headers and collapsible groups; pin Configuration in the footer. Impeccable: `layout`, `clarify`.
- **S4. P2. Unknown URLs are silently rewritten.** `App.tsx:353` sends every unknown path to Activity with no message. A typo, a stale bookmark or a removed route looks like it worked. `/appliance` without an appliance does the same (`:350`). An unauthenticated deep link is replaced by `/login` (`:196-205`) and `LoginPage.tsx` never reads the previous location, so the deep link is lost after sign-in (grep: no `location` use). Fix: a real 404 page with a link to Activity; pass `state={{from}}` and restore it after login. Impeccable: `harden`.
- **S5. P2. No page titles or headings for orientation.** Static `<title>` (observed). `TraceListPage.tsx` and `CommandTraceListPage.tsx` have no `<h1>` at all; their first element is a stat grid. Detail pages have no `<h1>` either. Screen-reader users and tab switchers cannot tell where they are. Fix: set `document.title` per route and give every page one `<h1>`. Impeccable: `clarify`.
- **S6. P2. Root-level `ErrorBoundary` replaces the whole app.** `main.tsx:15-23` wraps it outside the router, and `ErrorBoundary.tsx:36-72` renders a full-screen card with no nav. One page render error removes navigation; "Try again" re-renders the same crashing tree; "Reload page" reloads the same broken URL (crash loop). Fix: place a boundary inside `<main>` in `App.tsx:311` keyed by `location.pathname` so nav survives and navigating away resets it. Impeccable: `harden`.
- **S7. P2. Failed background tasks are invisible.** `TaskTracker.tsx:196-198`: if nothing is running and the panel is collapsed it returns `null`, and the only way to expand is the pill, which only exists while tasks are active. A task that failed while you were on another page is never surfaced, and the failed row (kept in the panel up to 5 entries, `:189-191`) cannot be opened. Also on mobile the pill (`:205`) and the 320px panel (`:216`) sit at `bottom-6 right-6 z-50`, directly over the Next button of every paginated list (`TraceListPage.tsx:231-237`). No `aria-live`, `aria-expanded`, and 10px text (`:258,273`). Fix: keep a small badge after a failure until dismissed; on `<md` dock the pill above the content flow or into the top bar.
- **S8. P2. Toasts: announcement and size.** `ToastContainer.tsx:14` returns `null` when empty, so the live region is created already containing text. Most screen readers do not announce a region inserted with content, so the polite status is often silent. Errors also use `polite`, not `assertive`. Dismiss target about 14px (`:33-38`). `fixed top-4 right-4` with no max width, so a long message can stretch to the left edge on 375px and cover the sticky top bar (z-50 over z-30, `App.tsx:297`). Fix: always render the container, `role="alert"` for errors, `max-w-[calc(100vw-2rem)]`, 44px dismiss target. Impeccable: `harden`, `adapt`.
- **S9. P2. `CustomSelect` loses focus and has no accessible name.** (a) All trace and export filters render it without `label`, so every one is announced "Select an option" (`CustomSelect.tsx:142`; usage `TraceListPage.tsx:116,124`, `CommandTraceListPage.tsx:145,153`, `ExportPage.tsx:101,127`). The visible `<label>` is not tied to it. (b) Choosing an option by click (`:198`) or Enter (`:121-122`) closes the list but never refocuses the trigger, so focus falls to `<body>` after the portal unmounts. (c) Highlight uses `idx === highlight` with no `aria-activedescendant` or option ids, so arrow navigation is silent. (d) Opening autofocuses the text filter (`:50-53`); on a phone that raises the keyboard over a 4-option list. (e) Trigger height ignores the `size` prop (`:156` hard-codes `px-3 py-2 text-sm`, `pad` at `:130` is only used for options), `rounded-lg` 8px, `border-stone/40 bg-slate-warm`: it does not match the adjacent `rounded-xl border-stone bg-basalt` inputs in the same filter row. Fix: pass `label`, restore focus on close, add `aria-activedescendant`, skip the filter input when there are 8 or fewer options, 44px height. Impeccable: `harden`, `polish`.
- **S10. P2. Input focus ring is nearly invisible.** `index.css:194-198`: `.input-focus:focus` sets `box-shadow: 0 0 0 1px` at 30% amber and a border color change. A 1px 30% ring does not reach 3:1 non-text contrast on either theme. Used on every filter input and textarea in scope (`TraceListPage.tsx:30`, `TraceDetailPage.tsx:371,384`, `ExportPage.tsx:32`). Buttons and `CustomSelect` have no focus style (audit item already known); `ThemeSelector.tsx:42` and `ToggleSwitch.tsx:33` do. Fix: global `:focus-visible { outline: 2px solid var(--color-amber); outline-offset: 2px }` and make `.input-focus` a 2px ring. Impeccable: `polish`.
- **S11. P3. `RestartBanner` restarts with no confirm.** `RestartBanner.tsx:36-47` calls `triggerRestart()` directly: it takes the service down and ends in-flight conversations. No `role="alert"`, no warning that the dashboard will drop, and `restarting` never clears on success (`:30-33`), it relies on the 5s poll. Fix: `ConfirmDialog` with a consequence line.
- **S12. P3. `ConfirmDialog` is right but narrow.** `ConfirmDialog.tsx:67-74`: default label "Delete" in the rose style, so non-delete uses must remember to override label and the red still reads as danger; fixed ids `confirm-dialog-title` (`:50`) collide if two exist. No consequence line, no type-to-confirm. `h-screen` on iOS can exceed the visible viewport.
- **S13. P3. Capability check retries forever every 2s.** `App.tsx:159-163`. `/appliance` then shows "Checking appliance capability..." indefinitely (`:345-348`), no failure state. Add a retry cap and an error message.
- **S14. P3. `ToggleSwitch` is the model component** (44px target, `role="switch"`, `aria-label`). Minor: both `cursor-pointer` and `cursor-not-allowed` are applied when disabled (`ToggleSwitch.tsx:35-37`), and the focus ring also shows on mouse click because it uses `focus:` not `focus-visible:`.
- **S15. P3. Raw palette in shared components.** `TaskTracker.tsx:35,37,274,284` (emerald, rose-300, cyan-400), `CommandTimeline.tsx:28` (sky-400). These ignore the light-theme overrides.

### Bugs and errors
- Observed live: `Disconnected` badge shown from first paint (see A2). Console on the first load: only Vite debug lines and the React DevTools notice, no errors. No console access after the block.
- Network on first load: `/api/tasks/background` was fetched about 9 times in the first few seconds alongside the SSE stream; `/api/activity/summary`, `/mesh`, `/agent-stats` and `/appliance/capabilities` each fetched twice (StrictMode in dev, expected). `/api/auth/status` also twice.

### Performance notes
- Dev server serves about 70 unbundled modules, so first-load numbers here say nothing about production; the audit's bundle finding (no code splitting, `@xyflow/react` on first paint) stands (`App.tsx:11-38`).
- Two always-on pollers per tab: tasks every 2s (`TaskTracker.tsx:126`), restart check every 5s (`RestartBanner.tsx:23`, only mounted on Plugins). Neither pauses on `document.hidden`.

### Suggested changes
`optimize` (S1: stop double-feeding tasks, visibility-aware streams), `harden` (S2, S4, S6, S8, S9), `layout` and `clarify` (S3, S5: group the nav, title and h1 per route), `adapt` (S7, S8 on mobile), `polish` (S10, S15).

### Not exercised
Everything on mobile and tablet; light theme; the drawer, Esc and focus tests; Sign Out (mutates session); theme selector clicks (changes persisted setting); Restart button.

### Live pass (2026-10-14), app shell

Single tab `seed`, no other tabs open. Everything loaded normally (all `/api/*` 200), so the first-pass starvation no longer applies. Viewports: 1440x900 dark and light, 375x812, 640x900, 768x1024, 1024x768.

**Desktop dark and light.** No contrast failures or raw-color leaks seen on the shell in either theme. Sidebar has 22 links, each 40px tall. Nav container is 681px at 1440x900 (scrollHeight 954), so **15 of 22 items are visible, 7 hidden** [live, CONFIRMED]. At 1024x768 only **12** are visible. Page `<title>` stays "Lucia — Home Automation" on `/` and `/does-not-exist` [live, CONFIRMED]. Only 1 `<h1>` on Activity, 0 on `/traces`, `/command-traces` and both detail pages [live, CONFIRMED S5]. No skip link (0 matches); the sidebar has 26 tab stops (22 links, 3 theme buttons, Sign Out) before the first control in `main` [live, new, P3].

**Breakpoints and ThemeSelector.** The sidebar becomes persistent at **768px** (`md`), not 640 or 1040. At 640px it is still the drawer, with the ThemeSelector off-screen (left -89px) inside it. At 768, 1024 and 1440 the ThemeSelector sits in the sidebar footer. So there is no 640-1040 placement shift; the only gap is that 640-767px users must open the drawer to reach it. At 768px the sidebar eats 256 of 768px, leaving 512px of content [live, new, P3: tablet portrait gets desktop chrome and a squeezed page].

**Mobile 375x812 drawer** (all [live], CONFIRMED S2):
- Hamburger (`aria-label="Open sidebar menu"`) is 44px and has no `aria-expanded`. Drawer `aside` has no `role` and no `aria-modal`.
- After opening, focus **stays on the hamburger**. **Esc does not close** the drawer (still `visibility: visible` after Escape).
- DOM order is `aside` before `header`, so **Tab from the hamburger lands in page content** (an SVG edge in the mesh), not in the open drawer. Focus is not moved or trapped.
- Closing via the X: focus ends on a mesh edge behind the drawer, not on the hamburger. `body` overflow is `visible` (no scroll lock).
- Close is not animated: `visibility` is `hidden` by the 50ms sample. Snap-close CONFIRMED.
- Nav rows are 40px (under 44, over 24). Theme buttons are 48px, Sign Out 40px, close button 44px. At 375x812 the nav shows about 13 of 22 items; Tasks, Exports, Skill Optimizer, Response Templates, Plugins, Voice Platform, User memories, Matcher Debug and Configuration need an inner scroll (nav scrollHeight 954 in 569px).
- Mobile top bar shows only the logo and "Lucia" (headerText "Lucia"): no page name [live, CONFIRMED].
- No horizontal overflow on any page tried (scrollWidth 375 on `/`, `/traces`, `/command-traces`, `/command-traces/:id`, `/exports`).

**Network.** `/api/tasks/background` is polled every 2s while the SSE stream is open: 5 calls in 10s, and 344 matching calls buffered in a few minutes [live, CONFIRMED S1 poll half]. SSE `activity/live` and `tasks/background/stream` both open (2 permanent connections per tab). Console: only Vite debug lines and the React DevTools notice, no errors or warnings.

**Keyboard.** Browser default focus ring appears on keyboard focus (`outline: auto 1px`) on `CustomSelect`, so "no focus-visible" is **PARTLY REFUTED**: the ring exists but is the 1px browser default, not an app-styled 2px ring (S10 stands as weak, not absent). `CustomSelect` trigger accessible name is "Select an option" [live, CONFIRMED S9a]. Opening it focuses the "Type to filter..." input even for 5 options, `aria-activedescendant` is null [live, CONFIRMED S9c, S9d]. Esc closes it and returns focus to the trigger (the select-by-Enter focus-loss claim S9b was not conclusively tested).

**Verified claims.** S1 poll+SSE: CONFIRMED live. The "3 tabs freeze the app" part: not re-tested (would need extra tabs). S2: CONFIRMED live (all sub-claims above except `aria-controls`, not checked). S3 (22 flat items, 7 below fold): CONFIRMED. S4 unknown URL rewrite: CONFIRMED (see Activity). S5: CONFIRMED. S6, S7, S8, S11-S15: not checkable (no error, failed task or toast state available without mutating).

**New findings.**
- **N-S1. P3.** Mesh edges and nodes are tab stops: 10 nodes (`role="group"`, no label) plus 10 edges (labelled "Edge from X to Y") = 20 extra Tab presses on Activity, and they are the first thing Tab reaches after the drawer. See Activity N-A3.
- **N-S2. P3.** No skip-to-content link and `main` has no id (see above); 26 Tab presses to reach page content.
- **N-S3. P3.** The 12px `Prompt` `summary` on command trace detail is 20px tall and the "Include Corrections" checkbox on Exports is 13x13px, both under the 24px minimum (WCAG 2.5.8) [live].

---

## Activity (`/`, `src/pages/ActivityPage.tsx`)

- **Purpose and primary task:** answer "is Lucia healthy and what is it doing right now" at a glance, then drill into a trace.
- **Scores (source plus the first-paint look):** 19/40. Visibility of status 1 (shows "Disconnected" and skeletons or blanks where it should say "connecting" or "failed"), Error recovery 1 (failures swallowed), Flexibility 1 (nothing links to the underlying data), Match to real world 2, Consistency 2, User control 2, Error prevention 3, Aesthetic 2, Recognition 2, Help 2. Scored 1: Visibility, Error recovery, Flexibility.
- **Cognitive-load failures: 5/8.** Fails: nine same-weight numbers (working memory), no priority hierarchy, "Parsed / Fallback / Parser Rate" repeat one idea three ways, no time window on any number, no next step.

### Desktop findings
- **Observed live (1440x900, dark):** header "Activity / Platform metrics and live agent mesh" with the badge at right; 4 skeleton tiles; "Agent Mesh" 420px panel reading "No agents registered"; "Activity Feed" reading "Waiting for activity…"; "Agent Usage" skeleton. While data was blocked the skeleton never resolved and nothing explained why. Layout is a 2/3 + 1/3 split below the tiles; the feed panel is short next to the mesh.
- **Inferred:** tile grid is `lg:grid-cols-3 xl:grid-cols-6` (`ActivityPage.tsx:50`) with 9 tiles, so xl is 6 plus an orphaned 3. Loading renders 4 skeletons (`:52`) and then 9 tiles, a layout jump.

### Mobile findings (inferred; 375 pass blocked)
- Tiles stack one per row at under `sm` (`:50`), so nine tiles is about 9 x 84px = 756px of scroll before the mesh. The mesh and feed come after that.
- Mesh: fixed `h-[420px]` (`MeshGraph.tsx:247`), `panOnDrag` and `zoomOnScroll` on (`:262-263`). A finger dragging over the graph pans the graph instead of scrolling the page: a scroll trap taking 420 of 812px. On desktop the mouse wheel over the graph zooms instead of scrolling the page, same trap. `minZoom={0.4}` (`:256`) and node spacing 200px (`:111`) mean any topology wider than about 850px (4+ agents) cannot be fit on 343px; nodes clip and 14px labels render at about 6px. Node sublabel is 10px (`:81`).
- Feed rows are `text-xs`; agent name and tool name truncate with no tap-to-expand (`:157-159`).

### Pain points and friction

- **A1. P1. Fetch failures read as an empty system.** `ActivityPage.tsx:193-195` swallows errors ("cards will show loading state" is false: `loading` goes false in `finally` at `:196`, `summary` stays null, and `cards` is `[]` at `:37-47`, so the tile grid is blank, not skeleton). Mesh then says "No agents registered" (`MeshGraph.tsx:238-243`), usage says "No agent activity data yet" (`:77-83`). An outage and a fresh install look the same. The 30s refresh also swallows (`:212`). Fix: an `error` state with a "Couldn't load activity. Retry" banner; keep stale data with a "Last updated 14:02" stamp. Impeccable: `harden`.
- **A2. P2 (P3 on healthy load). [live, CONFIRMED, about 40ms flash] "Disconnected" shown before any connection attempt.** `useActivityStream.ts:19` initialises to `'disconnected'`, and the effect connects after first paint. Observed live: red dot, "Disconnected", and Retry visible on first paint and for as long as the connection was blocked. A first-timer sees an error on a healthy page. Fix: initial `'connecting'` state with neutral styling. `ConnectionIndicator` (`:11-24`) should also say what is lost ("Live feed paused").
- **A3. P2. KPI tiles: nine numbers, no hierarchy, ambiguous meaning.** `:37-47`.
  - "Error Rate" is `0%` in green when there are 0 requests (`:39`), claiming health with no data. It is also lifetime, not recent.
  - Three amber "hit rate" tiles (`:40-42`) read as warnings but are neutral metrics; a 0.0% cache hit rate with no lookups looks like failure.
  - "Router Cache Hit Rate", "Agent Cache Hit Rate" and "Combined" are three tiles for one idea; "Combined" is a ratio of sums (`:29-34`) and is not derivable by a reader from the other two.
  - "Command Parsed", "LLM Fallback", "Parser Rate" (`:44-46`) are count, count and their ratio; no total, and the sage threshold at 50% (`:46`) is arbitrary and unexplained.
  - "Tasks Completed" has no failed count, though `summary.tasks.failedCount` exists (`types.ts:360`). `conversation.errors` and `total` (`types.ts:363`) are never shown.
  - Emoji in two labels (`:44-45`).
  - No tile links anywhere; no time range stated. Fix: four tiles (Requests, Errors, Handled locally %, Cache hit %) with one-line definitions and a "since" label; each links to `/traces`, `/traces?outcome=error` or `/command-traces`. Impeccable: `shape`, `clarify`.
- **A4. P2. Mesh: readability, interaction, staleness.** `MeshGraph.tsx`.
  - Scroll trap and mobile clipping as above (`:256,262-263`).
  - No legend for state colors (amber processing, blue tools, green responding, rose error), only color for state (`:31-38`); active state adds a pulse (`:85-90`) not guarded by reduced motion.
  - Topology loads once (`ActivityPage.tsx:182-200`); the 30s refresh reloads summary and stats only (`:203-215`), so a newly registered agent never appears. Tool nodes are added from events (`:245-253`) and never removed.
  - Not keyboard or screen-reader reachable: no focusable nodes, no text alternative, `elementsSelectable={false}` (`:260`). A visually hidden list of agents and states would fix it.
  - Idle mesh is a large static diagram with nothing to act on, and clicking a node does nothing: a node could link to that agent's traces.
- **A5. P2. Activity feed.** `:126-166`.
  - `max-h-64` (256px) holds about 10 rows; the grid cell beside the 420px mesh leaves unused space.
  - Auto-scrolls to bottom on every event (`:129-133`) even if you scrolled up to read; no pause.
  - Error message is `truncate` with no `title` or expand (`:159`); the one thing you need from an error row is cut.
  - Events from concurrent requests interleave with no request id or grouping, rows are not links to the trace, and the feed is lost on navigation (state at `:176`; leaving the page unmounts it and the SSE resumes with no replay).
  - `key={i}` on a list sliced from the front (`:153`, `:224`) remaps keys on every event.
  - Emoji icons (`:116-123`), raw `text-blue-400` (`:117,119`); no `role="log"` or `aria-live`; times are seconds-only `toLocaleTimeString` with no date.
- **A6. P3. Agent Usage table.** `:72-111`. Not sortable by error rate; rows not links to `/traces?agent=…` (no URL filter exists, see T1); `s.errorRate > 5` hides small error rates at "sage" (`:102`); emoji prefix (`:100`); no empty-state guidance.
- **A7. P3. Cleanup.** `setTimeout` at `:263` and `:270` is never cleared on unmount (only `clearTimeoutRef` is tracked and never cleared on unmount either). Summary polling every 30s continues in hidden tabs (`:203-214`).

### Bugs and errors
- Observed: skeleton never resolves when the API is starved (S1); `Disconnected` at first paint (A2).
- Inferred: stale topology (A4); blank tile grid on error (A1); `key={i}` churn (A5).

### Performance notes
- `@xyflow/react` plus its CSS load with the first route (`MeshGraph.tsx:13-25`); `useNodesState`/`useEdgesState` copy the layout on every `nodeStates` change (`:225-232`), so each SSE event recomputes layout and resets nodes. Fine at 10 nodes, costly at 100 tool nodes.
- The tab-wide SSE and polling costs are under S1.

### Suggested changes
`harden` (A1, A2), `shape` and `clarify` (A3: 4 linked tiles, defined metrics), `adapt` (A4: collapse the mesh to a list on `<md`, or disable pan and zoom and use a "Fullscreen" action), `layout` (A5: make the feed the same height as the mesh, newest first, "Jump to latest" pill, link rows to traces), `optimize` (reload mesh on the 30s tick).

### Not exercised
Retry button on the badge (reconnects the real SSE); nothing mutating exists on this page. Mesh pan and zoom on touch, node states under real traffic, feed overflow and long errors were not observable.

### Live pass (2026-10-14), Activity

**Desktop dark and light (1440x900).** Loads cleanly. Header "Activity", subtitle, "Live" badge, 9 tiles (6 + 3 at 1440, 3 columns at 1024), mesh 418px, feed "Waiting for activity…", Agent Usage "No agent activity data yet". Light and dark both readable; amber metric values are legible on both. Real data: Total Requests 0, Error Rate 0%, all cache rates 0.0%, Tasks Completed 3, LLM Fallback 5, Command Parsed 0.

**Claims.**
- **"Disconnected" on first paint (A2): CONFIRMED but brief.** Sampling a same-origin iframe every 25ms: blank at 25ms, content at 80ms, **"Disconnected" at 102ms, "Live" at 142ms**. So a healthy connection shows the red state for about 40ms (a flash), not for the whole load. It persists only when the stream is blocked or fails. Severity drops from P2 to P3 for a healthy load; the fix (initial `connecting` state) is the same.
- **"0%" Error Rate in green with no requests (A3): CONFIRMED live.** Also a data contradiction not predicted before: Total Requests is 0 while LLM Fallback is 5 and Tasks Completed is 3, so "Total Requests" does not count the same things as the other tiles.
- **xl 6+3 orphaned tile row: CONFIRMED** (visible at 1440). Emoji in two labels: CONFIRMED (renders as a tiny glyph, "Command Parsed" icon is unreadable at this size).
- **Mesh mobile clipping (A4): CONFIRMED live.** At 375px the graph is at the `minZoom` 0.4 (`translate(-215px,169px) scale(0.4)`); **7 of 10 node boxes sit outside the 341px container** (x from -198 to 573). At 1440 light the outermost nodes are also clipped at the panel edges, and at 1024 the nodes shrink to unreadable labels.
- **Scroll trap (A4): CONFIRMED live.** A wheel scroll at the graph's centre with the graph filling the viewport left `scrollY` at 900; the page did not scroll. On touch the graph also captures drags (`touch-action: auto`).
- **"Mesh not keyboard reachable": REFUTED.** Each of 10 nodes (`role="group"`, no label) and 10 edges (labelled) is `tabindex=0`. The problem is the reverse: 20 tab stops with no state or action, and nodes have no accessible name. A self-edge "Edge from orchestrator to orchestrator" exists in the DOM (a topology data oddity).
- **Mobile tile stack (A3): CONFIRMED.** 9 tiles at 86px each; the mesh starts at 1093px of a 1804px page, more than a full screen of scroll before the mesh.
- A1 (fetch failures read as empty), A5, A6, A7: not checkable (no failure available; no live events during the pass).

**Network and console.** Clean, 200s. Summary polling (30s) not seen in the 10s window.

**Tablet 768 / 1024.** At 1024x768 the content column is about 700px, mesh nodes render at about 6px text and the Agent Mesh and Activity Feed panels are cramped but not overflowing (scrollWidth 1018 vs 1024).

---

## Traces (`/traces`, `src/pages/TraceListPage.tsx`)

- **Purpose and primary task:** find a conversation trace (by text, label, agent, date) and open it to review or label it.
- **Scores (source-based):** 21/40. Flexibility 1 (no URL state, no saved filters, no sort), Error recovery 1 (no retry, no clear-filters), Match to real world 2, Visibility 2, User control 2, Consistency 2, Error prevention 3, Recognition 3, Aesthetic 3, Help 2. Scored 1: Flexibility, Error recovery.
- **Cognitive-load failures: 3/8.** Fails: stat tiles that do not respond to the filters beside them (mixed scope), five filters in one wrapping row with no grouping, nothing signals which rows still need review.

### Desktop findings
- **Inferred (not viewable live):** five stat tiles, then a wrapping filter row (search, Label, Agent, From, To, Export Page), then a 5-column table (Timestamp, User Input, Agents, Duration, Label) and Previous/Next with "Page x of y (n total)".
- No heading, so the first element on the page is a number grid.

### Mobile findings (inferred; blocked)
- Tiles: `grid-cols-2` (`:90`), 5 tiles, so 3 rows. Filters stack and wrap, each CustomSelect about 38px (S9). The table scrolls horizontally inside `overflow-x-auto` (`:168`), and the leftmost column is a `whitespace-nowrap` full locale timestamp (`:186`, about 150-170px, computed), so the user input, the reason you are here, starts at the edge of a 343px viewport. The label badge and agent chips are off-screen to the right.
- Per-row: 80-char input truncate plus a `{n}↩` chip with a `title` tooltip only (`:190-197`), no touch equivalent.

### Pain points and friction

- **T1. P1. [live, CONFIRMED] Filters, page and search are not in the URL and are lost on the way back.** `TraceListPage.tsx:34-39` are plain `useState`. Opening a trace and pressing "Back to Traces" (`TraceDetailPage.tsx:124`, `navigate('/traces')`) mounts a fresh list at page 1 with no filters. Reviewing a page of unlabeled traces means: open, label, Back, re-pick the filter and page, repeat. Alex cannot share a link to "negative traces from agent X this week" either. Fix: store `page`, `search`, `label`, `agent`, `from`, `to` in `useSearchParams`; have Back use `navigate(-1)` or a stored `?` string. Impeccable: `harden`, `shape`.
- **T2. P1. [live, CONFIRMED on command traces] Rows are mouse-only.** `:181-184` `<tr onClick>`; keyboard and screen-reader users cannot open a trace. Known from the audit; cited here because it is the page's core action. Fix: make the user-input cell a `<Link to={`/traces/${id}`}>` and keep the row click as an enhancement.
- **T3. P2. The table is destroyed on every change and queries fire per keystroke.** `useQuery` has no `placeholderData` (`:48-51`), so a new key sets `traces` to `undefined`. The table, pagination and any scroll position unmount and are replaced by "Loading traces…" (`:158-163`), then the table remounts. Search has no debounce (`:108`) so every character is a request and a flash. Fix: `placeholderData: keepPreviousData`, a 250ms debounce, and dim the existing rows while loading. Impeccable: `optimize`.
- **T4. P2. Filters do not cover the task.** The "Errored" tile shows a count (`:95`) but there is no way to filter to errored traces. No sort (newest first is implicit), no duration filter, no "needs review" shortcut (unlabeled is selectable but the 'Unlabeled' badge is on most rows, so the label column mostly shows noise). Fix: an Outcome filter and clickable stat tiles that apply a filter; hide the "Unlabeled" badge or show only labeled ones.
- **T5. P2. Dead-end states.** `:164` "Failed to load traces." with no retry and no reason; `:210` "No traces found." with no "Clear filters" and no hint that a filter caused it. Fix: reuse one `ErrorState` and `EmptyState`. Impeccable: `harden`.
- **T6. P2. Labels are not bound to their controls.** `:102,115,123,131,140` are bare `<label>`s with no `htmlFor`, and `CustomSelect` announces "Select an option" (S9). The search input has only a placeholder.
- **T7. P3. Stat tiles mislead.** The five tiles come from `fetchStats` (all traces), but sit directly above filters that change the table (`:89-97`). With a filter active, "Total 540" and "Page 1 of 1 (3 total)" are visible together. Errored is amber, not rose (`:95`). The `StatCard` is copy-pasted (`:248-258` and `CommandTraceListPage.tsx:273-283`): extract.
- **T8. P3. "Export Page" is a client-side dump of 20 rows** (`:148-154`), named like the server-side Exports page. `downloadJson` writes whatever the table has. Rename to "Download this page (JSON)" or move into a menu; link to Exports for the real thing. Impeccable: `clarify`.
- **T9. P3. Pagination and formatting.** Previous/Next only (`:223-238`), no first/last, no page size, Next does not scroll to the top of the table. Duration is raw `12345 ms` (`:202`), timestamp is full `toLocaleString` (`:23`) with no relative time ("3m ago"). The pagination summary is left-aligned and the buttons right: on mobile fine, but 32px high (`py-1.5`).

### Bugs and errors
- Inferred: table unmount flash (T3); filters lost on Back (T1).
- Observed live: none on this route (could not load).

### Performance notes
- No debounce; each keystroke is a query with a different key, so React Query caches one entry per partial string for 5 minutes (default). `refetchOnWindowFocus: false` is set globally (`main.tsx:10`), so the list never refreshes on its own; there is no refresh button and no SSE (unlike command traces).

### Suggested changes
`harden` and `shape` (T1, T2, T5), `optimize` (T3), `layout` (T4 clickable stat tiles as filters; sticky filter bar on desktop; on mobile put filters behind a "Filters (2)" button and render rows as cards: input first, then agent chips and label), `clarify` (T8, T7).

### Not exercised
Filter dropdowns, date pickers, search typing, Export Page (triggers a download), row clicks. All blocked by the connection starvation and, for Export Page, by the rules.

### Live pass (2026-10-14), Traces

**State of the instance: zero LLM traces exist** (Total 0, Positive 0, Negative 0, Unlabeled 0, Errored 0; "No traces found."). So no row could be opened and the row, label, pagination and Export Page paths are not checkable live with data.

**Desktop dark and light.** Five zero tiles, filter row, table header, one "No traces found." row, "Page 1 of 0 (0 total)" with both buttons disabled. Light and dark readable. 0 `<h1>` [live, CONFIRMED S5].

**Mobile 375x812.** No horizontal overflow (scrollWidth 375). All controls 38px (search, selects, date inputs, Export Page) and 34px (Previous, Next): under 44, over 24. Page is only 841px tall with no data.

**Claims.**
- T5 empty state: **CONFIRMED live**. "No traces found." with no hint (fresh install and filtered-out are the same string); but here it also shows **"Page 1 of 0 (0 total)"**, a new P3 (page 1 of 0 should read "No results" or "Page 1 of 1").
- T6 bare labels / "Select an option" names: CONFIRMED (same `CustomSelect`).
- T2 mouse-only rows: not checkable here (no rows); CONFIRMED on the identical Command traces table (see below).
- T1 filters lost on Back: not checkable here; CONFIRMED on the identical Command traces page.
- T3, T4, T7-T9: not checkable (no data).

---

## Trace detail (`/traces/:id`, `src/pages/TraceDetailPage.tsx`, `SpanTimeline.tsx`)

- **Purpose and primary task:** understand why a request routed and answered as it did, then label it positive or negative with notes and a correction.
- **Scores (source-based):** 21/40. Error prevention 1 (state bleeds between traces and saving can erase existing notes), Error recovery 1 ("not found" for every failure, dead end), Visibility 2, User control 2, Match to real world 3, Consistency 2, Flexibility 1 (no next/previous, no filtered navigation), Recognition 3, Aesthetic 3, Help 2. Scored 1: Error prevention, Error recovery, Flexibility.
- **Cognitive-load failures: 3/8.** Fails: the primary action (labeling) is the last of 10 sections, no progressive disclosure of long prompts and responses beyond one `details`, and related content is split across three places (history, related, routing).

### Desktop findings
- **Inferred:** section order is back and actions, header card (time, session, duration, error), conversation history, user input, final response, collapsed system prompt, related traces, routing decision, span timeline, agent executions, label form. Everything is a stacked card; the Save Label button is at the far bottom.

### Mobile findings (inferred; computed widths)
- **Span timeline is unreadable at 375px.** Each row is a 192px label (`SpanTimeline.tsx:70`) plus a 64px duration (`:87`), 8px gaps, inside `p-4` card inside `px-4` main: 375 - 32 - 32 - 192 - 64 - 16 = about 39px for the bar. Span widths are percentages of 39px with a 0.5% minimum, so most bars are 1 to 5px. The time axis prints three labels (`:96-100`) in that same space and will collide. Tooltips are `title` only (`:84`), unavailable on touch.
- 3 action controls in one row (Back, Export JSON, Report Issue) on 343px (`:122-145`): each about 130px, so the row wraps or squeezes. Label buttons are 3 in a row (`:331-362`), each `px-4`, about 110px each: fits, but they never wrap (no `flex-wrap`), likely overflowing at 320px.
- Long unbroken strings in `whitespace-pre-wrap` paragraphs (`:186,195,200,66`) without `break-words` can push the card wider than the viewport, e.g. a long entity id or URL in a response. Tool-call `<pre>` blocks have `overflow-x-auto` (`:50,53`), which is correct.

### Pain points and friction

- **D1. P1. Label state leaks across traces and Save can overwrite existing notes.** `:90-92` hold `labelStatus`, `notes`, `correction` in `useState`. The route is `<Route path="/traces/:id" …/>` (`App.tsx:315`) and the Related Traces rows navigate to another id (`:226-230`), so React reuses the same component instance: a pending "Negative" pick, typed notes and the "Label saved." message carry over to the next trace. Separately, notes and correction are never initialised from `trace.label` (`types.ts:59-60` has `reviewerNotes` and `correctionText`), so opening an already-labeled trace shows empty fields, and `Save Label` sends `reviewerNotes: undefined` (`:100-104`) in a PUT (`api.ts:86-91`). If the server treats PUT as replace, saving a label change wipes the existing notes and correction. Inferred; confirm server semantics. Fix: `key={id}` on the page body (or reset on id change), hydrate from `trace.label`, only send changed fields, add an unsaved-changes indicator. Impeccable: `harden`.
- **D2. P2. The review loop is long.** Label is the last section, there is no previous/next within the current filter, and after saving the user must go back (which resets filters, T1). Save feedback is an inline "Label saved." that never clears (`:400-402`). Label buttons are toggles with no `aria-pressed` (`:332-361`). Fix: sticky label bar at the bottom on mobile and a right-hand rail on desktop; "Save and next unlabeled". Impeccable: `layout`, `shape`.
- **D3. P2. Related Traces are `div onClick`.** `:226-230`: not focusable, no role, no link semantics, no middle-click. Fix: `<Link>`.
- **D4. P2. "Back" and error handling.** `:123-128` Back is `navigate('/traces')` (see T1). `:117` shows "Trace not found." for a 404, a 500 or a dropped connection alike, with no retry and no link back, a dead end. The `isLoading` view (`:112-116`) has no heading or back link either.
- **D5. P2. Span timeline hides meaning.** Bars are color-coded sage, amber or dust with no legend (`SpanTimeline.tsx:8-13`); the cache and LLM badge text partly explains it but only for tagged spans. Spans are sorted by start but the parent-child structure is ignored, so a nested span looks like a sibling (`:33-35`). Tags are visible only in the hover `title` (`:84`). No keyboard or touch access to details. Fix: show tags in a row-expand, add a legend, indent by parent, switch to a stacked layout on `<md` (label above the bar). Impeccable: `adapt`, `layout`.
- **D6. P2. "Report Issue" sends trace content to github.com in a URL.** `:136-143` opens `buildConversationTraceIssueUrl`, which embeds the user's utterance, a response excerpt (500 chars), session id and error text (`traceExport.ts:32-70`) as query parameters. The product promises privacy-first and local processing (`PRODUCT.md`). There is no notice, no preview and no redaction (home addresses, names and device names spoken to a voice assistant are common). The user does see GitHub's form before submitting, which limits harm, but the first click discloses nothing in the UI. Fix: confirm dialog with an exact preview and a "Copy as text" option. Impeccable: `clarify`, `harden`.
- **D7. P3. Overflow and readability.** Add `break-words` to the pre-wrap blocks (`:186,195,200,66`); session id (`:156`) is mono `text-xs` with no wrap or copy button; durations are raw ms; headings are `text-xs uppercase tracking-wider text-dust` for every section, so scanning the 10 sections is hard (low hierarchy: `:221,258,317,330`). Dust text at 12px on a glass panel is also near the contrast limit.
- **D8. P3. Emoji-free but off-token.** Colors use tokens here (good), but radii are `rounded-xl`/`rounded-lg` mixed, and `bg-void/50` insets are used on all nested blocks.

### Bugs and errors
- Inferred: D1 state leakage and overwrite; D4 error conflation.
- Observed live: not reachable (no trace could be opened, see top). No real trace was opened.

### Performance notes
- Two queries per page (`trace`, `related`), fine. `SpanTimeline` sorts and computes on every render with no memo; trivially cheap at tens of spans.

### Suggested changes
`harden` (D1, D3, D4, D6), `layout` and `shape` (D2: sticky label bar, next-unlabeled), `adapt` (D5 stacked labels on mobile), `typeset` (D7: section hierarchy, wrap rules), `clarify` (D6 copy).

### Not exercised
Save Label (mutates real data), Export JSON (download), Report Issue (leaves localhost), Clear, label toggles (local state only but pointless without data).

### Live pass (2026-10-14), Trace detail

**No real trace exists, so the page was reached only with a bogus id** (`/traces/doesnotexist`, read-only GET). Result: the entire main area is the single line **"Trace not found."** with no heading, no Back link and no retry [live, CONFIRMED D4 dead end]. The title is still the static app title.

**Not checkable live (no trace):** D1 state leak and save-overwrite, D2, D3, D5, D6, D7; the 375px span timeline claim. For the span timeline claim, the arithmetic could not be verified with a real `SpanTimeline`; the sibling `CommandTimeline` at 375px measured a 101px bar against a computed 103px, which supports the method, but the 39px figure for `SpanTimeline` stays **unverified (computed)**.

---

## Command traces (`/command-traces`, `src/pages/CommandTraceListPage.tsx`)

- **Purpose and primary task:** see how each spoken command was handled (pattern match, LLM fallback, voice onboarding) and find mismatches.
- **Scores (source-based):** 21/40. Flexibility 1, Error recovery 1, Visibility 3 (live indicator is a plus), Match to real world 2, User control 2, Consistency 2, Error prevention 3, Recognition 3, Aesthetic 2, Help 2. Scored 1: Flexibility, Error recovery.
- **Cognitive-load failures: 3/8.** Fails: LLM Fallback and LLM Completed look identical in the list, a row of badges plus chips plus mono numbers per row, no grouping by outcome.

### Desktop findings (inferred)
- Live indicator (Wifi icon plus "Live") occupies its own row above the tiles (`:107-115`). Five tiles, filter row of Search, Outcome, Skill, From, To, Export Page, then a 6-column table (Timestamp, User Input, Outcome, Handler / Action, Confidence, Duration).

### Mobile findings (inferred; blocked)
- 6 columns, each `px-4`, means the table is well over 700px and scrolls inside its container (`:197`). Timestamp first and `whitespace-nowrap`, then input; outcome and handler are off-screen. Same card-per-row conversion as T-list recommended.

### Pain points and friction

- **C1. P1. Rows are mouse-only.** `:211-215`, same as T2. Fix with a `<Link>` on the input cell.
- **C2. P2. Two outcomes are indistinguishable in the list.** `:19-20` render both `llmFallback` and `llmCompleted` as "🤖 LLM", while the filter offers them separately (`:78-79`) and the detail page labels them "LLM Fallback" and "LLM Completed" (`CommandTraceDetailPage.tsx:25-28`). A user filtering for "LLM Fallback" cannot confirm the result by looking at rows. Fix: show the exact label in the list, or separate the colors. The emoji (`:17,20`) and `violet-500/15 text-violet-400` (`:20`) are off-token. Impeccable: `clarify`, `colorize`.
- **C3. P2. Live updates move the list under the cursor.** `:51-56` invalidate both queries on every SSE trace. On page 1 with no filters, a new command shifts every row down while the user is about to click. There is no "3 new" pill, no pause, and the update also re-fetches when the user is on page 5 with filters applied. The `Disconnected` state is dust-gray with no Retry (`:113`), unlike Activity; after 10 retries the stream is dead until a reload. Fix: queue new traces behind a "N new" button when the user has scrolled or filtered; add Retry.
- **C4. P2. Same filter-state, placeholder, label and error problems as the LLM list.** `:41-47` state-only filters (T1), no `keepPreviousData` (`:65-68`, T3), no debounce (`:137`), bare labels (`:131,144,152,160,169`), "Failed to load command traces." with no retry (`:193`), "No command traces found." with no clear-filters (`:235`).
- **C5. P3. Filter values mix casing.** `:77-80` send `CommandHandled`, `LlmFallback`, `LlmCompleted`, `Error`, while rows compare `commandHandled`, `llmFallback` (`:15-23`). If the server is case-insensitive this is fine; if not, filters silently return nothing. Unverified; check the API.
- **C6. P3. Ambiguous columns.** Confidence shows `-` for workflow rows (`:225`) with no explanation; "Handler / Action" shows `skill / action` and nothing for LLM rows, which is the very row type that needs an explanation ("No pattern matched"). Avg Duration is `${Math.round(...)}ms` (`:124`), no thousands separator.
- **C7. P3. Mobile and shell.** `StatCard` duplication (see T7); `Export Page` naming (T8).

### Bugs and errors
- Inferred: C2, C3. Observed live: none (not reachable).

### Performance notes
- Every SSE event triggers two refetches (list plus stats) for every open copy of the page (C3). With heavy traffic (a voice satellite chatter) that is a request storm; debounce the invalidation to 1s.

### Suggested changes
`harden` (C1, C3, C4), `clarify` and `colorize` (C2 exact labels, tokens), `adapt` (cards on mobile), `optimize` (C3 debounce).

### Not exercised
Filters, search, Export Page, row clicks (blocked).

### Live pass (2026-10-14), Command traces

**Data present: 5 command traces** (all LLM-handled; 0 Command, 0 Errors, avg 7568ms). Real utterances are not copied here.

**Desktop dark and light.** "Live" indicator on its own row above 5 tiles, filter row, 6-column table, "Page 1 of 1 (5 total)". 0 `<h1>`.

**Claims.**
- **C1 / T2 rows not keyboard reachable: CONFIRMED live.** `tbody tr` has `tabindex=-1` and contains no `a` or `button`; the page's only tab stops in `main` are the 8 filter and pagination controls. A keyboard user cannot open a trace.
- **T1 filters lost on Back: CONFIRMED live.** Typed "office" in Search (5 rows narrowed to 2), clicked a row, clicked "Back to Command Traces": URL `/command-traces` with no query string, search input empty, 5 rows again. Filters are not in the URL. (Same code path as `/traces`.)
- **C2 LLM Fallback vs LLM Completed identical in the list: CONFIRMED live.** All 5 rows show "🤖 LLM"; the detail page for the first says "LLM Completed" while its section reads "LLM FALLBACK".
- C6 confidence: CONFIRMED and worse than predicted. Rows with no pattern match show **0.0%**, not "-", and LLM-handled rows with a matched skill show 90.0%. A 0.0% confidence reads as a measured score instead of "no match".
- C5 filter casing: not checkable without mutating a filter that hits the API; the Outcome dropdown has 5 options and filtering was not applied.
- C3 live updates moving the list: no new traffic arrived during the pass; not checkable. "Live" badge showed connected.

**New findings.**
- **N-C1. P2.** Duration column prints raw floats: `9227.1738 ms`, `7513.1345 ms`, `5708.7903 ms` (4 decimals, no rounding). The tile above says "7568ms", rounded, so the same page uses two formats. Fix: round and format once (`1.2 s` or `9,227 ms`). `CommandTraceListPage.tsx:228` (and the detail header and LLM fallback card).
- **N-C2. P3.** Raw text on the detail page is `<Unknown1 />Zach's light.`: an upstream STT/markup artifact shown verbatim. Not a UI bug, but the page gives no hint that the leading tag is data.

**Mobile 375x812.** No page overflow (scrollWidth 375). Table is 714px wide inside a 341px scroll wrapper; column widths 179, 88, 95, 129, 113, 111. The **User Input column starts at x=196**, more than half the viewport, and the Outcome column is cut off at the right edge in the first screen [live, CONFIRMED]. Filters take about 400px of the first 812px before the table header. All controls are 38px (search, selects, date, Export Page) or 34px (Previous, Next).

**Keyboard.** Tab order in `main`: search, Outcome, Skill, From, To, Export Page, Previous, Next; focus ring is the 1px browser default.

**Network and console.** No errors, all 200.

---

## Command trace detail (`/command-traces/:id`, `src/pages/CommandTraceDetailPage.tsx`, `CommandTimeline.tsx`, `InputHighlight.tsx`)

- **Purpose and primary task:** see how the matcher parsed one utterance, what the skill did, and why it fell back to an LLM, to fix patterns or templates.
- **Scores (source-based):** 24/40. Visibility 3, Match to real world 3, User control 2, Consistency 3, Error prevention 3, Recognition 3, Flexibility 1 (no next/previous trace), Aesthetic 3, Error recovery 1 (dead-end not-found), Help 2. Scored 1: Flexibility, Error recovery.
- **Cognitive-load failures: 3/8.** Fails: the response is last, nine optional cards stack with the same visual weight, the timing waterfall is also last.

### Desktop findings (inferred)
- Order: back and actions, header facts, Input (raw and highlighted matched text), optional Voice onboarding card, Pattern Match with a confidence bar, Skill Execution, Tool Calls, Response Template with token chips, LLM Fallback with a link to the LLM trace, Timing Waterfall, collapsible Request Context, Response. Controlled `<details>` use is accessible (`:412,436`). This is the better-organised of the four detail and list pages.

### Mobile findings (inferred; computed)
- `CommandTimeline`: label 128px (`:70`) + value 64px (`:80`) + gaps 16 + paddings 64: bar is about 103px (computed), legible but tight; child rows use 112px labels (`:95`) and 11px text. Better than SpanTimeline, still tiny.
- Header facts wrap with `gap-6` (`:106`); IDs use `break-all` in `ContextField` (`:486`), good. Raw template and captured values wrap in mono; fine.
- Three action buttons in the top row as in the LLM detail page.

### Pain points and friction

- **CD1. P2. The answer is at the bottom, the plumbing is on top.** `trace.responseText` is the last card (`:471-477`), after Pattern Match, Execution, Tool Calls, Template, LLM, Timeline and Context. Raw text, response and outcome are the 80% question; put "Heard" and "Said" at the top as a two-line summary and move internals below. The same response text can appear up to three times (`execution.responseText` `:266-270`, `templateRender.renderedText` `:379-381`, `trace.responseText`). Impeccable: `layout`, `distill`.
- **CD2. P2. Timing waterfall is a construct, not a measurement, and is misaligned.** `CommandTimeline.tsx:49-65` places phases sequentially by cumulative duration, not by recorded start times; tool-call children are laid end to end from the execution start (`:88-93`), which is wrong for parallel calls. The axis uses `pl-36` (144px, `:120`) but the label column plus gap is 136px (`w-32` + `gap-2`, `:69-70`), so the axis is 8px right of the bars. `bg-sky-400/60` (`:28`) is off-token and the 3-color phases have no legend. Fix: use real offsets if the API has them, otherwise label it "Estimated sequence". Impeccable: `polish`, `typeset`.
- **CD3. P2. Not-found conflation and dead end.** `:72` "Command trace not found." for 404, 500 and network failure alike, no back link. `requestContext` is destructured with no null guard (`:74`): a trace without it throws and the root boundary (S6) wipes the shell.
- **CD4. P3. One-way navigation.** The LLM trace link exists (`:398-408`) but the LLM trace page has no link back to its command trace, and neither page offers previous/next. "Back to Command Traces" resets filters (`:81`, T1).
- **CD5. P3. Emoji and badge consistency.** `:24,26,28` emoji in outcome badges, `violet-500/15` (`:26-28`), `emerald`-free but `bg-dust/20` (`:334`); the 2.5 / 1 padding here (`px-2.5 py-1`) differs from the list badges (`px-2 py-0.5`).
- **CD6. P3. Mouse-only details.** Template token replacements appear only in `title` (`:352`); the Replaced Tokens list (`:363-376`) covers it, so this is minor. Duplicated fields: Conversation ID and Area in the header (`:111-122`) and again in Request Context (`:443-451`); `requestContext.speakerId` and `trace.speakerId` both shown (`:458-466`).
- **CD7. P3. Report Issue leaks raw text.** `:93-100` builds a GitHub URL with raw and normalized text and the response (`traceExport.ts:112-130`), same concern as D6. Voice transcripts are more personal than typed prompts.

### Bugs and errors
- Inferred: CD2 axis offset, CD3 crash path. Observed live: none.

### Performance notes
- One query; light. `details` state mirrored in React (`:58-59`) is redundant with the native element but harmless.

### Suggested changes
`layout` and `distill` (CD1: summary header, collapse internals), `polish` (CD2), `harden` (CD3, CD7), `adapt` (stack timeline labels above bars on `<md`).

### Not exercised
Export JSON, Report Issue, View LLM Trace navigation (blocked: no real trace could be opened).

### Live pass (2026-10-14), Command trace detail

Opened a real trace (navigation only; Export JSON and Report Issue not clicked).

**Desktop dark and light.** Header facts card (timestamp, conversation id, area, duration, outcome), Input card (raw text, highlighted text, "No pattern match"), "LLM FALLBACK" card with duration and a collapsed `Prompt` summary, Timing Waterfall, collapsed Request Context. This trace has **no Response card at all** (LLM Completed with no response text recorded), so the primary question ("what did it say") is unanswerable from the page [live, new, P2]. No `<h1>`. Both themes readable.

**Mobile 375x812.** No overflow. Action row: Back (137x40), Export JSON and Report Issue wrap to two lines and are 93x54 each; fine. Waterfall: **bar track is 101px wide** (computed 103px, CONFIRMED within 2px); the 0.1ms Pattern Match bar is 1px and the 9227ms "Other" bar fills it. Axis labels 0ms / 4614ms / 9227ms fit. `Prompt` summary is 20px tall.

**Claims.**
- CD1 response last: not checkable (no response card on this trace); order of the other cards matches source.
- CD2 waterfall: the live waterfall has two bars, "Pattern Match 0.1ms" and "Other 9227.1ms", where "Other" is the whole LLM call. A single opaque "Other" bar makes the chart say nothing; label it "LLM fallback". The `pl-36` axis offset was not measured.
- CD3 not-found dead end: **CONFIRMED live**. `/command-traces/doesnotexist` renders only "Command trace not found." with no heading and no link back. The null-`requestContext` crash was not reproducible with real data.
- CD5 emoji badges: CONFIRMED (🤖 in the outcome badge).
- Duration on the header and card shows the raw `9227.1738 ms` (see N-C1).

---

## Exports (`/exports`, `src/pages/ExportPage.tsx`)

- **Purpose and primary task:** build a filtered dataset of labeled conversation traces and download it for evaluation or fine-tuning.
- **Scores (source-based):** 22/40. Visibility 2 (no count preview, stale processing state), Match to real world 2, User control 2, Consistency 2, Error prevention 2, Recognition 3, Flexibility 2, Aesthetic 3, Error recovery 2, Help 2. Nothing at 1 or below.
- **Cognitive-load failures: 3/8.** Fails: no explanation of what an export contains or what "corrections" are, a form with five inputs and no summary of effect, history rows with a terse "Filter" column.

### Desktop findings (inferred)
- `<h1>` "Dataset Exports" with no description, a "Generate Export" card (Label Filter, From, To, Agent, "Include Corrections", amber button) and "Export History" (table: Timestamp, Filter, Records, Size, Status, Download).

### Mobile findings (inferred)
- The form wraps into a stack; `CustomSelect` triggers about 38px. The history table has 6 columns and scrolls inside its container (`:169`); the Download link is the last column, off-screen. "Generate Export" is about 44px (`py-2.5`), acceptable.

### Pain points and friction

- **E1. P2. "Processing…" never updates.** The history query has no `refetchInterval` (`:43-46`) and `refetchOnWindowFocus` is off (`main.tsx:10`), so an incomplete row (`:199`) spins until the user reloads. The mutation only invalidates once, on success (`:72-74`). Fix: `refetchInterval` of 2s while any row has `isComplete: false`.
- **E2. P2. Generate gives no preview and thin feedback.** No count of matching traces (stats are already loaded at `:48-51`), so Alex can create an empty export. The success line "Export created successfully." (`:154-156`) does not say how many records or link to the new row; it stays on screen forever. The error line hides the server's message (`:151-153` vs `api.ts:107` which includes it). "Include Corrections" is unexplained (`:133-141`). Fix: "Will export 214 traces (38 with corrections)", then a toast and a highlighted history row. Impeccable: `clarify`, `harden`.
- **E3. P2. Labels not bound, page chrome inconsistent.** `:100,108,117,126` bare labels (audit item 4); the checkbox is the only properly labeled control (`:133`). The title has no subtitle (Activity has one), and "Generate Export" appears as both card title and button (`:96,149`). Inputs use `px-4` (`:32`) where the trace filters use `px-3` (`TraceListPage.tsx:30`).
- **E4. P3. Scope confusion.** "Exports" in the nav, "Export Page" on both list pages (a client-side dump of 20 rows) and "Export JSON" on both detail pages are three different features with one name. Only LLM conversation traces can be exported server-side; command traces have no equivalent. Fix: name this page "Training datasets" and say so in the h1 subtitle, and link to it from the list pages. Impeccable: `clarify`.
- **E5. P3. History table.** The "Filter" column (`:187-192`) omits whether corrections were included; no relative time, no delete or expiry, no sort. A download is a plain `<a href>` (`:204-209`): fine, but there is no file type or size next to the action, and the link text "Download" is repeated per row for screen readers (add `aria-label="Download export from 14:02"`).

### Bugs and errors
- Inferred: E1 stale spinner. Observed live: none (not reachable).

### Performance notes
- Light. The stats query is shared with the trace list (`['stats']`), so it is cached across pages; good.

### Suggested changes
`harden` (E1, E2), `clarify` (E2, E4, E5), `polish` (E3 chrome alignment).

### Not exercised
Generate Export (creates a real export), Download links (download), the filter dropdowns and date pickers (blocked).

### Live pass (2026-10-14), Exports

**Desktop.** `<h1>` "Dataset Exports" (the only data page with an `h1` besides Activity), Generate Export card, Export History reading "No exports yet." Nothing clicked. Generate Export is 40px tall.

**Mobile 375x812.** No overflow. Filters stack in 2+2 rows; Label select 81px wide with the From label crowding it; date inputs 167px; Generate Export 175x40. The **Include Corrections checkbox is 13x13px** [live, new, P3]; the label wraps it but the text is small. History table not seen (empty).

**Claims.** E3 bare labels: **CONFIRMED live** (4 of 5 `label`s have no `for` and do not wrap their control; only the checkbox label does). E2 no count preview: CONFIRMED by absence (with 0 LLM traces, Generate Export is enabled, so an empty export is possible). E1 stale spinner, E4, E5: not checkable (no history rows, and export is not to be triggered).

---

## Cross-page observations (this group)

1. **One pattern is repeated four times and carries the same defects:** list page = stat tiles plus filter row plus table plus Previous/Next. Local `useState` filters, no `keepPreviousData`, no debounce, mouse-only rows, bare labels, dead-end errors, "Back" that resets. Fixing it once as a `FilterableTable` with URL state, a `<Link>` row, `ErrorState`, `EmptyState` and a mobile card layout covers Traces, Command traces and (with a different body) Exports history.
2. **No connection-economy design.** Every tab opens streams and pollers (S1). The product targets a Jetson on a LAN; a person with two or three dashboard tabs open is the normal case.
3. **Status is communicated by color and emoji, not by text or token.** Amber means "metric", "LLM" and "warning" on different pages; emoji appear in `ActivityPage.tsx:44-45,100,116-123`, `CommandTraceListPage.tsx:17,20`, `CommandTraceDetailPage.tsx:24-28`.
4. **Time is shown as long locale strings and raw ms everywhere** (`TraceListPage.tsx:23,202`, `CommandTraceListPage.tsx:29,228`, `ExportPage.tsx:10`). No relative time, no units formatting, no timezone cue.
5. **No headings or titles on data pages**, a static `<title>`, and a mobile top bar that omits the page name: orientation depends entirely on the sidebar.
6. **Privacy-first product, but "Report Issue" exports raw utterances to github.com** with no warning.
7. **Mobile was designed for navigation, not for data.** The shell is responsive; wide tables, the 420px pan-and-zoom mesh and the 192px-label timeline were not adapted.

## Top 5 for this group

1. **S1 (P1):** each tab holds two or more permanent connections plus a 2s task poll; with 3 or more tabs open the app stops loading. Observed live: my tab never finished loading. Fix `TaskTracker.tsx:67,126-133` first.
2. **D1 (P1):** `TraceDetailPage.tsx:90-110`, label, notes and success state leak between related traces, existing notes are not loaded, and Save may erase them. Add `key={id}`, hydrate from `trace.label`.
3. **T1 (P1):** list filters and page are lost on Back and cannot be shared. Move to `useSearchParams`, make rows real links (T2, C1).
4. **A1 and A2 (P1 and P2):** Activity swallows fetch failures and shows "Disconnected" on first paint, so an outage and a fresh install look identical (`ActivityPage.tsx:193-196`, `useActivityStream.ts:19`).
5. **S2 (P1):** mobile drawer has no Esc, focus move, trap, return or `aria-expanded` (`App.tsx:211-227,297-309`). Reuse the `<dialog>` pattern.

Severity counts across all findings above: P0 0, P1 7 (S1, S2, A1, T1, T2, D1, C1), P2 30, P3 21.

## Live pass summary

Pass date 2026-10-14. Single tab, no other connections; all pages loaded. Viewports 1440x900 (dark and light), 1024x768, 768x1024, 640x900, 375x812.

**Counts.** CONFIRMED live: 19 (S1 poll half, S2 drawer sub-claims counted once, S3, S4 unknown-URL rewrite, S5, S9a/c/d, T5 empty state, T6, C1/T2, T1, C2, C6, CD3, D4, E3, A3 error-rate tile, A4 mesh clipping, A4 scroll trap). REFUTED or corrected: 3 ("Disconnected" lasts about 40ms on a healthy load, not the whole load; "mesh not keyboard reachable" is wrong, 20 unlabeled tab stops instead; "no focus-visible" is partly wrong, the 1px browser default ring shows). NEW: 8 (N-S1, N-S2, N-S3, N-C1, N-C2, Traces "Page 1 of 0", Activity "Total Requests 0 vs LLM Fallback 5", command trace with no Response card).

**Claims asked about.**
- "Disconnected" on first paint: CONFIRMED, but a 40ms flash on a healthy load.
- Filters lost on Back: CONFIRMED live on Command traces (same code on Traces).
- Rows not keyboard reachable: CONFIRMED live (`tabindex=-1`, no link).
- Span timeline bar width at 375px: NOT CHECKABLE (no LLM traces exist). Sibling `CommandTimeline` measured 101px vs computed 103px.
- Nav items visible at 1440x900: **15 of 22** CONFIRMED (12 at 1024x768, about 13 at 375x812).
- `/does-not-exist`: lands on `/` (Activity) with the URL rewritten and no message, CONFIRMED.

**Top live-only findings.** (1) Mobile drawer fails every a11y check at once: no Esc, no focus move, Tab escapes into the page, no `aria-expanded`, snap-close. (2) Activity mesh at 375px: 7 of 10 nodes off-screen, wheel and drag trapped, plus 20 unlabeled tab stops. (3) Raw 4-decimal durations (`9227.1738 ms`) in the command trace list and detail, next to a rounded tile.

**Remaining unverified.** All LLM-trace detail behavior (D1 to D7, span timeline), Traces list rows, pagination, Export history, A1 error states, C3 live-update shifting, S6 to S8 and S11 to S15, the three-tab connection freeze, selecting an option in `CustomSelect` (focus after select).
