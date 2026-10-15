# Review 05: Alarms, Prompt Cache, Response Templates, Matcher Debug

Reviewer evidence labels: **[live]** = observed in the browser before the coordinator stopped live work; **[src]** = inferred from source only. Live access was cut short: Chrome's 6-connection-per-host limit was exhausted by parallel tabs (navigations hung 300s, pages stuck on "Loading..."), then the coordinator told me to stop. Everything not marked [live] is unverified in the UI. Impeccable detector on all four page files: `[]` (no findings; it does not cover the issues below). Page files read in full; `PersonalityEnginePanel.tsx` read because Templates embeds it.

Live instance state at review time: Alarms had 0 alarms and 0 sounds, so no alarm card was ever rendered. Console had no errors on `/alarms`.

---

### Alarm Clocks (`/alarms`, `src/pages/AlarmsPage.tsx`)

- **Purpose and primary task:** create, edit, enable/disable, dismiss and snooze alarms that play on HA media players; manage the alarm sound library (Alarms and Sounds tabs).
- **Scores:** Nielsen 22/40. Heuristic scored 1 or below: *Error prevention 1* (no range or cron validation, only toasts after submit, `:183-209`); *Help and documentation 1* (cron, "presence", "TTS Fallback", media-source URI unexplained); *Flexibility 1* (no keyboard handling in dialogs, no Enter to submit, no shortcuts). Cognitive-load failures: 3/8 (jargon "CRON", "Target Entity", free-text entity ID instead of a picker; schedule choice nests three levels deep: Recurring/One-time, then preset, then time or cron; hidden Advanced section holds the volume-ramp feature).
- **Desktop findings**
  - [live, 1440x900 dark] Empty state is clear: icon, "No alarms configured", "Create your first alarm" button, plus a duplicate "New Alarm" button above it (`:397-404` and `:410-415`). Two CTAs for one action.
  - [live] New Alarm dialog: no `role="dialog"`/`aria-modal` (`:599-600`), focus stays on the trigger button behind the overlay after open, **Esc does not close**, **backdrop click does not close**, background is not inert (Tab walks out of the dialog into the page). Verified by DOM and key presses. Same for the Add Sound dialog (`:821`).
  - [live] Typing `99` into the hour field is accepted silently; the input reports `rangeOverflow` but the modal is not a `<form>` and buttons never submit, so native validation never runs. `saveAlarm` would send cron `0 99 * * *` (`:198-199`, `:193`).
  - [live] Custom cron input placeholder `0 7 * * 1-5` is nearly invisible (`placeholder:text-stone` on `bg-basalt`, `:712`). No hint of what the five fields mean, no preview of the resulting schedule, no validation (`:193`).
  - [live] Time entry is two 24-hour number inputs with auto zero-padding; no AM/PM and no native `type="time"`. Spinner arrows, no leading-zero typing problems seen with "18".
  - [live] Labels are not tied to controls: 0 of 4 field labels have `htmlFor` (`:613,624,636,653`; confirmed `for=""`). Close X buttons have no `aria-label` (`:605,825`).
  - [src] Per-alarm action buttons Dismiss, Snooze, Edit, Delete are `opacity-0 group-hover:opacity-100` (`:466,473,480,498`) with no `focus-visible`/`group-focus-within` reveal and only `title` as the name. Keyboard users tab onto invisible buttons; touch users cannot see them at all. Only the Enable/Disable power icon is always visible.
  - [src] Enable/Disable toggle is a bare Power/PowerOff icon swapping color (`:485-494`); the state is conveyed by color and an icon change, not text, and the row also dims to `opacity-60` (`:421`). One click persists immediately, no feedback beyond a toast.
  - [src] Snooze toast hardcodes "9 minutes" (`:267`) while the UI has no way to set it.
  - [src] `describeCron` ignores day-of-month and month fields (`:34`): `0 7 1 * *` renders "Daily at 07:00". Wrong text on a schedule summary.
  - [src] Load failure shows a toast, then the page renders the empty state "No alarms configured" (`:111-116`, `:406`). A network failure looks like "you have no alarms".
  - [src] Tabs are plain buttons with no `role="tab"`/`aria-selected` (`:373-390`).
- **Mobile findings**
  - [live, 375x812] Dialog is flush to both screen edges (no outer margin), 620px tall; fits. Controls are 36-40px tall (inputs 38-42, Recurring/One-time 36, Cancel/Create 40, preset chips about 30), checkbox is 13x13, Advanced summary is 16px tall. All under 44px. No horizontal page overflow (`scrollWidth` 375). "Every day" preset chip wraps to two lines.
  - [src] Volume-ramp block is `grid-cols-3` inside the already narrow dialog (`:766`), about 100px per column for a slider with a long label. Likely cramped.
  - [src] Alarm card: five action buttons (each p-2, 16px icon, about 32px) in a `shrink-0` cluster beside the title (`:462-502`); with a long name the title truncates hard.
  - [src] Sounds table is a 4-column `<table>` with a long `<code>` media URI (`:538-591`) inside `overflow-hidden`, no `overflow-x-auto`; long URIs will clip on mobile.
  - [live] Not seen: keyboard overlap with the on-screen keyboard (no tool for it).
