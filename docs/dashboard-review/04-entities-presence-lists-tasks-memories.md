# Dashboard review 04: Entity Locations, Presence, Lists, Tasks, User Memories

Reviewer scope: `src/pages/EntityLocationPage.tsx`, `PresencePage.tsx`, `ListsPage.tsx`, `TasksPage.tsx`, `UserMemoriesPage.tsx`, plus `components/EntityMultiSelect.tsx`, `AutoAssignPreviewModal.tsx`, `EntityOnboardingBanner.tsx`. Paths below are relative to `lucia-dashboard/src`.

## Method and a hard limitation

- **Source: fully read** for every file above, plus the relevant `api.ts` and `types.ts` sections. `impeccable detect --json` on all 8 files returned `[]` (no findings), so everything below comes from reading, not the detector.
- **Live: NOT achieved.** I opened my own tab (`tab-5`) at 1440x900 dark and loaded `/entity-location`. The shell rendered, but every `/api/*` call stayed pending. Observed:
  - `/api/entity-location`, `/floors`, `/domains`, `/api/presence/sensors`, `/api/tasks/stats` and even `/api/auth/status` all hit my 8-20 s abort timeout from inside the page.
  - The page stayed on "Loading floors…" and the summary cards never rendered.
  - Navigating the tab to `/presence` timed out after 300 s, and the pane later showed a blank dark page.
  - `curl` from the shell to the same server answered in 3-6 ms (`/` 200, `/api/auth/status` 200, `/api/presence/sensors` 401 without a cookie), so the server was up.
  - Seven tabs (`seed`, `tab-2`, `3`, `4`, `5`, `7`, `8`) shared the origin, and the other reviewers' tabs kept getting fronted, which also starved screenshots ("tab is not fronted").
  - Likely cause (inferred, not proven): the browser's per-origin HTTP/1.1 connection cap (6) was exhausted by long-lived streams. The dashboard opens two per tab: `/api/entity-location/embedding-progress/live` (`EntityLocationPage.tsx:289`) and `/api/tasks/background/stream` (global tracker). The log also showed `/api/tasks/background` fired about 8 times in the first seconds.
  - All "observed live" claims below are therefore limited to this. **Everything else is "inferred from source".** No desktop/mobile/tablet/light-theme screenshots were obtained, and no per-page console or network review was possible beyond the above. The only console output was Vite debug and the React DevTools notice.
- Nothing was clicked that mutates state. The tab was closed.

---

### Entity Locations (`/entity-location`, `src/pages/EntityLocationPage.tsx`)

- **Purpose and primary task:** Inspect the cached HA floor/area/entity registry and its embeddings, and control which agents can see which entities (per-row, bulk, and auto-assign). The real work is on the Entities tab.
- **Scores:** Nielsen **21/40**.
  - H1 visibility of status 2. Embedding progress is shown twice, but write errors surface far from the action.
  - H3 user control **1**: bulk actions apply immediately with no undo and no preview (`:781-821`).
  - H4 consistency **1**: "Smart Assign" means "open a preview" in the header but "apply now" in the bulk bar. "Clear All Filters" means "delete saved config".
  - H5 error prevention **1**: the double-write race at `:741` and the one-click destructive header buttons.
  - H7 flexibility 2, H8 minimalist **1** (five stacked blocks before the table), H9 error recovery 2, H10 help 2, H2 match 2, H6 recognition 3.
  - Cognitive-load failures: **5/8**. Those are: too many simultaneous choices in the header, the filter panel and the bulk bar; competing primary actions; unclear selection scope; jargon (Embedding, Domain pulse, Invalidate, Exposed); and hidden state (the selection is silently cleared on filter, page or tab change).
- **Desktop findings** (inferred from source; live load never completed):
  - Above the table there are, in order: header with 5 colored buttons (`:954-1017`), 4 summary cards (`:1022`), an embedding-progress panel (`:1058`), the onboarding banner (`:1084`), the tab bar, then a filter panel (`:1203-1307`) and the bulk bar. On a 900px-high viewport the table starts below the fold.
  - The default tab is **Floors** (`:103`), a short read-only list, not the Entities work tab.
  - Embedding counts appear twice: card at `:1044` and panel at `:1067/1079`.
- **Mobile findings** (inferred; 375/768 not viewable live):
  - Header button group is `flex items-center gap-2` with no wrap (`:954`) and holds 5 buttons of roughly 110-150 px each, so it overflows 375px. The parent wraps but the inner group does not.
  - Tab bar: 4 tabs at `px-4` plus icon is about 380 px (`:1101-1115`), marginal at 375.
  - Search tab row `:1363` is `flex items-center gap-3` with input, a fixed `w-40` domain box, a select and a button. It overflows.
  - Bulk bar `:706` is a non-wrapping flex row with 4 buttons plus a count. It overflows.
  - The 8-column entity table scrolls horizontally in `overflow-x-auto` (`:840`); Agents and Actions columns are off-screen at first.
  - The per-row agent dropdown (`:638-640`) is `absolute` inside that `overflow-x-auto` container. That makes overflow-y auto too, so the menu will be clipped or force an inner scroll on the last rows, worst on short tables and on mobile.
  - Inputs are `text-sm` (14px), so iOS Safari zooms on focus.
  - Selection checkboxes are 16px (`h-4 w-4`) and row actions about 22px.

**Pain points and friction (ordered)**

