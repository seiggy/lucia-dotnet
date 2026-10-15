# Dashboard UI review: index

Date: 2026-10-14. Scope: every `lucia-dashboard` route (28 page files plus the app shell). The review itself changed no code; the fixes made afterwards are listed under Fix status below. The earlier summary is in [../dashboard-audit.md](../dashboard-audit.md).

## Read this first: how reliable is this?

Two passes. Seven reviewers first read their pages' source in full and ran the Impeccable detector (two bounce-easing true positives). The first live attempt failed: seven parallel tabs each held streaming connections and exhausted Chrome's six-per-host limit. A second, serial pass ran one agent at a time in a single tab, at 1440 dark and light, 1024, 768, 640 and 375px, with console and network checks and measured tap targets.

Live pass totals: **about 131 source claims confirmed, 14 refuted or corrected, 44 new findings.** Each group file has a "Live pass summary" at the end and a "Live pass" subsection per page. The first-pass `[src]` tags were not all retagged in place, so read the live subsections for what was verified.

**Limits of the live pass:**

- **The instance is nearly empty.** There are no entities, presence sensors, memories, alarms, templates, plugins, LLM traces or exports, and Home Assistant looks unreachable. Entity Locations bulk actions (including the P0 race), trace detail and span timeline, alarm cards and the timezone bug, template editing, Plugins installed rows, and the Voice promote/merge flows could not be exercised.
- **Login and Setup** were rendered in an isolated headless browser with mocked APIs, not against the real instance.
- No mutating control was clicked on the real instance.
- Real Jetson hardware, iOS input zoom and password managers were not tested.

Counts are the reviewers' own tallies and are approximate.

## Totals (approximate)

| Severity | Count |
|---|---|
| P0 | 2 |
| P1 | about 70 |
| P2 | about 160 |
| P3 | about 95 |

## Reviews

| File | Pages |
|---|---|
| [01-monitor-and-shell.md](01-monitor-and-shell.md) | App shell and nav, Activity, Traces, Command Traces, Exports |
| [02-agents-conversation-mcp.md](02-agents-conversation-mcp.md) | Agents, Conversation, Agent Definitions, MCP Servers |
| [03-providers-config-plugins.md](03-providers-config-plugins.md) | Model Providers, Configuration, Plugins |
| [04-entities-presence-lists-tasks-memories.md](04-entities-presence-lists-tasks-memories.md) | Entity Locations, Presence, Lists, Tasks, User Memories |
| [05-alarms-cache-templates-matcher.md](05-alarms-cache-templates-matcher.md) | Alarms, Prompt Cache, Response Templates, Matcher Debug |
| [06-skill-optimizer-and-voice.md](06-skill-optimizer-and-voice.md) | Skill Optimizer, Voice Platform |
| [07-auth-setup-installer-appliance.md](07-auth-setup-installer-appliance.md) | Login, Setup, Installer, Appliance (source only) |

## Fix status (2026-10-14)

Fixed in `lucia-dashboard/src`: the Entity Locations bulk-assign race, the Agents page selection, connection starvation (the activity, command-trace and task streams close in hidden tabs, task polling only while the stream is down, 10s timeout and a retry screen for the auth check instead of dropping an installed system into Setup), and the shared `Modal`, `ConfirmDialog`, `ConfirmProvider`/`useConfirm`, `Field` and global `:focus-visible` primitives. All 19 native `confirm()` calls and the one `prompt()` now use `useConfirm` or a `Modal`, with consequence text checked against the backend handlers (Promote and Move-clip confirms are new). All six div modals (Alarms alarm and sound forms, Template preview, Plugins repo, auto-assign preview, Register Agent) use `Modal`. `CustomSelect` portals into an open dialog, and form modals no longer close on a stray backdrop click. The auto-assign Apply error now shows inside its modal.

Still open: `Field` is used only in the new Register Agent and connection-name dialogs (about 100 other labels are unassociated), plugin uninstall/disable and repo remove still have no confirm, the Appliance reboot and restart confirms, and everything in the P1 list not named above.

## P0: data loss or broken core behavior (#1 fixed, #2 partly fixed, see above)