- **Pain points and friction (ordered)**
  1. **P1** Dialogs lack role, focus management, Esc, backdrop close, inert background. `AlarmsPage.tsx:599,821`. Fix: reuse the `ConfirmDialog` `<dialog>` pattern in a shared `Modal` (audit item 2). Command: harden.
  2. **P1** One-time alarm edit has a timezone bug. `:160` loads `new Date(alarm.nextFireAt).toISOString().slice(0,16)` (UTC) into a `datetime-local` input, which is interpreted as local time; `:203` then sends `new Date(value).toISOString()`. Opening and saving a one-time alarm in any non-UTC zone shifts its fire time by the UTC offset. Fix: format with local components. [src, not reproduced because there were no alarms.]
  3. **P1** Actions invisible without hover (`:466,473,480,498`): inaccessible to keyboard and touch. Fix: always show Edit and an overflow menu, or reveal on `group-focus-within` and at `@media (hover:none)`. Command: adapt.
  4. **P2** No validation or guidance for time and cron (`:693-713`, `:183-209`). Fix: hour 0-23 and minute 0-59 clamp on blur; for Custom show a live plain-language preview ("Weekdays at 07:00") using `describeCron`, and disable Create until valid. Also fix the field mix-up: if cron `*` hour, `Number('*')` is `NaN` and `:198` writes `NaN` (edit of such a cron lands in the "Every day" preset, `:134`). Command: harden.
  5. **P2** Target Entity is a free-text field (`:625-631`). Fix: a picker of `media_player` entities plus a "Presence-based" option; "presence" as a magic string is undiscoverable except for a 12px hint. Command: clarify.
  6. **P2** Unlabeled fields (`:613-653`, `:735-791`, `:832-881`). Fix: `htmlFor`/`useId` (audit item 4).
  7. **P2** `describeCron` loses dom/month (`:30-44`); the summary can lie.
  8. **P2** Error on load looks like empty (`:111`). Fix: error panel with Retry.
  9. **P3** Duplicate empty-state CTAs (`:397`, `:410`); emoji 🔊 as meaning at `:457` (audit item 13); `bg-teal-500/10 text-teal-400` raw palette at `:557` (audit item 8); `rounded-lg`/`rounded-md` 8/6px where DESIGN.md wants 12px on controls (`:399,618,805,810` and many more); Sounds delete icon has no `title` or `aria-label` (`:581`); text-[10px] "Uploaded" badge (`:557`).
- **Bugs and errors:** the timezone bug and the hour/minute validation gap above; `/api/alarms` and `/api/alarms/sounds` fetched twice on load [live, StrictMode in dev]; no console errors [live].
- **Performance notes:** [live] network log was dominated by `GET /api/tasks/background` repeated more than 80 times in a few seconds, from the shell (`TaskTracker`), not this page (audit item 18).
- **Suggested changes:** shared `Modal` (extract + harden); move actions into a visible row or kebab menu (adapt); use native `<input type="time">` (one control, mobile pickers, no padStart code) for the preset path (distill); replace CRON presets with weekday toggles Mon-Sun plus a time (clarify); explain the media-source URI with an example and show the uploaded file path; add an alarm-state text ("On"/"Off") next to the icon.
- **Not exercised:** edit dialog (no alarms exist), Save/Create, enable/disable, dismiss, snooze, delete, sound upload, Add Sound dialog (open/cancel was not reached), light theme, tablet.

#### Live pass (2026-10-14)

Instance still has 0 alarms and 0 sounds. All checks in light theme unless noted; nothing saved, dialogs cancelled with the Cancel button.

- **Desktop light 1440x900:** page renders cleanly; empty states for both tabs ("No alarms configured", "No alarm sounds configured / Alarms will use TTS announcement as fallback"). Dialog placeholders (`Morning Wake Up`, `media_player.bedroom or "presence"`, cron `0 7 * * 1-5`) are barely legible against the field fill in light theme as well as dark (`placeholder:text-stone`). First load took about 15-20 s of "Loading alarms..." after coming from Matcher Debug (requests to `/api/alarms` and `/api/alarms/sounds` pending, then 200); `curl` to the server was fast (5 ms), so the stall is browser-side connection starvation from the shell's streams. The page shows a plain "Loading alarms..." with no timeout or error path.
- **CONFIRMED live (light, 1440):** New Alarm dialog has no `role=dialog`; focus stays on the trigger button; Esc does not close; click on the backdrop does not close; Tab from the last dialog button (Create Alarm) lands on the sidebar "Activity" link behind the overlay (no trap, no inert). Same on Add Sound dialog (Esc did nothing, no role, focus left on the trigger).
- **CONFIRMED live:** typing `99` in the hour field is accepted with no visual error (`max=23`, `validity.rangeOverflow=true`, input not inside a `<form>`). Custom cron field accepts `not a cron 99`: no inline message, no hint of the five fields, **Create Alarm stays enabled**. 0 labels have `htmlFor`/`id` on the inputs (all 7 inputs `labels.length` 0 except the checkbox, which is wrapped).
- **Mobile 375x812 light:** dialog opens as a bottom sheet flush to edges; Esc and tap above the sheet do not close it. `scrollWidth` 375, no horizontal overflow. Cancel button 77x40, Create 40 high, inputs 38-42 high (all under 44, none under 24 except the Advanced summary).
- **REFUTED / corrected:** the earlier note that the focus ring is absent is wrong for the sidebar: tabbing onto a sidebar link shows a visible orange ring. The `focus-visible` gap is specific to page controls, not the shell.
- **New findings:**
  - **P2** Esc/backdrop/focus-escape also hold in the Add Sound dialog (`AlarmsPage.tsx:821`), so one shared Modal fixes all three dialogs on this page.
  - **P3** "Create Alarm" is enabled for an empty Name and Target Entity in the default state (field-level required not enforced until submit). Not clicked.