1. **P0, bulk "Assign to Agent" fires two conflicting writes.** `:741` calls `handleBulkAssign(null)` under the comment "won't run, we handle inline". It does run. It sends `updateEntityAgents({id: null...})` (reset to All), clears the selection and closes the dropdown, while `:742` concurrently sends `updateEntityAgents(updates)`. The two requests race. The final server state for the selected entities may be "visible to all agents", silently wiping existing restrictions. `.then` has no `.catch` (`:742`), so failures are unhandled and unreported. Fix: delete `:741`, add `catch`, `await` in order, disable the bar while pending. Command: harden.
2. **P1, bulk "Assign to Agent" changes meaning for "All" entities.** `:735` does `getEntityAgents(id) ?? []` then appends. An entity currently visible to all agents becomes visible to only the one chosen agent. This is the opposite of the per-row dropdown, where null means all and ticking an agent keeps the others (`:620`). Fix: copy that behavior, and label the buttons "Add agent" / "Restrict to agent".
3. **P1, bulk "Smart Assign" has no preview or confirm.** `:782-815` fetches the global preview, then overwrites every selected entity with the preview result, or `[]` (hidden from all agents) if the entity is not in any group (`:795`). Same label as the header's preview-first Smart Assign (`:994`). No count, no confirm, no undo. Fix: reuse `AutoAssignPreviewModal` scoped to the selection, or rename "Re-run smart rules on N selected".
4. **P1, "Clear All Filters" is a destructive config reset named like a view filter.** `:973-981`, with `confirm()` at `:504`. It deletes every per-entity agent restriction. The entity tab's own "Reset" (`:1279`) clears view filters. Operators will confuse them. Fix: rename "Remove all agent restrictions (N)", move it into a danger area, and use a styled confirm that states the count.
5. **P1, one-click hide-everything beside routine actions.** "Start Clean" (`:997`) sits between "Smart Assign" and "Reload from HA", with a native `confirm()` (`:518`). It sets every entity to hidden and is duplicated in the onboarding card (`EntityOnboardingBanner.tsx:38-49`) styled as an equal-weight option. Fix: type-to-confirm or a modal that shows the entity count and consequences, and demote it to a "More actions" menu.
6. **P1, nested component definitions remount the table on every render.** `AgentBadges :602`, `AgentDropdown :615`, `BulkActionBar :702` and `EntityTable :836` are declared inside the page component. Every state change (each keystroke in the name filter, each selection toggle, each SSE progress tick during generation `:290`) creates new component types, so React unmounts and remounts up to 250 rows x 8 cells.
   - Effects: scroll position of the table container resets, focus is lost (a keyboard user cannot tick two checkboxes in a row), and a large page stutters while typing.
   - Fix: hoist to module scope with props (or `useMemo`ed rows), and use `React.memo` per row. Command: optimize.
7. **P1 [live for checkbox semantics and 16px size], selection is not accessible and is page-scoped.** The checkboxes are `<button>` with no `role="checkbox"`, `aria-checked` or label (`:846`, `:870`). The target is 16px and rows are not clickable to select. There is no shift-range select. Selection is wiped on page change, filter, tab switch (`:242`, `:1105`), so "select all 600 matching" is impossible. The count says "N selected" with no "of M". Fix: real `<input type=checkbox>` with `aria-label`, row click, and a "Select all M matching" banner as in Gmail.
8. **P1, the bulk bar can scroll away and leaves ghost space.**
   - It is not sticky, so with 250 rows the bar is off-screen after selecting at the bottom.
   - When count is 0 it stays in the DOM as `invisible h-0` (`:711`). Inside `space-y-6` the margin still applies, leaving a dead 24 px gap, and it is rendered in the DOM in both tabs.
   - Fix: `sticky bottom-4` (mobile: bottom bar) and mount only when count > 0.