1. **Entity Locations bulk "Assign to Agent" races two writes** (`EntityLocationPage.tsx:741`). It calls `handleBulkAssign(null)` (the comment says it won't run, but it does), which resets the selected entities to "all agents", and then fires its own `updateEntityAgents`. Selected entities can silently lose their restrictions. Checked in code by me.
2. **Agents page keys everything on `agent.url`, which the live `/agents` no longer returns** (`AgentsPage.tsx:737,787,884-889`). The cards carry only `supportedInterfaces[0].url`, so every card selects together, detail shows the first agent, and unregister or refresh hits `/agents/undefined`. Selection and the request paths are fixed, and the server now decodes the encoded address it receives (it used to reject every refresh with 400 and ignore every unregister). Registering and unregistering still fail for dashboard users because the registry endpoints accept only the internal agent token (tracked in #308). Refresh works for agents with an absolute address and is disabled for the ones that run in this host, whose card URLs are relative. I confirmed the API shape and the page code; I did not confirm in the running UI or check for a mapping layer in `api.ts`.

## P1 themes that cut across pages

1. **Connection exhaustion.** Each tab holds a task stream plus a 2s poll (`TaskTracker.tsx:67,126`), and `RestartBanner` polls every 5s. With about three to five tabs open the app hangs on "Loading..." with no timeout or error state, while `curl` to the same API answers in milliseconds. Seen by four reviewers. About 80 to 138 `/api/tasks/background` requests per two minutes were logged.
2. **Native `confirm()` for destructive actions** on about 20 sites, with no object name or consequence. Several bulk actions are worse: "Clear All Filters" actually deletes agent access config, "Start Clean" hides every entity, and Promote, Merge, Reassign, Apply Results, Evict, plugin Uninstall and Appliance reboot have no confirm at all.
3. **Hand-rolled modals** with no dialog role, focus move, trap, Esc or inert background (Alarms and Sounds forms, Template preview, auto-assign, plugin repo, Register agent, mobile drawer). Alarms was observed live. `ConfirmDialog` and `CustomSelect` show the right pattern.
4. **Forms:** labels not associated to controls, placeholder-only inputs in Setup, no global `:focus-visible`, off-palette blue focus rings, 4px radii against the 12px spec, icon-only buttons 24 to 34px.
5. **Unsaved-change handling.** Configuration's Show secrets toggle wipes unsaved edits, there is no route-change or refresh guard, Back on forms discards typed input, Save and Discard sit at the bottom of long forms with no sticky bar on mobile.
6. **Silent failures.** Activity, Prompt Cache, Skill Optimizer and Voice status turn fetch errors into healthy-looking zeros, "Setup needed", or an editable empty form.
7. **Secrets.** The provider edit form prefills the real API key into the DOM (observed live), and plugin config fetches secrets unmasked on mount.
8. **Mouse-only or hidden controls.** Trace rows and agent cards are clickable divs, alarm actions are `opacity-0` until hover, and entity checkboxes are 16px buttons.
9. **Mobile layout.** Observed: MCP form and cards clip at 375px, provider delete targets are 24px. Inferred: Entity Locations header and 8-column table, Tasks tables, Matcher's `grid-cols-5`, Voice Models tabs, Skill Optimizer toolbar, span timeline, Presence truncation.
10. **No concept help for first-timers.** Nothing explains agent vs definition vs MCP server, cache hit rate, matcher score, template tokens or Wyoming. Nav labels differ from page titles, and 22 nav items are ungrouped.
11. **Page structure.** Voice Platform is one 1832-line component with non-routable tabs, and the `/voice-onboarding` redirect lands on the wrong tab. Entity Locations defines components inside the page so up to 250 rows remount on every keystroke.
12. **Performance.** About 1 MB single JS bundle with no route splitting, and aggressive polling.

## Verified live (new or upgraded by the second pass)

- **Agents P0 reproduced:** clicking any card marks all 10 selected and shows `orchestrator`; the URL line is empty; the console logs a duplicate-key error.
- **Mobile drawer** fails every check at once: Esc does not close it, focus stays on the hamburger, Tab escapes behind it, no `aria-expanded`, 40px rows.
- **Dialogs confirmed broken live** on Alarms, Add Sound, Register Agent and the Plugins repo dialog: no role, Esc and backdrop do nothing, Tab escapes into the sidebar.
- **Prompt Cache contradiction is a backend bug**, not UI timing: the list endpoints return `[]` while `/stats` returns 4 entries, so the UI shows "(4)" and "No entries" together and Clear All is disabled, so the phantom entries cannot be cleared.
- **Matcher Debug** blocked about 35s with only a spinner, then returned nothing, and its "lower the threshold" advice is wrong for an empty index. The tuning grid breaks at 375px and 768px.
- **Setup at 375px:** the dashboard key is 501px wide in a 313px card, the page overflows by 238px and Copy is pushed off-screen. If `/api/auth/status` returns 500, an existing install lands on the first-run Welcome screen (`AuthContext.tsx` catch sets `setupComplete=false`).
- **Appliance:** after a reboot confirm with the backend offline, the page stays healthy-looking with Reboot and Restart enabled. AgentHost and Redis restart fire with no confirm.
- **Entity Locations header** (`:954`, no wrap) widens `main` to 534px at 375px and 768px, so Start Clean and Reload are off-screen. The standard overflow check passes, so only measuring `main` shows it.
- **Tasks history search** loses focus after each character (input node replaced), and the history table is 678px in a 341px clipped box at 375px.
- **Configuration at 768px:** the app sidebar plus the 256px section nav squeeze the form to about 170px. Unsaved edits are lost silently on sidebar navigation. Save/Discard sit below the fold and are not sticky.
- **Plugins at 375px:** the tab strip is 424px in 293px, so the Store tab is clipped to its icon.
- **Agents at 768px:** the detail pane is about 190px wide next to the sidebar.
- **Voice Platform:** the literal text "undefined" shows as a pill on four STT model cards, wrong architecture labels (Silero VAD reads "Zipformer CTC"), Status shows VAD and wake word as "(loading…)" while Models lists them Active, the Models sub-tabs wrap and hide "Speech Enhancement" at 375px.
- **Activity mesh at 375px:** 7 of 10 nodes off-screen, scroll trapped, 20 unlabeled tab stops. Total Requests 0 shows beside 5 LLM fallbacks. Command-trace durations show raw values like `9227.1738 ms`.
- **Slow backend handling:** Lists spinner for 20s or more then "empty" beside a 500 banner, summary tiles absent for 11 to 15s with no skeleton or timeout, Alarms first load stalled 15 to 20s.
- **MCP form** has no visible focus ring at all (`outline: none`, no replacement).
- **Polling:** `/api/tasks/background` is polled about every 2s on every page (roughly 500 requests in one session) alongside its stream.
- Command-trace filters lost on Back confirmed. Unknown URLs land silently on `/`.

**Refuted or corrected live:** light-theme badge contrast on Agents and Definitions passes (4.7 to 7.9:1), "no focus-visible" is only true for inputs (buttons and sidebar links show the browser ring), the Skill Optimizer toolbar wraps rather than overflows at 375px, Voice settings inputs and icon buttons are labeled, the Appliance update-rail fits at 375px, and the mesh is keyboard reachable (badly).

## Other notable single-page bugs

- Trace detail leaks label, notes and the success message between related traces, and Save may erase notes (`TraceDetailPage.tsx:90-110`).
- Tasks history search loses focus after each character (`TasksPage.tsx:179,320`).
- Editing a one-time alarm shifts its time by the UTC offset (`AlarmsPage.tsx:160` vs `:203`), and the hour field accepts 99.
- Prompt Cache showed stats of 4 entries beside an empty table, and shows the error and "No entries" together.
- Opening the Agent Definitions form auto-connects every enabled MCP server (`AgentDefinitionsPage.tsx:307-311`).
- Conversation example chips send live commands on click.
- Setup "Complete Setup" has no error or busy state, and the one-time dashboard key has no save acknowledgement.
- Appliance reboot and restarts have no offline or reconnect state, and update confirmations omit versions and downtime.
- Installer failures do not mark the failing stage.
- "Report Issue" puts raw utterances into a github.com URL.
- The Exports history never refreshes a "Processing" row.
- Unknown URLs silently redirect to Activity, and login loses deep links.
- The root ErrorBoundary removes the whole nav on any render error.
- `docs/images/setup-*.png` show an old four-step wizard.

## Correction to the earlier audit

`ToastContainer` does have `role="status"` and `aria-live="polite"`. Its real problem is that the container only renders once a toast exists, so the first announcement can be missed.

## Positive patterns to copy

- User Memories is the strongest page and a good template.
- Installer: mobile progress bar, 48px controls, reduced-motion handling.
- `ConfirmDialog`, `CustomSelect`, and the token system with light/dark parity.
- Configuration's Reset All is properly guarded.

## Suggested order of work

1. Fix the two P0s and the connection starvation (`TaskTracker`, `RestartBanner`, add a timeout and error state to the shell loader).
2. Build shared primitives: `Modal`, `useConfirm`, `Field`, `PageHeader`, global `:focus-visible`. This clears most P1 themes 2 to 4.
3. Unsaved-change and silent-failure handling (themes 5 and 6), then secrets (theme 7).
4. Mobile pass, after a proper live verification.
5. Route code splitting, nav grouping and title alignment, concept help text, Voice Platform split.

Suggested Impeccable commands: `/impeccable harden`, `/impeccable extract`, `/impeccable adapt`, `/impeccable optimize`, `/impeccable clarify`, `/impeccable onboard`, `/impeccable layout`, and `/impeccable polish` last.