- **Tablet and dark:** dark rechecked earlier; tablet 768 not run for Alarms (the page is a single column).
- **Still not checkable:** alarm card actions, edit dialog, timezone bug (`:160`, `:203`), snooze toast, load-failure-looks-empty (`:111`), Sounds table overflow: all need data or an error state.

---

### Prompt Cache (`/prompt-cache`, `src/pages/PromptCachePage.tsx`)

- **Purpose and primary task:** inspect two caches (router decisions, agent responses), see hit stats, evict entries. The primary task is "is the cache helping, and is it serving something stale or wrong".
- **Scores:** Nielsen 20/40. At 1 or below: *Visibility of system status 1* (error and empty states contradict each other, see below); *Help and documentation 0* (no concept explained anywhere); *Error prevention 1* (per-row Evict has no confirm, Clear All uses native `confirm()`); *Match with real world 1* (jargon). Cognitive-load failures: 4/8 (no explanation of "cache hit rate" or why router and agent caches differ; 7-8 column tables; mixed priorities; wrong emphasis on destructive Clear All).
- **Desktop findings**
  - [src] Page is title plus tabs plus 4 stat tiles plus table; no subtitle or sentence on what a cache is for. `:176`. A first-timer sees "Router Cache" and "Agent Cache" with no definition.
  - [src] Stat tiles: "Hit Rate", "Total Hits", "Total Misses". Misses are shown in alarm-rose (`:84-86`), though misses are normal and expected, especially on a new cache. Amber is used for Hit Rate (`:70`), consistent with "amber = emphasis" but arbitrary.
  - [src] Error and empty collide: `:209` shows the error text, and `:212` still renders the table with "No routing cache entries found." when `error` is set. A failed fetch tells the user the cache is empty. No Retry button.
  - [src] Loading blanks the table but not the stats (`:205` vs `:212`), and `loadData` sets `loading` true on every evict, so the whole table flashes (`:102`).
  - [src] The `reasoning` field is fetched (`:19`) and never displayed; for a router cache, the "why" is the value.
  - [src] No search, sort, filter, or page limit; entire cache loads into memory (`:105-120`). Full `toLocaleString()` dates in two columns (`:46,255,258`) eat width.
  - [src] Evict has no confirm and no per-row identity in its accessible name (`:261-266`, 14 identical "Evict" buttons for a screen reader). Clear All uses native `confirm()` (`:144,163`, audit item 3) and sits at the same visual weight as navigation, 40px from the tabs.
  - [src] Tabs are not `role="tab"` and have no `aria-pressed` (`:180-201`).
- **Mobile findings**
  - [live, 375x812 dark and light] 2x2 stat grid fits. The tab buttons wrap their labels to two lines ("Router / Cache (4)"). Table sits in a horizontal scroller; the empty message is `colSpan` text clipped mid-sentence ("No routing cache entr...") and the Actions column is off-screen. 
  - [live] **Contradiction seen:** the tab and stats said 4 router entries, 0.0% hit rate, 0 hits, 5 misses, while the table said "No routing cache entries found." Cause not determined (stats and list disagree, or list was not yet populated). Either way a user would not trust the page. Needs a serial live recheck.
  - [src] On mobile the 7-column table (8 on Agent tab) is useless without horizontal scroll; the primary info (prompt, agent) is only the first two columns. Prefer cards.
- **Pain points and friction (ordered)**
  1. **P1** Error state also renders the empty-table message; no retry (`:209`, `:212`, `:270-276`). Fix: `ErrorState` with Retry replaces the table when `error`. Command: harden.
  2. **P1** Destructive controls: native `confirm()` for Clear All (`:144,163`), none for Evict (`:261,339`). Fix: `ConfirmDialog` with count ("Clear 4 routing decisions. They will be re-learned on next use.") and `aria-label="Evict: <prompt>"`. Command: harden.
  3. **P2** Nothing explains the concept. Fix: one sentence under h1 per tab ("Router Cache remembers which agent handled a phrase so the router skips the model call. Agent Cache stores full answers for repeated prompts."), plus tooltips or a "?" on Hit Rate, Confidence, Hit Count. Command: clarify.
  4. **P2** Hit rate at 0.0% with misses 5 reads as broken. Show "No hits yet" or the ratio with sample size ("0 of 5 lookups") and do not color misses as errors. `:70,84-86`.
  5. **P2** Show `reasoning` for router entries (`:19`) via an expandable row; show the response text in a detail drawer for agent entries (truncated at 60 chars at `:314,317`, full text only in a `title` tooltip, which touch users cannot read).
  6. **P2** Mobile table (`:224`, `:296`): render as cards under `md`.
  7. **P3** `rounded-xl` and tokens are on-spec; emoji none; `text-xs` badges fine. Tab icon plus count inside button makes wrapping on mobile; shorten to "Router (4)" / "Agent (4)".