9. **P1, auto-assign modal hides errors and ignores dialog basics.** `AutoAssignPreviewModal.tsx:25-26` has no `role="dialog"` or `aria-modal`, no Esc, and no focus trap (audit P1#2). Backdrop click calls `onClose` (`:25`), so a stray click during Apply dismisses it while the request continues. An Apply failure sets the page-level error (`EntityLocationPage.tsx:560`) behind the modal, so the user sees nothing. The close button has no `aria-label` (`:40`). Fix: shared `<dialog>`-based Modal, inline error in the footer.
10. **P2, the preview does not show what changes.** It lists proposed groups and a 160 px nested scroll of IDs (`:83`, inside a `max-h-[60vh]` scroller `:46`), which is double nested scrolling, not searchable, and the excluded list is only a sample. There is no "N will change vs current". Apply label ("Apply Assignment") does not say it replaces existing per-entity settings. Fix: show before/after counts and a searchable list; label "Apply to N entities".
11. **P2, filters mix instant and apply-on-click.** The name and location text inputs, the domain select, the agent select and the Domain-pulse chips only set local state (`:1211`, `:1229`, `:1239`, `:1257`). Nothing happens until "Apply filters" or Enter. The page-size select applies instantly (`:1298`). Chips look like instant toggles but are not. Fix: apply on change with a 250 ms debounce for text, drop the Apply button, show active-filter chips with "Clear".
12. **P2, status and errors are far from the action.** One `{error && <p className="text-rose">}` (`:1118`) appears below the tab bar, with no `role="alert"`. Per-row agent toggles (`:449-470`) persist immediately and silently: no success cue, and a failure shows only at the top.
13. **P2, Search tab is weaker than the Entities tab.** Free-text "Domain filter" input instead of the available-domains list (`:1375`), no pagination or result cap (`:1414`), and the agent select shares `impersonateAgent` with the Entities tab (`:1384`) and silently rewrites the domain box (`:1390`). It duplicates the Entities filter with a different mechanism ("semantic" vs "contains") that is never explained.
14. **P2, no way from Areas to its entities.** The Areas row shows `Floor` as the raw `floorId` instead of the floor name (`:1180`), and the entity-count badge (`:1181`) is not a link. Fix: make it a link that sets the location filter and switches to Entities.
15. **P2, jargon for an HA operator.** "Embedding", "Domain pulse" (`:1253`), "Invalidate and reload" (`:363`), "Exposed Only/All Entities" button showing current state vs action (`:969`), eye icons for the dropdown "All/No Agents". The trash icon on each row reads "Remove entity from cache" but looks like deleting from HA (`:919`). Embedding column shows a red "Missing" badge per row (`:894`) with only a per-row regenerate and no "Regenerate missing" bulk action.
16. **P2, native confirm at `:363`, `:407`, `:504`, `:518`** (audit P1#3). The Reload confirm does not mention the cost (re-embedding).
17. **P2, small and unlabeled controls.** Row icon buttons `p-1` + 14px icon with `title` only (`:901-920`). `focus:outline-none` on inputs (`:1213` etc., audit P1#4). Filter inputs are unlabeled, placeholder only. `focus:border-amber` is the only focus cue.
18. **P3, per-row dropdown.** The trigger `mousedown` is "outside" `dropdownRef`, so clicking the trigger while open closes then reopens (`:145-153` vs `:630`). No Esc, no `aria-expanded`/`menu` roles. Layout shifts as agent badge chips wrap.
19. **P3, `EntityOnboardingBanner.tsx:12`** reads `localStorage` with no try/catch in a state initializer; it throws in private windows or with blocked storage and crashes the page (the same call is unguarded at `EntityLocationPage.tsx:525,558`). Dismiss button has `title` only (`:23`).
20. **P3, other.** Raw `text-sky-400`/`bg-sky-400/15` throughout (`:86,720,988`; audit P2#8). `rounded-md`/`rounded-lg` vs the 12px spec. The "Domain pulse" chip wall is unbounded (all domains).

**Bugs and errors:**
- The race at `:741` (P0).
- `useEffect` deps warnings at `:286` and `:319` (known, audit P2#12).
- `EntityMultiSelect.tsx:39,69` set-state-in-effect lint errors (known).
- Live: every `/api` call pending (see the limitation note above).
- `handleRemoveEntity` page-math at `:415` uses stale `visibleEntities` (minor).

**Performance notes:**
- Server-side pagination is good: 25/50/100/250.
- The remount issue (#6) is the main cost, worst at 250 rows with a progress SSE ticking.
- Loading replaces the whole table with a spinner on every filter or page change (`:1311`), which jumps the layout and loses scroll.
- The effect at `:59` fan-out: each mount issues 6 requests (summary, progress, visibility, agents, domains, tab data).
- The EventSource stays open for the lifetime of the page.

**Suggested changes:**
- `harden`: fix the P0, add `catch`, replace confirms with a promise-based `useConfirm`, and add a count and consequence line.
- `optimize`: hoist `EntityTable`/`BulkActionBar`/`AgentDropdown`, memoize rows, and keep previous data while loading (React Query `placeholderData`).
- `layout`: default to Entities; collapse the summary cards, embedding panel and banner into a single status strip (progress bar only while `isGenerationRunning`); move Reload, Start Clean and Remove-restrictions into a "Maintenance" menu; use a sticky bulk bar.
- `adapt`: on mobile render rows as cards (name, area, agents chip, overflow menu), a bottom-sheet agent picker, and a filter sheet.
- `clarify`: terminology ("Agent access", "Exposed to Assist" per HA wording, "Semantic index" for embeddings).
- `onboard`: keep the banner, but make Smart Assign the primary (it is preview-first) and Start Clean a secondary text link.

**Not exercised:** Auto-assign (all variants), Start Clean, Clear All Filters, Reload from HA, Exposed Only toggle, per-row and bulk agent assignment, regenerate and remove entity, any selection. Live rendering never loaded.

#### Live pass (2026-10-14)

**Data caveat (applies to all five pages):** the live instance has an EMPTY entity cache (0 floors, 0 areas, 0 entities, 0 embeddings, `[]` from `/api/entity-location/floors`, `/domains`, `/api/presence/sensors`). Reloading from HA would populate it but is a mutation, so I did not. Consequently nothing that needs rows could be exercised: row selection, the bulk bar, row remounting, large-list typing performance, the agent dropdown, the auto-assign modal. These are "not exercised", not refuted. Home Assistant also appears unreachable from the server (see Lists).

- **Desktop dark (1440x900):** Header with 5 buttons, 4 summary cards, embedding panel, tab bar, then content. With an empty cache the first viewport shows "No floors cached." and no onboarding banner (`EntityOnboardingBanner` is hidden at `entityCount` 0, `:1084-1086`). So the page that most needs a "load your entities" prompt gives none, while "Clear All Filters" and "Start Clean" stay fully enabled on zero data. NEW P2. Screens at the Entities tab: filter panel, 3 unlabeled selects/inputs, empty 8-column table, "Showing 0-0 of 0".
- **Desktop light:** no raw-color leaks or unreadable text. Muted card labels and the empty-state text measure about 3.6:1 (estimated with a script against the card background; below 4.5:1 for 12px text). The header buttons keep their tinted fills and stay legible.
- **Mobile 375x812 (NEW P1, CONFIRMED live):** the non-wrapping header button group (`:954`) is 518px wide. It stretches the whole `main` (`overflow-auto`, scrollWidth 534 vs 375), so the page scrolls sideways as a unit: "Start Clean" and "Reload from HA" sit off-screen, and the filter panel inputs run to x=414 (viewport 375), so the right edge of every filter input, the summary cards and the table card are cut off unless the user scrolls horizontally. `document.documentElement.scrollWidth` is 375, so a page-level overflow check misses it; the scroll container is `main`. "Clear All Filters" and "Reload from HA" wrap to 3 lines and 76px tall. Tab bar: "Search" clipped at x=417. Filter inputs are 42px tall, Apply/Reset 38px, page-size select 38px, header buttons 56-76px.
- **Tablet 768x1024 (NEW P1):** the 256px sidebar stays docked, leaving a 512px main. The header group still overflows (Reload from HA ends at x=798 of 768), tab bar ends at 681. So the same overflow occurs at tablet, not just phone.
- **Hidden bulk bar (CONFIRMED live):** the bar exists in the DOM at count 0 as `invisible h-0` (1120x0 at 1440), with the 24px `space-y-6` gap visible between the filter panel and the table (observed as a larger gap than elsewhere). On mobile its 5 buttons are laid out at x=97..504 inside the hidden element; it is not focusable (`visibility:hidden`). The bar's buttons measure 44px tall (xs text 12px) when visible per layout; its Clear button is 28x28.
- **Checkboxes (CONFIRMED live):** header checkbox is `<button>` 16x16 with no role, no aria-checked, no label. Below both the 44px and 24px targets.
- **Unlabeled controls (CONFIRMED live):** both text inputs and all 3 selects have no aria-label and no associated label (`labels.length` 0). Text is 14px on mobile (iOS zoom risk, inferred).
- **Focus (PARTIALLY REFUTED):** buttons (tabs, header) show the browser's default 1px focus outline (amber border plus outline) so keyboard focus IS visible on buttons; inputs use `outline: none` and show only an amber border change (`focus-visible` true, border rgb(226,168,75)). Revise finding #17: "no focus-visible" is true for inputs only, and buttons rely on the unstyled UA ring.
- **Slow summary call (NEW P2, live-only):** the page's own `GET /api/entity-location` stayed pending for 15 s or more on three consecutive loads (summary cards and the status strip absent the whole time, no skeleton), while a manual `fetch` of the same URL returned in 8 ms the first time and 4.7 s later. Other entity-location calls on the same page returned in under 50 ms. The page gives no loading cue for the cards, so they pop in late and push the tab bar down by about 190px. In dev (Vite + StrictMode) every call is also issued twice.
- **Search tab:** renders an empty form (location input, domain box, agent select, disabled Search button). I did not run a search (it may call the embedding provider).
- **Not exercised live (needs rows):** the P0 bulk-assign race (said as asked: not exercised), nested-component remount and focus loss while typing in the table filters, 250-row performance, bulk bar at 375/768 with a selection, row dropdown clipping, auto-assign preview modal, selection-clearing on tab/filter change.
- **Console:** no errors from this page. Network: all calls 200; `/api/tasks/background` polled about 10 times in 16 s from the global tracker (NEW P3, noisy but cheap).

**Claims verified:** #7 checkbox semantics CONFIRMED live (16px, no role/label); #8 ghost bulk-bar space CONFIRMED live; #11 page-size applies instantly (select next to the unapplied filters) not checked; header/search/bulk overflow (#mobile bullets) CONFIRMED live for header and tab bar, filter panel stretched; the P0 race, #1-#6, #9, #10, #12-#16, #18-#20 not checkable (no rows, no mutation).

---

### Presence (`/presence`, `src/pages/PresencePage.tsx`)

- **Purpose and primary task:** See which areas are occupied and tune the sensors that drive presence detection (confidence, disable, remove override, re-scan).
- **Scores:** Nielsen **24/40**.
  - H2 match **1**: "Confidence" and "override" are undefined, and the page shows config but not live sensor state.
  - H1 status 2 (stale occupied data), H3 control 3, H4 consistency 2, H5 error prevention 2, H6 recognition 2, H7 2, H8 3, H9 3, H10 **1** (no explanation of confidence or what Disabled does).
  - Cognitive-load failures: **3/8** (unexplained jargon, hidden state, icon ambiguity).
- **Desktop findings** (inferred; also nothing loaded live):
  - Loading is a bare centered spinner (`:123`).
  - The page is calm: header, "Currently Occupied" chips (`:168`), a single filter box, then one card per area with sensor rows.
- **Mobile findings** (inferred):
  - Header `flex items-center justify-between` with no wrap (`:137`): title block plus two 40px-tall buttons ("Enabled", "Re-scan") squeeze at 375.
  - Sensor row `:223` packs `flex-1` entity ID + `w-28` select + eye + trash with `gap-4`. At 343px the ID gets about 110-130px and `truncate` (`:226`) cuts it with no `title`, so the only identity of the row cannot be read.
  - Icon buttons are `p-1.5` + 16px, about 28px.

**Pain points and friction (ordered)**

1. **P1, entity ID is the only identity and gets truncated.** `:226`. There is no friendly name, device or last-seen/state. Fix: show friendly name as the title with the ID as muted subtext, add a `title`, and wrap on mobile (stack the controls below the ID).
2. **P2, the page shows config, not state.** Occupied areas come from a one-time `loadData` on mount (`:53`); nothing polls or streams. The sensor rows do not show current on/off, last-changed, or which sensor made an area occupied. For "why is the bedroom marked occupied?", the page cannot answer. Fix: poll every 10-15 s while visible or reuse SSE, add a state dot + "changed 3 m ago".
3. **P2, "Confidence" is never explained.** Levels Highest..None (`:12-18`) are colored text, and `Highest` (green-400) vs `High` (emerald-400) are near-identical (raw palette, audit P2#8; `:13-14`). Fix: help text ("how strongly this sensor's activation is trusted when combining sensors") and a legend. `None` vs Disabled overlap is unclear.
4. **P2, the global Enabled/Disabled control is a labeled-by-state button.** `:145-155`: no `aria-pressed`/`role="switch"`, and the label shows state while the icon (Shield/ShieldOff) and color imply an action. One click disables presence detection for the whole house with no confirm and no explanation of the effect. A `ToggleSwitch` component already exists in the repo. Fix: use it, with the copy "Presence detection: On".
5. **P2, empty state blames the wrong thing.** `:202-207` shows "No presence sensors found. Click Re-scan" even when the filter hides all rows. Fix: distinguish "no match for 'x'" with a Clear link.
6. **P2, filter input has no label or aria-label** (`:192-198`), no clear button, no result count.
7. **P2, eye icon semantics are inverted.** An open Eye (sage) means "enabled, click to disable" (`:257`), the opposite of the "show" convention. The row also dims when disabled (`:223`), good, but the only cue for the toggle is `title`. Fix: use a switch with a label.
8. **P2, native `confirm` at `:99`** (audit P1#3), and the wording is inconsistent: the trash says "Remove override" (`:264`), the confirm says "Remove sensor mapping", the toast says "Sensor mapping removed". It never says what happens next (revert to auto-detected? reappear on re-scan?).
9. **P2, header button group does not wrap on mobile** (`:137,144`).
10. **P3, h1 style** drifts: `text-2xl font-semibold` without `font-display` (`:139`). Raw colors `:13-16`.

**Bugs and errors:** `(s.areaName ?? s.areaId).toLowerCase()` at `:112` is safe only while `areaId` is always a non-empty string (type says `string`); an unassigned sensor with null at runtime would throw and blank the page. Group key at `:116` has the same assumption. Live: not observable.

**Performance notes:** Fine at tens of sensors; no virtualization needed. Each `changeConfidence` persists immediately and replaces the row.

**Suggested changes:** `clarify` (confidence help, naming), `adapt` (stacked mobile row, wrapping header), `harden` (confirm, filter empty state, null-safe grouping), `polish` (ToggleSwitch, token colors), and live refresh via `optimize`.

**Not exercised:** Enable/Disable toggle, Re-scan, confidence changes, sensor enable/disable, remove override, all of which persist.

#### Live pass (2026-10-14)

- **Data caveat:** `/api/presence/sensors` returns `[]`, so the page shows only its empty state. Identifier truncation (#1), confidence select, row layout, and occupied chips could not be observed: "not exercised".
- **Desktop dark/light:** header "Presence Detection", "0 sensors mapped · 0 areas occupied", an "Enabled" button and "Re-scan", a filter input, then the empty card ("No presence sensors found. Click Re-scan to discover sensors from Home Assistant"). Clean in both themes, no contrast problems seen. The filter input is shown even with zero sensors (NEW P3: nothing to filter). #5 (empty state blames the wrong thing) is not checkable, but the zero-sensor message itself is correct.
- **Mobile 375x812:** no overflow (main 375/375). The header squeezes as predicted (CONFIRMED live): the title wraps to 2 lines ("Presence / Detection"), the subtitle to 2 lines, and "Re-scan" wraps to 2 lines (87x60px); "Enabled" is 109x40; filter input 343x42. No button reaches 44px height except the filter input at 42 (all near). This is a layout squeeze, not a clip.
- **Network/console:** no errors, calls under 20 ms.
- **Claims:** #9 header does not wrap on mobile CONFIRMED live (it compresses and wraps text instead of the group); #6 filter input unlabeled: `placeholder` only (seen in DOM, no `aria-label`) CONFIRMED live; #1, #2, #3, #4, #7, #8 not checkable.

---

### Lists (`/lists`, `src/pages/ListsPage.tsx`)

- **Purpose and primary task:** View and edit the HA shopping list and todo lists (add, complete, remove).
- **Scores:** Nielsen **24/40**.
  - H1 2, H2 3, H3 2 (no undo or "uncheck"), H4 2, H5 3, H6 3, H7 **1** (no bulk, no "clear completed", no keyboard focus return, items by name only), H8 3, H9 2, H10 2.
  - Cognitive-load failures: **2/8**.
- **Desktop findings** (inferred): two tabs (Shopping / To-Do), a single add input + button, and a stack of rows with a check and a trash icon. Visually simple, 4px-radius cards (`:196,242`) against the 12px spec.
- **Mobile findings** (inferred): the todo toolbar wraps (`:247`), which is fine. Check/trash buttons are `p-1` with 16/14px icons (about 24/22px), adjacent to the row edge: weak targets for a page likely used on a phone in a shop.

**Pain points and friction (ordered)**

1. **P1 [live for the add button], icon-only buttons have no accessible name.** `:219-233`, `:277-291`, and the add buttons `:205-211`, `:262-268` have no `aria-label` or `title`. Sam cannot tell Check from Trash. Fix: `aria-label={\`Complete ${name}\`}` etc.
2. **P2, completion is not reversible and not distinguishable.**
   - Shopping: the check button stays enabled on completed rows and calls complete again (`:219-225`); there is no "uncheck". Todo disables it when completed (`:279`). Completed items stay mixed in HA order, with no "Clear completed" and no active/done counts.
   - Remove has no confirm or undo (acceptable for list items, but an undo toast would be better).
3. **P2, operations key on item name, not id.** `completeShoppingItem(name)` / `removeShoppingItem(name)` (`:99,111`; `api.ts:568-584`). Duplicate names are ambiguous, and `todo` uses `summary` (`:123,136`).
4. **P2, list switching refetches everything and shows stale items.** `loadTodoEntities` depends on `selectedTodoId` (`:39-47`), so the effect at `:59-63` re-runs on every list change: it reloads shopping + entities, resets `error`, and sets `loading`. `todoItems` keep showing the previous list's rows under the new selection until the fetch lands (`:49-57`, no loading state).
5. **P2, every action disables all buttons** (`disabled={actionId !== null}`, `:221,229,279,287`) and refetches the whole list, with no optimistic update. Rapid checking in a store feels sluggish.
6. **P2, inputs are unlabeled** (placeholder only, `:198,255`), and `<label>List:</label>` (`:248`) is not associated with the `CustomSelect`. `newItem` is one shared state across both tabs and lists (`:25`), so draft text leaks between tabs.
7. **P2, error banner:** dismissible, but no `role="alert"`. Messages are `Failed to fetch shopping list: ${statusText}` (`api.ts:555`) and so read "…: " when `statusText` is empty. When HA's shopping_list integration is not configured the user sees a raw failure, not a setup hint (the todo empty state at `:244` is good; shopping has none).
8. **P3, naming:** nav says "Lists" (audit P2#6), the page says "Shopping & To-Do Lists", the copy says "todo lists" and "Todo integration" (`:244`). Header `div` wraps a single h1 (`:159`).
9. **P3, no due date/description** for todos, which HA supports, and no per-list item counts in the select.

**Bugs and errors:** the stale-items and refetch behavior (#4). Live: not observable.

**Performance notes:** small lists; the cost is the refetch-per-click latency (#5).

**Suggested changes:** `harden` (labels, aria, alert, setup hint), `layout` (done section collapsed, counts, "Clear completed"), `adapt` (44px rows and checkbox-style complete), and `optimize` (optimistic update, stop refetching entities on list change).

**Not exercised:** Add, complete, remove on both tabs (all mutate real HA lists). The list-switch dropdown was not opened (no live load).

#### Live pass (2026-10-14)

- **Load behavior (NEW P1, live-only):** at 375px the page showed only a centered spinner for 20 s or more (no heading, no tabs, no message) before rendering. The backing calls `GET /api/shopping-list` and `GET /api/todo-lists` did not answer within 20 s in my manual fetches, then the page rendered with the banner "Failed to fetch todo lists: Internal Server Error" (console: two 500s). Cause is server/HA side (Home Assistant looks unreachable), but the UI has no request timeout, no progress text, and hides the whole page including the heading and tabs behind the spinner (`ListsPage.tsx` loading branch). Fix: render the shell, show a per-section skeleton, add a 10 s timeout with Retry.
- **Wrong empty state (NEW P2, CONFIRMED live):** the Shopping tab says "Shopping list is empty. Add items above or via the Lists Agent." while the same load shows a failure banner and the shopping request had also hung. A failed or timed-out fetch is presented as an empty list, so a user could add duplicates or assume data loss.
- **Error banner:** text "Failed to fetch todo lists: Internal Server Error" plus a 57x24px "Dismiss" button; no `role="alert"` (CONFIRMED live: `[role=alert]` absent). Error text sits above the tabs and does not say HA is unreachable.
- **Mobile 375:** no overflow. Add input 253x38, add button 48x38 with NO accessible name (`aria-label` null, `title` empty; CONFIRMED live, matches #1), both inputs have `labels.length` 0 (CONFIRMED live, #6). Tabs 38px tall. All below 44px; Dismiss below 24px high.
- **Not exercised:** add/complete/remove, list switch (no todo entities loaded), tablet, light theme (page content was the error state only; no theme problems seen elsewhere).
- **Claims:** #1, #6, #7 (no role=alert) CONFIRMED live; #7 (statusText empty) REFUTED for this case: the message includes "Internal Server Error" because the server sent a status text; #2-#5, #8, #9 not checkable.

---

### Tasks (`/tasks`, `src/pages/TasksPage.tsx`)

- **Purpose and primary task:** Monitor in-flight agent tasks, cancel one, and browse archived task history.
- **Scores:** Nielsen **21/40**.
  - H1 **1**: no auto-refresh and no loading indicator that keeps context, so "Active" goes stale.
  - H3 2, H4 2, H5 **1** (Cancel with no confirm next to a row-expand target), H6 2, H7 2, H8 3, H9 **1** (blank `statusText` errors), H10 2.
  - Cognitive-load failures: **4/8** (arbitrary IDs, hidden state, raw status strings, no what-next).
- **Desktop findings** (inferred): 5 equal stat cards, a segmented tab control with a solid amber active tab (`:162,168`, heavier than the amber-restraint rule), and a 6-column table with click-to-expand rows.
- **Mobile findings** (inferred): both tables use `overflow-hidden` (`:223`, `:334`) rather than `overflow-x-auto`, so on 375px the 6 columns are clipped and the Cancel/expand cell is unreachable. The stats grid is fine (2 cols). Action buttons are `px-2.5 py-1` (about 28px tall).

**Pain points and friction (ordered)**

1. **P1 [live], archived-task search loses focus on every keystroke.** The search input lives inside `ArchivedTasksTable` (`:320`). Each keystroke changes `searchFilter`, which re-creates `loadArchived` (`:98`), re-runs the effect (`:100-103`), sets `loading=true` (`:77`), and the parent swaps the whole table, input included, for a spinner (`:179-182`). The input unmounts and remounts, so the user can type only one character at a time. Fix: keep filters outside the loading branch (render spinner only over the results), debounce 300 ms, and keep previous data.
2. **P1 [live], tables clip on mobile.** `overflow-hidden` at `:223` and `:334` (see above). Fix: `overflow-x-auto`, or better cards on mobile with status, input and a "Cancel" action.
3. **P1, Cancel has no confirm or feedback.** `:250-255`. It cancels a running task immediately (`:105-112`), and the row is a `cursor-pointer` expand target one click away. Success is silent: the list just reloads. Fix: a confirm that names the task (first 60 chars of `userInput`), and a toast.
4. **P2 [live], Fragment keys.** The `<>` wrappers at `:237` and `:348` have no key (the key sits on inner `<tr>`s), so React warns "Each child in a list should have a unique key". Use `<Fragment key={t.id}>`.
5. **P2, no refresh.** The active list loads once (`:100`); no polling, no Refresh button, no live indicator, although a Task Tracker with SSE exists elsewhere in the app. Stats are also fetched twice and shared across tabs.
6. **P2, every reload blanks the list.** `loading` replaces the content (`:179`), including after Cancel (`:108`), so the table vanishes and re-appears and scroll is lost.
7. **P2, history cannot be read in full.** `Final Response` is cut at 300 chars (`:368`) and each message at 500 (`:377`) with no "show more", so the one place to inspect what the agent said is lossy. The UUID is shown as 12 chars + "…" (`:243`) with the full value only in the expanded row, and there is no copy button or link to the trace.
8. **P2, rows are mouse-only.** `<tr onClick>` (`:238-242`, `:349-353`) has no button, `tabindex` or `aria-expanded` (same pattern as audit P1#5). The chevron is decorative.
9. **P2, error text.** `${res.statusText}` (`api.ts:504,513`) is often empty, giving "Failed to fetch active tasks: ". There is no Retry.
10. **P2, status presentation.** Raw `InputRequired` (no space) shown as the label (`:31`), `Working` and `Submitted` are both amber, and the status filter omits Working/Submitted/InputRequired (`:303-307`, fine for archive, but undocumented). "No active tasks." (`:219`) has no explanation of when tasks appear.
11. **P3, stats:** 5 tiles with the same weight, and "Total Archived" duplicates the sum. Tabs do not show an Active count, even though the tile does.
12. **P3, pagination:** Previous/Next only, fixed 25 (audit P2#15).

**Bugs and errors:** #1 (functional), #4 (console warning). Live: not observable.

**Performance notes:** fine; the extra stats fetch on every history page change is redundant (`:87-90`).

**Suggested changes:** `harden` (#1, #3, #9), `adapt` (#2), `optimize` (keep-previous-data, polling only while visible), `clarify` (status labels, empty-state copy), `layout` (tile row to a single status strip).

**Not exercised:** Cancel, task creation. Rows were not expanded (no live load).

#### Live pass (2026-10-14)

Data: 0 active, 5 archived (3 completed, 2 canceled). Stats at first load read `activeCount: 5`, then 0 on the next load, so the page showed a stale or changing count that I did not investigate.

- **Desktop dark/light (1440x900):** stat tiles, a segmented control with a solid amber active tab, filter row, 6-column table (1118px wide) fits. No contrast problems in light; amber "unknown" agent text is lowish on the light card. The history table has no pagination controls with 5 rows. Expanding a row (read-only click) shows Full ID, Context ID, Created, Final Response and Conversation history.
- **One-character-at-a-time search (CONFIRMED live, P1):** I clicked the history search, then typed `hello`. The input node was replaced (a captured reference to the input no longer matched the DOM), `focusout` fired, and `document.activeElement` became `BODY`. The tool types fast enough that all five characters landed, but with human typing speed focus would be lost after the first. The text stays in state but the user must click back into the field after every character. Root cause as in #1 (`TasksPage.tsx:179` loading branch swapping the table). Contrast: User Memories search kept the same node and focus on `abcd`.
- **Mobile 375x812 (CONFIRMED live, P1):** table is 678px wide inside a 341px `overflow:hidden` container. Status, Agents and part of User Input show; Messages and Archived columns are unreachable and there is no scrollbar. Filter row (status select + search 204x38) fits. Stat grid is 2 columns, "Total Archived" alone on a third row; tiles take about 280px before the tabs (the tab bar starts at y=442 of 812).
- **Row interaction (CONFIRMED live):** rows have no `tabindex`, `role` or `aria-expanded`; mouse-only.
- **Fragment keys (CONFIRMED live):** console error "Each child in a list should have a unique key ... render method of `ArchivedTasksTable`", logged on each load.
- **User Input column is useless (NEW P2, live-only):** every row begins "HOME ASSISTANT CONTEXT: REQUEST_CONTEXT: { "timestamp…". The preview shows the prompt envelope, not what the person said, so rows are indistinguishable. In the expanded view the user turn is the same envelope text. Fix: show the extracted utterance, or strip the context block server-side.
- **Truncation (CONFIRMED live):** Conversation history entries end in "…" mid-sentence (500-char cut) and the task ID shows 12 chars plus "…".
- **Load time (live):** at 375px the active list, stats and history took 11 s (all four calls about 11.3 s) on a cold load; at 1440 the same calls were under 1.5 s later. Loading shows a bare "Loading…" with the stat tiles absent. Intermittent server slowness, not a client bug, but the UI gives no timeout or retry.
- **Not exercised:** Cancel, tablet 768, long-history pagination.
- **Claims:** #1, #2, #4, #8 CONFIRMED live; #7 (300/500 char cuts) CONFIRMED live for messages; #9 not checkable (no error); #3, #5, #6, #10-#12 not checkable.

---

### User Memories (`/user-memories`, `src/pages/UserMemoriesPage.tsx`)

- **Purpose and primary task:** For an enrolled speaker, inspect, search, edit and delete their stored memories.
- **Scores:** Nielsen **31/40**. This is the strongest page in the group.
  - H3 3, H5 3 (the native confirm is explicit about scope), H7 2, H1 3, H10 3.
  - Cognitive-load failures: **1/8** (the GUID suffix in the select is noise).
- **Desktop findings** (inferred): `max-w-6xl` centered page (the others are full width), a labeled select + search + Refresh toolbar (`:157`), friendly key labels with the raw key beneath, and a clear scope paragraph. Real a11y work is evident: `htmlFor` labels (`:159,168`), `role=alert/status` (`:182-184`), `aria-label` on rows and buttons, focus restoration after edit/delete (`:63-77`), `min-h-11` targets, and `focus-visible` rings (`:9`).
- **Mobile findings** (inferred): single-column list with 44px buttons, `break-words`/`overflow-wrap:anywhere` on long values; toolbar stacks below `lg`. `text-base` inputs avoid iOS zoom. No horizontal overflow expected.

**Pain points and friction (ordered)**

1. **P2, a request per keystroke and a skeleton flash.** The query key includes `search` (`:51`) with no debounce and no `placeholderData`, so each character fires a request and swaps the list for the pulse skeleton (`:208-209`). The AbortSignal prevents stale writes, but it is chatty on the Jetson target and flickers. Fix: debounce 250 ms, `placeholderData: keepPreviousData`.
2. **P2, native `window.confirm` for delete** (`:126`, audit P1#3). Copy is good, but it ignores the theme and defaults to OK. Fix: `ConfirmDialog` with Cancel focused.
3. **P2, save/delete result is far from the row.** The notice (`:183`) is at the top of the page; on a long list the user editing an item lower down gets no visible confirmation. Focus does return to the Edit button (`:63-77`), but the status is announced only to screen readers. Fix: inline "Saved" next to the row or a toast.
4. **P2, editing locks the whole page.** `controlsLocked` (`:61`) disables profile select, search, Refresh and every other row while one edit is open. This is safe but surprising: nothing says why they are disabled, and Esc/Ctrl+Enter do not cancel/save (`:235`). Fix: Esc to cancel, Ctrl/Cmd+Enter to save, and a note "Finish or cancel the edit to continue".
5. **P3, profile select shows the first 8 characters of the GUID** next to the name (`:163`). Useful only to disambiguate duplicates; show it only when names collide.
6. **P3, the "200 memories" cap is hard-coded** in the UI (`:263`) and tied to the server limit with no pagination. The check is `>= 200` and misreports when exactly 200 exist.
7. **P3, Refresh icon does not spin** while fetching (`:178`), and `Refresh` is disabled with no indicator.
8. **P3, dates** use `toLocaleString()` with no relative time or timezone (`:274`); "Saved … / Expires …" are two muted lines the eye skips.
9. **P3, layout inconsistency:** `max-w-6xl mx-auto` and `font-semibold` h1 differ from the sibling pages.

**Bugs and errors:** none found in source. Live: not observable.

**Performance notes:** capped at 200 items, no virtualization needed. `gcTime: 0` (`:57`) drops the cache on every profile switch, so going back refetches.

**Suggested changes:** `polish` (debounce, keep previous data, `ConfirmDialog`), `harden` (keyboard accelerators), and `extract` (this page's `inputStyle`/`secondaryButton`/live-region patterns are the template the other four pages should adopt).

**Not exercised:** Edit save, delete, profile switch (reads private data, and no live load).

#### Live pass (2026-10-14)

Data: one enrolled profile, 0 memories, so the edit/delete/long-value paths were not exercised.

- **Desktop dark/light:** loads in about 3 s. Header, a "Voice profiles" link button top right, a toolbar card (enrolled user select, search, Refresh), profile heading with monospace Profile ID, "0 memories displayed", a clear empty state naming the user, and a scope note. Left-aligned within a max-w container; clean in both themes.
- **Mobile 375x812:** no overflow (375/375). Select, search and Refresh are 309x44 each, stacked; inputs 16px (no iOS zoom). Fully legible. Profile ID wraps. This is the best mobile layout in the group.
- **Profile select label (CONFIRMED live, P3):** option reads "Zack (unknown-)". The profile ID starts with `unknown-`, so the 8-char suffix (`:163`) shows only the useless prefix "unknown-" and never disambiguates. Fix: show the last 4-6 characters or only on name collision.
- **Search (partly checked):** typed `abcd` (read-only): the input kept its node and focus (CONFIRMED live, no remount). A request with `query=abcd` was issued; resource timing lists only the completed one, so the claimed one-request-per-keystroke (#1) is not checkable this way (aborted requests are omitted). Skeleton flash not observable with 0 rows.
- **Console/network:** clean.
- **Claims:** a11y baseline claims (labels, 44px targets, 16px inputs) CONFIRMED live; #5 CONFIRMED live (and worse than described); #1-#4, #6-#8 not checkable (no data).

---

## Cross-page observations (this group)

- **Systemic list-patterns differ on every page.** Five loading treatments (spinner-only, text+spinner, centered spinner, skeleton pulse, whole-section replace); h1 styles differ (`font-display` bold on Entity/Lists/Tasks, semibold without `font-display` on Presence); card radii 4/12/16px; notices differ (toast on Presence, inline banner on Lists/Tasks/Entity, role-aware notice on Memories). Only Memories meets the a11y baseline. Extract `PageHeader`, `Card`, `EmptyState`, `LoadingState`, and `Field` from it (audit P2#7).
- **Loading branch swallows controls (Tasks `:179`, Entity `:1311`).** Both replace the results and, in Tasks, the filter input itself. Rule: loading state wraps results only, with previous data kept.
- **Component-in-component (Entity `:602-836`) and unkeyed fragments (Tasks `:237,348`)** are code-quality bugs that cause focus loss and warnings; add an ESLint rule (`react/no-unstable-nested-components`) to catch them.
- **Native `confirm()`** on Entity (`:363,407,504,518`), Presence (`:99`) and Memories (`:126`), all for destructive or state-changing actions, with no consequence line.
- **State-as-label toggles** on Entity ("All Entities"/"Exposed Only") and Presence ("Enabled"/"Disabled"), and eye-icon toggles. Use `ToggleSwitch` with a noun label.
- **Mobile:** none of the data tables has a mobile layout. Entity (8 columns) scrolls and clips its dropdown, Tasks clips with `overflow-hidden`, Presence truncates the only identifier. Header action groups do not wrap (Entity `:954`, Presence `:144`).
- **No live updates** on Presence and Tasks while both show live system state; only Entity subscribes (embedding progress).
- **Connection budget:** with two persistent EventSources per tab plus the Task Tracker's polling, several open dashboard tabs starve the browser's HTTP/1.1 connection pool (observed here as hung API calls; cause inferred). Consider sharing one SSE via `BroadcastChannel`/SharedWorker, or serve over HTTP/2.

## Top 5 for this group

1. **P0** `EntityLocationPage.tsx:741`: the bulk "Assign to Agent" fires a reset-to-All write at the same time as the intended write, so selected entities can silently lose their restrictions. Also: bulk Assign semantics (`:735`) and bulk Smart Assign (`:782-821`) apply with no preview, count or undo.
2. **P1** `EntityLocationPage.tsx:602-836`: nested component definitions remount the whole table (up to 250 rows) on every keystroke, selection toggle, and SSE tick. Focus and scroll are lost, which also blocks keyboard multi-select. Pair with inaccessible 16px checkbox buttons and page-scoped selection (`:846,870`).
3. **P1** `TasksPage.tsx:179` + `:320`: the history search input is unmounted by the loading spinner on each keystroke, so only one character can be typed at a time. The tables also clip on mobile (`:223,334`), and Cancel has no confirm (`:250`).
4. **P1** Mobile: Entity header/bulk/search rows and the 8-column table with a clipped agent dropdown (`:954,706,1363,640`), and Presence truncating the only sensor identifier (`PresencePage.tsx:226`). None has a mobile layout, and live verification at 375px is still owed.
5. **P1/P2** Destructive naming and weight: "Clear All Filters" (`:973`) deletes agent access config, and "Start Clean" (`:997`) hides every entity next to routine buttons with a native confirm. Plus hidden errors in the auto-assign modal (`:560`) and a modal with no dialog semantics.

## Live pass summary

**Counts (2026-10-14):** CONFIRMED live 14, REFUTED live 2 (partly: "no focus-visible" is true only for inputs; Lists empty `statusText`), NEW 8.

**Top live-only findings:**
1. NEW P1: at 375px and 768px the non-wrapping Entity header group (`EntityLocationPage.tsx:954`, 518px) widens the whole `main` scroll container to 534px. The whole page (filters, cards, table card, tabs) scrolls sideways and the right edge of every filter input is cut off. The `documentElement` overflow check passes (375), so only measuring `main` shows it.
2. CONFIRMED P1: Tasks history search loses its node and focus after each keystroke (`:179`/`:320`), and the history table is 678px inside a 341px `overflow-hidden` box at 375px, so Messages and Archived are unreachable.
3. NEW P1/P2: slow backends are shown badly. Lists shows a bare spinner for 20 s+ and then "Shopping list is empty" next to a 500 banner (a failed fetch rendered as empty); Entity summary cards and Tasks tiles are absent for 11-15 s with no skeleton or timeout.

**Other new:** Entity page shows no onboarding banner on an empty cache while Start Clean and Clear All Filters stay enabled; Tasks "User Input" column shows only the prompt envelope ("HOME ASSISTANT CONTEXT: REQUEST_CONTEXT…") for every row; Memories profile select suffix reads "unknown-"; `/api/tasks/background` polled about 10 times in 16 s.

**Remaining unverified (reason):** the P0 bulk-assign race (not exercised), row selection and bulk bar, remount and focus loss in the entity table, large-list keystroke timing, row agent dropdown clipping, auto-assign preview (all need entities; the cache is empty and reloading it is a mutation); Presence identifier truncation and row layout (0 sensors); Lists add/complete/remove and list switching (HA unreachable, no mutation allowed); Memories edit/delete and per-keystroke request count (0 memories; aborted requests not listed in resource timing); Tasks Cancel; light theme on Lists and Entity table rows; measured contrast is estimated with a script on translucent backgrounds.