- **Bugs and errors:** the stats-versus-table contradiction [live, once]; error-plus-empty [src]. No console errors seen on this page [live, mobile load only].
- **Performance notes:** four requests in parallel on load and again after every evict (`:105`); full-list sort client-side. Bounded today.
- **Suggested changes:** `PageHeader` with subtitle; tile row reduced to Entries, Hit rate (with "n lookups"), plus a one-line "Hits / Misses" caption; table to cards on mobile; evict through `useConfirm`; row-expand for reasoning.
- **Not exercised:** Evict, Clear All, Agent Cache tab on desktop, light theme, tablet, desktop at 1024.

#### Live pass (2026-10-14)

- **Contradiction CONFIRMED live and root-caused (P1, backend).** Fetching the four endpoints from the page: `GET /api/prompt-cache` returns `[]`, `GET /api/prompt-cache/stats` returns `{"totalEntries":4,"totalHits":0,"totalMisses":5,"hitRate":0}`; `GET /api/chat-cache` returns `[]`, `/api/chat-cache/stats` returns `{"totalEntries":4,"totalHits":0,"totalMisses":10,"hitRate":0}`. All 200, no console errors. So the stats counter says 4 entries per cache while the list endpoint enumerates none (a stats/list disagreement in the server, probably counting keys the list filters out or a stale counter), not a UI timing bug. The UI then shows tab "(4)", tile "ROUTER ENTRIES 4", and "No routing cache entries found." together. It is stable across reloads and on both tabs (Agent tab: "AGENT ENTRIES 4", "TOTAL MISSES 10", "No agent cache entries found.").
- **New P1/P2:** **Clear All is disabled when the list is empty** (`disabled` true on both tabs), so a user cannot clear the 4 phantom entries the stats claim exist; the only way out is outside the UI. Consequence: if the entries really exist server-side and are stale, the dashboard cannot evict them. Fix server list/stats consistency first; in the UI, enable Clear All based on `stats.totalEntries`, not `entries.length`.
- **Desktop light 1440x900:** layout clean; tiles and tabs readable; hit rate amber, misses rose, hits sage all legible on the light surface. Tabs have 0 `role=tab`. No subtitle or help sentence, as predicted.
- **Mobile 375x812 light:** 2x2 tile grid fits; tab labels wrap to two lines ("Router / Cache (4)", icon plus count); table header shows PROMPT, ROUTED AGENT, CONFIDENCE, HIT COUNT before cutting off; the empty message "No routing cache entr..." is clipped by the scroller and a horizontal scrollbar sits inside the card, Actions column offscreen. Page `scrollWidth` 375. Clear All button is the fade-out rose style (disabled), about 40px tall.
- **Error plus empty (`:209`, `:212`):** [src] not reproducible, because every request returned 200. Stays [src].
- **Network:** 4 requests on load, repeated once (StrictMode dev); no 4xx/5xx.
- **Not exercised:** Evict, Clear All (disabled anyway), tablet.

---

### Response Templates (`/response-templates`, `src/pages/ResponseTemplatesPage.tsx`)

- **Purpose and primary task:** edit the canned replies Lucia gives when a command is handled on the fast path (without an LLM), per skill and action, using `{tokens}`; optionally enable LLM "personality" rewriting.
- **Scores:** Nielsen 21/40. At 1 or below: *Visibility of system status 1* (unsaved edits are invisible per card except a disabled Save, and collapsing a group loses them silently); *Help and documentation 1*; *Error prevention 1* (empty variants can be saved, `:244`; unknown tokens pass silently into preview). Cognitive-load failures: 4/8 (three stacked blocks with different jobs: personality LLM config, add-template form, template list; no hierarchy of importance; raw skill IDs; 10px helper text; unexplained "Matched by").
- **Desktop findings** (all [src]; live pass not done)
  - Order is the issue: PersonalityEnginePanel (a prompt textarea, model select, two toggles, its own Save) is first (`:716`), then a full Add Template form (`:719`), and only then the actual templates (`:766`). The thing the title promises is below about two screens of other content.
  - There is **no editor modal**: editing is inline in cards. The only modal is `PreviewModal` (`:142-170`). It has `role="dialog" aria-modal="true"` but no accessible name (`aria-labelledby` missing), no Esc, no focus move or trap, no backdrop close; the X is a 16px icon (`:152-154`). The audit already lists it (item 2); `ConfirmDialog` is the correct pattern.
  - Preview is a modal that hides the editor, so the user cannot edit and preview side by side. A live inline preview line under each input would remove the modal. `renderPreview` (`:39-52`) substitutes a fixed list of 11 tokens with hardcoded samples ("Living Room Light"); tokens from the API pattern list that are not in that list (`pattern.tokens`, `:372`) render raw in preview, with no warning, so preview can disagree with the real output. The note "Placeholders replaced with sample values" (`:164`) is 11px.
  - Syntax help is one placeholder string `Template text with {entity} placeholders...` (`:272`) and clickable `{token}` chips at 10px (`:105-115`). There is no explanation that single braces are tokens, that several variants are picked from at random (assumption; the UI never says why "Add variant" exists), or what each token yields. First-timers will not know whether `{entity}` or `{entityName}` is right (preview accepts both, `:41-43`).
  - `ExampleTemplatesInfo` "Matched by: ..." (`:124-135`, used `:364`) is aggregated across all actions in a skill group (`:325-340`) so it cannot be tied to a specific action or card; 10px mono text.
  - Group collapse unmounts cards (`:361`), so `localTemplates` (`:189`) with unsaved edits is dropped with no warning. Same on route change. 
  - `handleSave` sends `localTemplates` including empty strings (`:244`) while create filters them (`:435`). An emptied variant can be saved and later produce an empty reply.
  - `Reset to Defaults` is a top-right button at the same weight as nothing else on the page (`:707-712`); the confirm correctly states the consequence and says it cannot be undone (`:785`), but "all custom templates" also covers personal edits across every skill. It is a good dialog on a button placed where a first-timer might click it.
  - Delete confirm message has no template name or action (`:794`).
  - Native `<select>` styled with `rounded` (4px) and `px-2 py-1.5` (`:471-472`) next to 12px inputs; `CustomSelect` exists and is used on Alarms.
  - Labels without `htmlFor` (`:486,515,552`); remove-line X buttons and Preview icon buttons are about 16-28px (`:152,274-281`); `placeholder-dust/60` low contrast.
  - Raw skill IDs in the Add form ("Light Control (LightControlSkill)", `:499`), and in groups unknown skills show the raw ID (`:34-36`); only 3 skills are mapped (`:28-32`).
- **Mobile findings** [src]
  - Card header row `flex-wrap` handles Preview/Save/Delete (`:227`), inputs `flex-1`; Add Template grid collapses to one column (`:484`). Token chips wrap. Likely usable, but tap targets are small (chips about 20px, `px-2 py-0.5`, `:111`) and the page is very long, with the personality panel (a 5-row textarea) before any template.
  - Preview modal `max-w-md w-full` with no side margin: flush to the screen edge at 375.
  - Not seen live.
- **Pain points and friction (ordered)**
  1. **P1** Preview modal lacks Esc/focus trap/name (`:144-148`). Fix: shared `Modal`. Command: harden.
  2. **P1** Unsaved edits are lost on group collapse (`:361`) and nothing warns. Fix: keep cards mounted (`hidden`), or show a per-card "Unsaved" dot and a `beforeunload` guard. Command: harden.
  3. **P2** Information order and noise: move PersonalityEnginePanel to a collapsed section or its own tab ("Templates | Personality"), put Add Template behind an "Add template" button, and default groups to collapsed except the first. `:716-723`, `:322`. Command: shape/distill.
  4. **P2** Add real syntax help: a short "How templates work" block (when it is used, `{token}` rules, variants chosen at random) and show each token with its sample value. Replace the modal with inline preview. `:105-115`, `:164`. Command: clarify.
  5. **P2** Empty variants can be saved (`:244`); unknown braces unflagged. Fix: trim and filter on save; highlight unrecognized `{tokens}` in amber on input and in preview.
  6. **P2** Fields lack `htmlFor`; select styles off-spec (`:471-472`).
  7. **P3** Text at 10-11px for primary help (`:105,127,164`); `bg-amber-glow/10` chips fine; raw `bg-yellow-400` dot in the embedded panel (`PersonalityEnginePanel.tsx:216`).
- **Bugs and errors:** unsaved-edit loss on collapse; empty-variant save; preview/tokens mismatch. Not verified live.
- **Performance notes:** `JSON.stringify` dirty check per card per render (`:193`), trivial at this size. Both queries run in parallel; no stale handling.
- **Suggested changes:** tabs or accordion to separate "Templates" from "Personality (LLM)"; inline preview row under each variant; syntax legend; humanized skill and action labels (map from API, not hard-coded, `:28`); per-card dirty badge.
- **Not exercised:** all live behavior. Specifically not clicked: Preview, Save, Delete, Add Template submit, Reset to Defaults, Personality Save/toggles.

#### Live pass (2026-10-14)

- **The instance has zero response templates**, so the editor cards, the Preview modal, the group collapse and the "Matched by" aggregate could not be opened live. The page shows the empty state "No response templates found. Create one above or reset to load the default templates." The only path to default templates is "Reset to Defaults" (not clicked). **New P1 (first-run):** a fresh instance lands on an empty list with the fix hidden behind a button that is described elsewhere as destructive ("cannot be undone"); there is no "Load defaults" action in the empty state itself. Fix: put a "Load default templates" button in the empty state.
- **CONFIRMED live (order):** at 1440x900 the viewport shows the Personality Response Engine panel first (the personality prompt is long and truncated in a 5-row textarea; toggle for "Use Personality Responses" is on; its own Save Settings), then the Add Template form, then the list. The page title's subject is below about 1.5 screens. `main` height 1165 desktop, 1453 at 375.
- **Add Template form (typed into, not submitted):** the Skill dropdown lists only 5 skills, two of them with the raw class name doubled ("SecurityControlSkill (SecurityControlSkill)", "SensorControlSkill (SensorControlSkill)"); Fan, Music and others are absent. Light Control offers only two actions: `brightness` and `toggle` (no on/off, no set colour). After choosing an action, a 10px mono line shows "Matched by:" with raw regex-like patterns (`"(action:die|brightnes) {the} {entity} {to} {value} {percent}"`). That is internal parser syntax shown to the user with no explanation, and it contains `{the}`/`{to}` pseudo-tokens that are not in the Insert chips. Insert chips offered: `{action}`, `{entity}`, `{value}` (19px tall). Placeholder text is `{entity} has been {action}`, which does not fit the brightness action. **CONFIRMED live:** typing `Set {entityName} to {value} {bogus}` gives no warning on unknown tokens and "Create Template" stays enabled. No inline preview exists in the add form.
- **Labels:** all 7 labels lack `htmlFor` (CONFIRMED live).
- **Mobile 375x812 light:** no horizontal overflow (`scrollWidth` 375); layout stacks correctly, selects full width, native select look (`rounded` 4px). Tap targets: Reset to Defaults 36px, Save Settings 36, Create Template 36, "Add variant" 16, toggles 56x44. The personality textarea is 118px tall with a scroll thumb, so most of a long prompt is hidden.
- **Desktop light:** panel and form read well; disabled Save Settings is pale but legible. A brief flash of the empty-prompt placeholder ("e.g. You are a fri...") appears before the personality config loads (observed on mobile, measured at load time): no skeleton.
- **Not checkable (no data):** Preview modal behaviour, group collapse edit loss, empty-variant save, "Matched by" aggregation, Delete confirm text, card layout on mobile. These stay [src].

---

### Matcher Debug (`/matcher-debug`, `src/pages/MatcherDebugPage.tsx`)

- **Purpose and primary task:** a developer/tuner tool: type a phrase, see how the hybrid (lexical plus embedding) search resolves it across floors, areas and entities, and tune parameters. The user is the person asking "why did Lucia pick that room/device".
- **Scores:** Nielsen 19/40. At 1 or below: *Match with the real world 1* (hybrid score, embedding similarity, dropoff, disagreement penalty, "Embed. resolution", T/W/D/P/M); *Help and documentation 0*; *Recognition rather than recall 1* (T=0.55 W=0.4 D=0.8 P=0.4 M=0.1 summary at `:441` needs a legend); *Error prevention 1* (unbounded number inputs, `min/max` unenforced). Cognitive-load failures: 5/8 (about 9 tunable parameters; 3 result sections plus a strategy panel; unexplained terms; 4 to 5 decimal scores everywhere).
- **Source check on safety:** the search is `GET /matcher-debug/search/{term}?...` (`api.ts:1275-1291`); it only reads, so it is a pure lookup. **Not exercised anyway**: the coordinator stopped live work before I reached it. The page also issues `GET /entity-location/visibility/available-agents` and `GET /skill-optimizer/skills` on mount (`:111-126`), both reads.
- **Desktop findings** ([src] unless marked; unlabeled inputs, header text, cryptic T/W/D/P/M line and no pre-search state are now [live], see Live pass)
  - Header: "Hierarchical search — walks floors → areas → entities, showing hybrid scores at every level." (`:201`). Accurate, but it does not say what to do, what "good" looks like or when to use the page. No empty state before first search: the page below the search card is blank.
  - The query input has only a placeholder, no label (`:214-220`); the agent `<select>` has no label (`:236`).
  - The first (live) skill's config is silently auto-applied (`:114-124`); "Using live Light config" only appears when Tuning options is expanded (`:292-296`). Tuning defaults (0.55, 0.4...) therefore may not be what the label shows, and a user who never opens the panel does not know which parameters the results used except via the cryptic `T= W= D= P= M=` line (`:441`).
  - Changing any tuning value silently clears the preset highlight (`:305-337`) but keeps the values; fine, but there is no "reset to live config".
  - Agent impersonation: choosing an agent also overwrites the domain filter (`:141-149`), without saying so. Chip "X view" appears (`:249-254`).
  - Result density: summary strip, strategy panel (strategy badge, reason, three big score tiles), then Floor, Area, Entity, Resolved sections each with Hybrid and Embedding bars to 4 decimals (`:62-78`, `:595-596`). Hybrid and Embedding both repeated per match; the bar adds little over the number. For many entities, the table has aliases chips on every row (`:585-591`), 10px text everywhere (`:479,510,584,588`).
  - Color semantics: strategy Entity=info, Area=sage, Floor=amber, and "Best Entity Score" uses dynamic classes `text-${color}` and `bg-${color}` (`:482,487`). Tailwind cannot see constructed class names; it works only if those exact classes appear elsewhere (`sky-400` is remapped to the info token in `index.css:91-93`). Fragile.
  - The no-results copy suggests "lowering the threshold" (`:664`) but the user may have a stale agent or domain filter; the cause is more likely filters.
  - Failure copy: the error banner shows the raw message "Failed to search matcher debug: <statusText>" (`api.ts:1289`, `:405`).
  - `text-bright`, `text-mist` tokens used for scores; raw `sky-400` classes (`:56,250,425,562`) bypass the info token name (audit item 8). Buttons `rounded-md`/`rounded-lg`.
- **Mobile findings** [src]
  - `grid-cols-5` for the tuning inputs with no responsive breakpoint (`:300`): at 375px each field is about 55-60px wide, so numbers with labels like "Disagreement penalty" and "Embed. resolution" will wrap and clip. Likely the worst mobile defect on the four pages.
  - Resolution tiles `grid-cols-3` (`:472`) with `text-lg` 4-decimal numbers: about 100px each; tight but fits.
  - Entity table has `overflow-x-auto` (`:563`, `:620`) and 5-6 columns; the Entity cell stacks name, ID, alias chips, so it will scroll sideways to see scores. Agent select row and skill preset row do not wrap (`:233`, `:271-291`) and can push past the viewport.
  - `T=... W=...` line is `ml-auto font-mono` inside a flex-wrap row (`:440`), likely wraps under the summary.
  - Search form `flex gap-2` with a 40px+ button is fine.
- **Pain points and friction (ordered)**
  1. **P1** Jargon without explanation: hybrid score, embedding, dropoff, disagreement penalty, "Embed. resolution", T/W/D/P/M (`:302-340`, `:441`). A first-timer cannot use the tuning panel or interpret the summary. Fix: one-line tooltip or helper text per parameter ("Minimum score to count as a match"), spell out the parameter line as chips ("Threshold 0.55"), add an "Explain this result" sentence beside Strategy based on `resolution.reason`. Command: clarify.
  2. **P1** Tuning grid unresponsive: `grid-cols-5` (`:300`). Fix: `grid-cols-2 sm:grid-cols-3 lg:grid-cols-5`. Command: adapt.
  3. **P2** Unlabeled query and agent controls (`:214`, `:236`).
  4. **P2** Silent auto-applied preset and silent domain overwrite by agent (`:114-124`, `:141-149`). Fix: show the active config in the results header and "domains set by Light agent" note.
  5. **P2** No pre-search state or example chips; add 2-3 clickable examples from the existing placeholder ("kitchen lights", "downstairs") and a one-paragraph purpose line.
  6. **P2** Density: collapse Floor/Area sections by default when empty (already hidden), show top 5 entities with "Show all N", merge hybrid and embedding into one row of numbers, drop 4-decimal precision to 2 except in a details view (`:62-78`).
  7. **P3** Dynamic Tailwind class construction (`:482,487`); raw `sky-400`; 10px text; `Eye`/`EyeOff` icons with no text and no `aria-label` for the Visible column (`:599-603`, `:645-649`), color-only state.
- **Bugs and errors:** none verified live. Suspects: constructed classes (`:482,487`); mobile grid overflow (`:300`).
- **Performance notes:** each search is one GET; no debounce (submit only), good. Mount fires two reads.
- **Suggested changes:** responsive tuning grid with help text; labeled search; example chips; result header showing parameters used and agent/domain filters in words; per-score tooltip; collapse "Resolved from Hierarchy" until opened.
- **Not exercised:** the search itself, agent select, preset buttons, domain chips, all result rendering, light theme, mobile (all live checks).

#### Live pass (2026-10-14)

Ran one query ("kitchen lights", defaults threshold 0.55, weight 0.4, dropoff 0.8, penalty 0.4, margin 0.3, pure GET `/api/matcher-debug/search/kitchen%20lights?...`). Light theme.

- **Search took about 35 s with only a spinning icon in the Search button and no other progress text**, then returned 200 with zero results: "0 direct matches, 0 resolved entities", "Resolution Strategy: None", "No matches at any level", and "No matches found for 'kitchen lights'. Try lowering the threshold or adjusting the embedding weight." The instance has no entity cache, so the correct advice (populate or sync entities) is not given; the copy blames the tuning, which cannot help. **CONFIRMED live (P2):** the no-results copy misdirects (`:664`). **New P1:** a 35 s blocking request with no timeout, cancel, or elapsed-time text (the button is disabled, the input stays editable); likely embedding-model warmup or an empty-index scan. The result summary line also prints `T=0.55 W=0.4 D=0.8 P=0.4 M=0.3`, CONFIRMED live (cryptic).
- **CONFIRMED live:** no pre-search guidance (blank below the card); query input and agent select unlabeled (`labels.length` 0, no `aria-label`). Agent select lists 6 agents with their domains in the option text. "Using live Light Control config" is shown as 9px text at the far right of the preset row, beside the preset buttons, so the auto-applied preset is visible when Tuning is open (only), as predicted.
- **Desktop light 1440x900:** clean; tuning card is a flat 5-column row at ~110px per field, fine. No contrast problems seen.
- **Mobile 375x812 (CONFIRMED live, P1):** after expanding Tuning options, each of the 5 inputs is **49x34 px**; "Threshold 0.55" renders as "0.5" (value clipped), the labels "Disagreement penalty" and "Embed. resolution" overlap ("DisagreemenEmbed."), labels wrap onto two lines so the inputs sit at different heights, the agent select is 381px wide inside a 342px card and is clipped on the right, and the skill-preset row ("Light Control", "Security Control", ...) overflows sideways (chips are 64-73x42) behind a horizontal scrollbar. Page `scrollWidth` stays 375 because the overflow is clipped inside the card. Domain chips 26px tall (under 44). Search button 108x42.
- **Tablet 768x1024 (CONFIRMED live):** the persistent 255px sidebar leaves about 510px, so the `grid-cols-5` row is cramped: labels wrap to two lines and stagger the inputs, preset chips run past the right edge ("Senso..."), and a horizontal scrollbar appears under the card. So the breakpoint problem starts at tablet, not only at phone width: use `grid-cols-2 sm:grid-cols-3 xl:grid-cols-5`, and wrap the preset row.
- **Network:** two mount reads (`/api/entity-location/visibility/available-agents`, `/api/skill-optimizer/skills`) plus the search; no errors.
- **Not exercised:** result rendering with data (floor/area/entity sections, strategy tiles, dynamic `text-${color}` classes `:482,487`), because no entities exist; agent select change, preset buttons and domain chips (they only change local state but would alter the next query; not needed).

---

## Cross-page observations (this group)

- All four pages share the audit's systemic issues, found at these sites: div modals without semantics (`AlarmsPage.tsx:599,821`, `ResponseTemplatesPage.tsx:144`); native `confirm()` (`PromptCachePage.tsx:144,163`); unlabeled fields on all four; raw palette (`teal-*` `AlarmsPage.tsx:557`, `sky-400` `MatcherDebugPage.tsx:56`); 4px `rounded` on selects (`ResponseTemplatesPage.tsx:472`).
- Concepts are never introduced. Cache, templates, matcher and alarm "Target Entity" all assume the reader already knows the model. Every page needs a purpose sentence and per-term help; Prompt Cache and Matcher Debug have no help at all.
- Error and empty states collapse into one: Alarms (load fail looks like "no alarms", `:111,406`), Prompt Cache (error plus "No entries", `:209,212`), Templates handles it correctly with a Retry (`:748-757`), a pattern to copy.
- Four different header layouts: Alarms `max-w-5xl` with subtitle, Prompt Cache bare h1, Templates with subtitle and action, Matcher with icon in h1 (`:362`, `:176`, `:702`, `:197`). Matcher uses `tracking-tight text-bright`, others `text-light`.
- Hover-only affordances and sub-44px targets are common (Alarms actions, Templates chips, X buttons).
- Dense data presented as wide tables with no mobile treatment (Prompt Cache, Alarms Sounds, Matcher entities).
- Live environment issue worth fixing: the dashboard opens many long-lived connections per tab (the shell's `/api/tasks/background` SSE plus its repeated polling, observed 80+ GETs in seconds). With HTTP/1.1 and Chrome's 6-per-host limit, a few tabs stall page loads (navigations hung 300s, pages stuck on "Loading..."). A real user with several tabs open will hit this. Serve over HTTP/2, share one stream per browser via `BroadcastChannel`, and stop polling while SSE is connected.

## Top 5 for this group

1. Replace all hand-rolled dialogs here (Alarm form, Sound form, Template preview) with one accessible `Modal` (role, focus, Esc, backdrop, inert background). [P1, observed live on Alarms]
2. Fix the one-time-alarm timezone round-trip (`AlarmsPage.tsx:160` and `:203`), and add time/cron validation with a plain-language schedule preview. [P1, source]
3. Explain the concepts: purpose sentences and term help for Prompt Cache (hit rate, router vs agent), Templates (token syntax, variants), Matcher (hybrid score, threshold, dropoff). [P1/P2]
4. Make Prompt Cache honest and safe: error state replaces the table with Retry, resolve the stats-versus-empty-table contradiction seen live, confirm Evict and Clear All with counts through `ConfirmDialog`. [P1]
5. Make Matcher's tuning grid responsive (`:300`) and Alarms' hidden actions reachable by keyboard and touch (`:466-498`). [P1]

## Counts

P0: 0. P1: 10 (Alarms 3, Prompt Cache 2, Templates 2, Matcher 2, plus the cross-page connection issue). P2: 17. P3: 9.

## Live pass summary

Serial pass on 2026-10-14, one tab, light theme, 1440x900 / 768x1024 / 375x812.

- **Confirmed live:** 14 (Alarms dialog: no role, no Esc, no backdrop close, focus escapes to sidebar, hour 99 accepted, no cron validation, unlabeled fields, Sound dialog same; Prompt Cache stats-vs-empty contradiction and mobile layout; Templates section order and unlabeled fields and unknown tokens unflagged; Matcher unlabeled inputs, cryptic summary line, misleading no-results copy, tuning grid defects at 375 and 768).
- **Refuted/corrected:** 1 (focus ring is present on sidebar links; the gap is in page controls only).
- **New:** 5. (1) P1 Prompt Cache: server stats say 4 entries per cache while list endpoints return `[]`, and Clear All is disabled on the empty list, so the phantom entries cannot be cleared. (2) P1 Matcher: the search blocks about 35 s with only a spinner, and its no-results advice is wrong for an empty entity index. (3) P1 Templates: empty first-run state with defaults only reachable through the destructive-looking Reset to Defaults. (4) P2 Templates Add form: skill list shows doubled raw class names, only some skills, internal regex "Matched by" text. (5) P2 Alarms first load stalled 15-20 s with no timeout UI.
- **Evidence tags changed to live:** the Alarms dialog, hour and cron validation, label and Prompt Cache contradiction items above; the unlabeled-input items on all four pages; Matcher tuning grid at 375 and 768.
- **Still unverified (need data or an error state):** Alarms card actions, edit dialog, timezone round-trip bug (`:160`,`:203`), load-failure-looks-empty, Sounds table overflow; Prompt Cache error-plus-empty (`:209`), Evict; Response Templates editor cards, Preview modal, group-collapse loss, empty-variant save; Matcher result sections, constructed Tailwind classes (`:482,487`). Dark theme was not rechecked this pass.

## Not exercised (summary)

All live checks except: Alarms desktop (dark) empty state and New Alarm dialog open/Esc/backdrop/hour input/Custom/Advanced/cancel, Alarms Sounds tab empty state, Alarms New Alarm dialog on mobile (375), Prompt Cache first view on mobile (375, dark). Not done: light theme anywhere, 1024 and 768 viewports, any save, delete, evict, toggle, preview, search action; Response Templates and Matcher Debug were never opened live (connection exhaustion, then the coordinator's stop instruction). A serial live pass should cover Templates and Matcher first, then recheck the Prompt Cache stats-vs-table mismatch.
