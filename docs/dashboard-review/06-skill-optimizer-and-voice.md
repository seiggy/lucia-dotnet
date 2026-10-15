# Dashboard review 06: Skill Optimizer and Voice Platform

Evidence key: **[live]** = seen in the browser; **[src]** = inferred from source only. Live access was cut short. I got one desktop pass (1440x900, dark) of Skill Optimizer before the browser's per-host connection limit starved my tab, then both pages stopped loading. Voice Platform was never seen live. Mobile was never seen live for either page. Everything about Voice and everything about mobile is [src]. The impeccable detector returned `[]` for both files, so it adds nothing.

---

### Skill Optimizer (`/skill-optimizer`, `src/pages/SkillOptimizerPage.tsx`)

- **Purpose and primary task:** Tune five hybrid-matcher parameters for a skill (threshold, embedding weight, score drop-off, disagreement penalty, resolution margin) by building a test-case set, running a search, and applying the result to config.
- **Scores (Nielsen 21/40):** Visibility 2, Match with real world 2, User control 2, Consistency 2, Error prevention 1, Recognition 3, Flexibility 2, Aesthetic/minimal 3, Error recovery 2, Help 1... summed with rounding to 21 (a rough estimate; this page was reviewed only partly live).
  - Error prevention 1: "Apply Results" writes config with no confirm, no diff, no undo (`:445-451`, `:217-243`). "Find Optimal Values" is enabled with only a blank row (`:431`).
  - Help 1: no explanation of what the five parameters mean, what "score" is, or how many cases are enough.
  - Cognitive-load failures: 3 of 8 (working memory: 5 unexplained parameters as raw decimals; minimal choices: 4 toolbar buttons of equal weight; priority/hierarchy: run controls sit below a tall table).

**Desktop findings**
- [live] First viewport is two cards (Skill + current params, Embedding Model), then a Test Cases card. For "Light Control" the cache line read "0 devices cached", so Auto-generate was disabled. A first-timer sees an empty table with no cause or next step except "import from traces or add manually".
- [live] The Test Cases card is `flex-1` (`:320`, parent `minHeight: calc(100vh - 8rem)` at `:251`), so with 1 row it still stretches to the full viewport. This leaves a large empty panel and pushes "Find Optimal Values" and the results to the bottom.
- [live] Both selects expose the same accessible name "Select an option" (CustomSelect falls back to that, `CustomSelect.tsx:142`). The visible `<label>`s at `:289,:307` have no `htmlFor`, so they label nothing.
- [live] No console errors. Network: `/api/tasks/background` polled in a tight series (about 107 requests in the first minute, many pending). That is TaskTracker, not this page, but it dominates this page's traffic.

**Mobile findings** [src only; not seen live]
- Toolbar `:325-353` has 4 buttons in a `flex gap-2` with no wrap next to an h2. At 375px the content width is about 300px and the buttons need about 410px, so it will overflow or crush. Expect horizontal overflow.
- The test-case table (`:362`) has 5 columns and an `EntityMultiSelect` per row inside `overflow-auto`. It will scroll sideways inside a small card. Rows are not usable as cards.
- Results grid is `grid-cols-3` with no breakpoint (`:521`). Three cards of about 90px each hold `text-lg` 6-digit values and uppercase labels like "Embedding Resolution Margin", which will wrap badly.
- Row delete is a bare "×" with no padding (`:411-416`); the measured toolbar buttons were 28px tall, well under 44px.

**Pain points and friction (ordered)**
1. **P1 `:217-243` Apply Results is a silent config write.** No confirmation, no before/after confirmation beyond small strikethrough numbers (`:572-574`), no revert. A bad optimization degrades matching for every user. Fix: confirm dialog (ConfirmDialog) listing the five old to new values; keep the previous values in a toast with "Undo". Command: `harden`.
2. **P1 `:170-174` and `:431` Start is enabled for invalid sets.** Trace-imported cases have `expectedEntityIds: []` (`:116`) and are silently dropped by the filter at `:170`. If all are trace cases the user gets a toast "At least one test case with search term and expected entity is required". Fix: disable Start until valid, show "N of M cases ready" and mark incomplete rows. Imported trace cases need an obvious "needs expected entity" state. Command: `clarify`.
3. **P1 `:183-197` Polling failures and running state are invisible.** A failed poll silently clears the interval (`:193-196`), leaving the UI at "running" forever. Reloading the page loses `jobId` (state only), so a long run cannot be resumed or cancelled. Fix: show "Lost contact with job" with Retry; persist `jobId` in the URL or sessionStorage. Command: `harden`.
4. **P2 `:456-488` Progress is unlabelled and hard to read.** `T= W= D= P= M=` letters with no legend; the bar has no `role="progressbar"`/`aria-valuenow`; "Optimizing..." is not an `aria-live` region. Fix: show named values in a small table, add progressbar semantics.
5. **P2 `:583` Status is emoji and color only** (✅ ⚠️ ❌, sage/amber/ember scores `:600,:605`). `maxScore = 4` is hard-coded with a magic comment (`:582`). Fix: icon plus text ("Perfect/Partial/Missed"), `aria-label` on the cell; take the max from the API. See audit item 13.
6. **P2 `:397` `parseInt(...) || 3` makes the Max field uneditable.** Clearing the field snaps to 3 (new rows default to 1 at `:133`). Typing "1" after backspace is awkward. Fix: allow empty while editing, clamp on blur.
7. **P2 `:376-382,:392-399` Row inputs have no label** (placeholder only on the term). Same for the delete button (`:411`, no `aria-label`). Matches known systemic issue (audit P1 #4). Fix: `aria-label` per row.
8. **P2 `:254-261` Page lacks orientation.** No explanation of when to run an optimization, how long it takes, or whether it affects live traffic. Add a short "How this works" line and run-time estimate. Command: `onboard`.
9. **P3 `:366 vs :256`** h1 style and page chrome differ from sibling pages (audit P2 #7). Buttons use `rounded-lg`/`rounded` against the 12px spec; all `text-xs`.
10. **P3 `:155-162` Export Dataset** downloads silently through a synthetic anchor; fine, but the filename and contents (entities list can be large and includes real entity IDs) are not previewed.

**Bugs and errors**
- `:232-239` `setTimeout` re-fetch after apply is not cleared on unmount and swallows errors; stale `currentParams` shown for 2s after "applied" toast.
- `:81-86` Device fetch has no loading or empty-cause state; failure is only a transient 4s toast.
- `:25-29` Toast container is not `aria-live` (audit P1 #5).
- Results use array-index keys (`:550`) with no sort or filter; with 200 trace cases the 320px scroll box is the only navigation.

**Performance notes:** [src] 200-trace import is capped at the API call and rendered as one `EntityMultiSelect` per row (200 comboboxes mounted). `EntityMultiSelect` receives the full `devices` array per row. Likely sluggish at 200+ rows; virtualize or switch to a single-row editor.

**Suggested changes:** `clarify` (parameter help, status text), `harden` (confirm Apply, job resume, validation), `layout` (stop the test-case card from `flex-1`; sticky run bar), `adapt` (mobile card rows, wrap toolbar, 1-col results), `polish` (tokens/radii).

**Not exercised:** Auto-generate, Import from Traces, Export Dataset, Find Optimal Values, Cancel, Apply Results (all change state or call the server). I clicked "+ Add" (local state only) and removed nothing. Light theme, mobile, tablet not seen. Skill dropdown contents not opened.

#### Live pass (2026-10-14)

Serial pass on tab `seed`, light theme only, 1440x900, 768x1024 and 375x812. Instance has 0 devices cached (Home Assistant data absent), so Auto-generate and Export are disabled and the test table is empty. No run, no Apply, no row added. Pages loaded in under 3s; no stalls.

- **Desktop light:** [live] Renders cleanly; no contrast leaks. Toolbar buttons are 28px tall (`text-xs`). Disabled buttons (Auto-generate, Export, and the primary "Find Optimal Values") are drawn at 0.4 opacity with no reason shown; "Find Optimal Values" disabled reads as faint amber with near-white text. "Import from Traces" is sage-tinted while "+ Add" is amber, so the primary action in the toolbar is unclear. Page document height is exactly 900px, so the `flex-1` Test Cases card fills the viewport with an empty panel (CONFIRMED live, but with zero rows: the card is stretched by design even when empty).
- **Keyboard:** [live] Tab skips disabled buttons and lands on "Import from Traces" with a visible 1px amber `auto` outline (`:focus-visible` true). The skill CustomSelect opens a `role=listbox` with 7 skills, Escape closes it and returns focus to the trigger. REFUTES "no focus-visible" for this page; audit systemic claim does not hold here.
- **Mobile 375 (toolbar overflow):** [live] REFUTED as horizontal overflow: `scrollWidth` 375 = `innerWidth`, no clipped elements. But the toolbar is crushed: the four buttons are 60px tall and 68-75px wide, labels wrap to 2-3 lines ("Auto- / generate", "Import / from / Traces"), "Test Cases (0)" wraps to two lines, and "+ Add" sits flush against the card's right edge (right=354 vs card 359). Row tap targets: selects 38px, "Find Optimal Values" 40px; toolbar buttons 60px tall pass 44 but are only ~70px wide. P2, `SkillOptimizerPage.tsx:325-353`: allow the toolbar to wrap (`flex-wrap`) and put the h2 on its own line.
- **Tablet 768:** [live] No overflow (`scrollWidth` 768). The persistent 255px sidebar leaves ~513px content; the two top cards sit side by side with the skill parameters wrapping ("Embedding Resolution Margin: 0.3" onto two lines); the toolbar buttons are 44px tall, two-line labels, widths 48-103px. The empty Test Cases card is about 300px of dead space. Acceptable but cramped.
- **Confirmed live:** both selects share the accessible name "Select an option" and the visible `<label>`s have empty `htmlFor` (labels `Skill`, `Embedding Model`); no `role=progressbar` anywhere on the idle page (running state not checkable). Empty-state copy "No test cases yet. Auto-generate from devices, import from traces, or add manually." is shown while two of those three options are disabled/empty because of "0 devices cached" with no explanation of why or how to fix it (P2, new): the "0 devices cached" fact sits in grey 12px text under the embedding model, far from the disabled button.
- **Not checkable live:** Apply Results confirm/undo, poll-failure state, trace import, row inputs, results grid at 375 (needs a run), parseInt Max field, 200-row performance.
- **Console/network:** [live] no console errors. `/api/tasks/background` polled hundreds of times over the session (about 500 requests logged across Voice and Optimizer); never errored.

---

---

### Voice Platform (`/voice-platform`, `src/pages/VoicePlatformPage.tsx`, 1832 lines)

- **Purpose and primary task:** Wyoming speech stack control room: check engine readiness, install/activate speech models (STT, VAD, wake word, speaker verification, enhancement), manage speaker profiles and their audio clips, enroll speakers via a mic wizard, list wake words, and watch live voice sessions. The primary task for a first-timer is "get voice working", which the page spreads across 3 tabs.
- **Scores (Nielsen 19/40, source-only estimate):** Visibility 3, Match 2, Control 2, Consistency 2, Error prevention 1, Recognition 2, Flexibility 1, Aesthetic 2, Error recovery 2, Help 2.
  - Error prevention 1: deletes use native `confirm()` with the wording "Delete this local model?" (`:664,:704`) and no object name; Save settings with a destructive-adjacent retention value has no bounds feedback (`:1178`).
  - Flexibility 1: five top tabs plus five model sub-tabs, all in component state; no deep links, no URL state, no shortcuts.
  - Cognitive-load failures: 5 of 8 (too many choices on Models and Profiles; no single-focus task path; mixed global/local settings on Profiles; jargon: Wyoming, VAD, KWS, ONNX, sherpa, diarization; weak progress/state memory since tab state is lost on reload).
- **Tab and section inventory (all [src])**
  1. **Status** `:944-1000`: Readiness card with 6 `StatusTile`s (STT, VAD, Wake Word, Speaker Verification, Speech Enhancement, Custom Wake Words), ONNX provider line, "Next steps" 8-item checklist. Tiles open the Models tab via "configure". Custom Wake Words tile has no `onConfigure`.
  2. **Models** `:1002-1088`: engine sub-tabs (stt/vad/kws/speaker-embedding/speech-enhancement); STT has Language/Architecture selects and Streaming-only filter; installed and catalog lists of `ModelCard` with Download / Activate / Delete.
  3. **Profiles** `:1090-1386`: Voice Recognition Settings form (4 toggles, 3 numeric/range), "Enroll New Speaker" wizard (name, optional wake phrase, prompt, 16-bar recorder, notes), profile cards with Rename, Show/Hide clips, Delete, Promote, Merge, per-clip `<audio>`, Reassign, Delete clip, View memories link.
  4. **Wake Words** `:1388-1430`: list with Delete; "Add Wake Word" form that does not create anything, it jumps to the Profiles enrollment wizard (`:891-902`).
  5. **Monitor** `:1432-1512`: SSE connection indicator, per-device session cards (state, voices, level bar), live transcript feed, transcript history with expandable pipeline stage table.
  6. **`/voice-onboarding`** (`App.tsx:337`): `<Navigate to="/voice-platform" replace/>`. It lands on the Status tab, not Profiles with the enrollment wizard open. Links from other places (command trace pages mention `voice-onboarding`, `CommandTraceListPage.tsx:12`, `CommandTraceDetailPage.tsx:19`) therefore lose their intent. Fix: redirect to `/voice-platform?tab=profiles&enroll=1` and read it.
- **Destructive-looking controls by source (none clicked)**
  - Delete local STT/engine model `:664,:704` (native confirm, no name, no size/consequence, no "this is the active model" guard other than hiding the button for the active one `:1567`).
  - Delete speaker profile `:730-739` (confirm "Delete this speaker profile?"; does not say clips and voiceprint are deleted too, or that the wake word linked to it may break).
  - Delete wake word `:769`, delete audio clip `:796`.
  - Merge profiles `:823-837` (confirm says "cannot be undone", the best copy of the six; but it fires from a `<select onChange>`, so one mis-click on the dropdown option is the commit).
  - Reassign clip `:808-821` has no confirm and also fires from `<select onChange>` (`:1357`).
  - Promote profile `:759-767` changes authorization (`isAuthorized: true`), a security-relevant action (grants a voice speaker identity trust), with no confirm.
  - Save settings `:717-728`; toggles apply to local state only until Save, but there is no dirty indicator or leave-guard.
  - Start enrollment / record / upload sample `:839-889`; model Download/Activate (Activate swaps the live STT model with no warning about interrupting active voice sessions).

**Desktop findings [src]**
- Hero block (`:906-925`) with a 28px radius, glow gradients and a pill eyebrow "Voice platform" takes roughly 200px of vertical space before any tab, and the h1 ("Wyoming control room") differs from the nav label "Voice Platform". It repeats the product name three times. It also adds 28px/24px/20px radii, off the 12px control spec in DESIGN.md (a hero-card exception may be defensible, but 24px on every list card is not).
- Everything is one 1832-line component with about 45 `useState` calls and every handler defined inline (`:114-902`). State for all five tabs is mounted at once, and all five load on mount (`:487-502`) even if the user only opens one tab.
- The tab bar (`:929-942`) uses plain buttons with no `role="tablist"/"tab"`, `aria-selected`, or arrow-key handling. Same for the Models sub-tabs (`:1004-1025`). Screen-reader users get 10 unrelated buttons and no indication of the active one (the only active cue is color, `:935`).
- Active tab is not in the URL, so Back/refresh/share lose it, and the Notice banner (`:927`) is not `role="status"` or `role="alert"` and is not cleared on tab change (it persists, can describe an action from another tab).
- Models: the "Installed" and "Available catalog" sections each render full cards (title, 3 pills, description) in a 2-column grid. A catalog with dozens of models becomes a very long page with no search, no sort and no pagination; the only filters exist for STT, none for other engines (`:1061-1086`).
- Status semantics are color plus icon on StatusBadge (`:1633`), but `StatusTile` (`:1602-1630`) differentiates ready/loading/not configured by tint alone: icon chip sage vs amber vs stone and subtitle color. The subtitle text does include "(loading…)" and "Not configured", which helps, but a "ready" tile only shows the model name with no word "Ready". "Next steps" checklist items (`:1637`) use the same Check icon for done and not done; only the color differs (sage vs stone). That is a WCAG 1.4.1 failure. Fix: different icon (empty circle vs check) and `<span class="sr-only">Done/To do</span>`.
- `:1436` Monitor "Live" dot is color only (sage vs dust) plus text, OK; but it pulses without a reduced-motion guard (audit P2 #10). "Connecting..." never turns into an error state: `es.onerror` only sets false (`:272`), so a dead stream reads as "Connecting..." forever.
- Monitor device cards show a raw `remoteEndPoint` (IP:port) as the device name (`:1448`). There is no friendly name, no area, no last-seen and no way to act on a satellite (assign, rename, disconnect). "Satellite/device management" in the brief effectively does not exist on this page; it is a read-only list of live sockets, and devices disappear the moment they disconnect (`:207-214`), so there is no offline history.
- Level bar (`:1456-1461`) is a plain div with no `role="meter"`/`aria-valuenow`; value is `rmsLevel*500` clamped, which saturates at 20% of full scale and may look permanently full for loud rooms. The transcript feed (`:1470`) is not `role="log"`/`aria-live`; a screen reader hears nothing.
- Transcript feed is capped at 100 entries (`:250`) and auto-scroll is absent, so new lines append below a `max-h-96` scroll box and the newest line is out of view unless the user scrolls (inferred; no `scrollTop` handling found).
- Raw palette colors: `bg-emerald-900/60 text-emerald-400`, `bg-stone-800/60 text-stone-400` (`:1479,1783`), `text-sky-300 bg-sky-500` (`:1645,1652`), `text-cyan-300` (`:1594`) bypass tokens and the light theme overrides (audit P2 #8). Hard-coded `rgba(245,158,11,..)` and `rgba(34,211,238,..)` gradients at `:907`.
- Text size: `text-[10px]` (`:1479,1783`) and `text-[11px]` (`:910`), plus `text-xs` for most metadata (clip details, thresholds). Below 12px for operational data.

**Audio and media accessibility [src]**
- `:1350-1351` The `eslint-disable-next-line jsx-a11y/media-has-caption` suppresses a rule for a plugin that is not installed (audit P2 #12). The real question: are captions needed? These are speaker voice samples, not speech content published to users, so a caption **track** is not practical, but the transcript is already available in the data (`clip.transcript`, truncated with `title` only at `:1342`). Recommendation: do not add `<track>`; instead show the transcript as visible text (wrap, not truncate) next to the player and set `aria-label="Play clip from {date}"` on the `<audio>`. Remove the stale disable comment. Severity P2.
- `<audio controls className="h-8">` (`:1351`) forces a 32px player; native controls render poorly clipped at that height in some browsers and the volume/seek controls become tiny on touch. Use the default height or `h-10`+.
- `preload="none"` is correct (avoids fetching all clips). One `<audio>` per clip, so nothing stops several from playing at once. Fix: pause others on `play`.
- Clip playback hits `GET /api/speakers/{profile}/clips/{clip}` directly via `src` (`api.ts:1976`), so any cookie-auth failure or 404 shows a dead player with no message. Add an `onError` handler.
- Recorder (`RecorderCard` `:1583-1600`): the 16-bar meter is decorative `div`s with no `aria-hidden`, and recording state is shown only by a Badge whose color changes rose/sage (text also changes, which is fine). The "Stop and upload" button is the only way to end recording, with no cancel/discard, no max length and no countdown; the page then uploads immediately, so a user cannot review before upload. Also `getUserMedia` permission is requested on first Record click, which is right, but denial messaging is reasonable (`:612-618`).
- `createScriptProcessor` (`:595`) is deprecated; replace with AudioWorklet before browsers drop it. P3.
- `meterLevel` updates state every animation frame (`:550`), re-rendering the whole 1800-line component about 60 times a second while the mic is open. With the Status/Models/Profiles tree mounted this is a real performance cost on the Jetson-class browser. P2: isolate the meter into its own component with a ref/canvas.

**Mobile findings [src; none seen live, widths 375 and 768 not tried]**
- Tab bar `flex flex-wrap` with `flex-1` buttons and `px-4` (`:929-942`) will wrap into 2-3 rows at 375px, shifting content down on each tab change. Better: horizontally scrolling segmented control or a select.
- Models sub-tabs (`:1004`) are `flex gap-2` with no wrap and 5 long labels ("Voice Activity Detection"); this overflows horizontally at 375px, so tabs 3-5 are off screen with no scroll affordance (the container has no `overflow-x-auto`). Likely P1 on mobile.
- Model cards (`:1549-1569`): title block and action buttons are in one `flex` row with `shrink-0` buttons. "Download" plus Delete icon button take about 150px and the text column gets about 100px at 375px, so names and descriptions wrap to a narrow strip. Stack actions below on mobile.
- Profile card header (`:1248`) puts name and badges beside 3 icon buttons (`p-2` about 34px, not 44px). IconButton is `p-2` with a 16px icon (`:1660`), about 34px. Edit mode puts the input, Save and Cancel in one row (`:1251-1261`), which will overflow at 375px.
- Merge/Move selects use `!w-auto` inline next to Cancel (`:1301,:1355`); fine but options with long names will push Cancel off screen.
- Clip row: metadata line `text-xs` with 5 segments wraps; `<audio h-8>` full width OK.
- Monitor: transcript feed lines are `flex gap-2` with `shrink-0` time, session id, badge and speaker, so only a sliver of width remains for the actual text at 375px. History rows `truncate` the text (`:1788`) and a 2-column stage table at `:1808` could overflow. Make each entry a wrapped two-line block on mobile.
- Hero (`:906`) `px-6 py-7`, metric cards stack; fine. The 28px radius card plus `max-w-7xl` consumes vertical space before the first tab.
- Status grid: `sm:grid-cols-2` then `xl:grid-cols-[1.2fr,0.8fr]`; on 768-1279px "Next steps" falls below the 6 tiles, so the action list is far from the status that drives it.

**Pain points and friction (ordered)**
1. **P1 `:664,704,730,769,796,826` Six native `confirm()` calls** (audit P1 #3). Wording is generic ("Delete this local model?" with no name, size or consequence; profile delete does not say clips go too). Use ConfirmDialog via a `useConfirm()` hook. Name the object, state the consequence, make delete-profile and merge type-to-confirm. Command: `harden`.
2. **P1 `:1090-1386` Profiles tab mixes three jobs** (global recognition settings, enrollment wizard, profile management) in one scroll with a Save button far from the toggles that matter. Settings belong in their own place (Settings sub-section or separate route). Command: `distill` / `layout`.
3. **P1 `:944-1512` Tabs are not routes and not ARIA tabs.** No deep link (`/voice-platform/models`), no Back support, no `role=tablist`; onboarding redirect cannot target the wizard. Make Status/Models/Profiles/Wake words/Monitor child routes (or `?tab=`) and split the file: `StatusTab`, `ModelsTab`, `ProfilesTab`, `WakeWordsTab`, `MonitorTab`, plus `useVoiceRecorder` hook (`:538-634`) and audio helpers (`:1712-1774`). The project rule is one class per file; components follow the same spirit. Command: `extract`.
4. **P1 `:1303,:1357` Merge and Reassign execute on `<select onChange>`.** Picking an option immediately commits (merge asks confirm, reassign does not). A keyboard user arrowing through options in some browsers fires `change` on every arrow. Use a select plus an explicit "Merge"/"Move" button.
5. **P2 `:759-767` Promote to enrolled has no confirm or explanation** of what authorization grants.
6. **P2 Status by color alone** (`:1612,1637`) as described above.
7. **P2 `:272` SSE failure is silent**; "Connecting..." never becomes "Disconnected, retrying". `EventSource` auto-reconnects but the UI can't tell the user when it gave up (state `CLOSED`).
8. **P2 `:1197-1204,:1395-1405` Wake Words is a dead end.** "Add Wake Word" opens a form that does not add one, then switches tab and posts a notice (`:901`). It is also unclear whether deleting the wake word affects the enrolled speaker. Rename to "Enroll with wake word" or put wake phrase directly in the enrollment wizard and make this tab read-only with a link.
9. **P2 `:1166` "Provisional default: 0.35"** is hard-coded copy; if the server default changes, the text lies. Read it from config.
10. **P2 `:1133-1136` Disabled "Speech enhancement" checkbox** is a status, not a setting; the explanation lives only in `title` (not available to touch or screen readers). Show helper text.
11. **P2 Labels:** most inputs lack labels (`:1215-1216` placeholder-only, `:1403-1404`, `:1253`), checkboxes in `<label>` wrapper are fine, selects at `:1032,1038` are wrapped in labels (good). `focus:outline-none` on `inputClass` (`:110`) with only a border color change, below contrast for focus (audit P1 #4).
12. **P2 `:1587` `statusNote` and `enrollmentError`** (`:1228`) are not announced (`role=alert`); a blind user pressing Record gets no feedback.
13. **P3 `:1266`** clip count badge appears only after a profile is expanded (`profileClips` lazy), so the count is missing on collapse-first load. Show count from the profile summary.
14. **P3 `:1569`** Delete button is hidden for the active model, with no explanation why; users hunting for it get no hint.
15. **P3 Download progress** is only a transient notice ("Track progress from the task tracker", `:640`); the card shows a spinner but no percentage, ETA or cancel. Link to the task tracker or inline progress.
16. **P3 Dates:** `toLocaleString()` for everything (`:1705`), "recently" for null (`:1704`), which can hide missing data as if it were fresh.

**Bugs and errors (source)**
- `:487-502` `hydrate` has an `ignore` flag that is only checked after `await`, and nothing uses it, so it is dead code.
- `:582` `AudioContext` and microphone stream stay open after the first recording until unmount or completion (`teardownAudio` only on finish or unmount). The browser mic indicator stays on while the user is on another tab. Close the stream when leaving the enrollment wizard or hiding it (`:1200`, `setShowEnrollment` does not call `teardownAudio`).
- `:1200` "Hide enrollment" while a session is open leaves `sessionId` set and the mic running; reopening resumes silently with stale state.
- `:869` `Math.max(trainingCount + 1, nextStatus.currentPromptIndex)` is guessy progress math; a "Retry" path (`:862`) returns early without clearing the previous recording but `isRecording` was already set false in `finishRecording` (`:628`), fine, yet the sample length error ("too short") is thrown after the processor is detached, so the user must start over the same prompt without feedback beyond the error text.
- `:1507` `key={record.id}` fine; `:1474` `key={i}` in the transcript feed, with in-place partial updates (`:235-247`), can reuse DOM across rows. Low impact.
- `:284,:1497` Transcript history fetch errors are swallowed (`.catch(() => {})`), so "No transcript history yet." can mean "request failed".
- `:301-305` `loadStatus` failure sets `status` to null and the page renders as "Setup needed" / all tiles "Not configured" (`:952`, `:1602`). An outage is shown as a configuration problem, a false and alarming state. Needs an error state with Retry.
- `:381-387` `loadVoiceConfig` failure is silent; the whole Voice Recognition Settings card just vanishes (`:1092`).
- `:1147,1179` `Number(event.target.value)` can produce 0/NaN on empty input and sends it on Save with no validation, despite `min/max` attributes (not enforced in JS).
- `:1356` `className={inputClass + ' !w-auto text-xs'}` and `:1253` use `!` important overrides to fight the shared class, a sign the class is wrong for these contexts.
- [live, cross-cutting] With about 5 dashboard tabs open on one origin, a new tab hung on "Loading..." and the document stayed `interactive` for over a minute while `curl` to `/api/auth/status` returned 200 in 30ms. The browser's 6-connections-per-host limit was exhausted by SSE streams (`/api/tasks/background/stream`, and this page's `/api/wyoming/sessions/live` adds another while Monitor is open). The Loading screen has no timeout, retry or message. See audit P3 #18; this is worse than polling noise. Serve over HTTP/2, or share a single stream across tabs (BroadcastChannel/SharedWorker), and add a timeout state to the shell loader.

**Performance notes:** [src] No code splitting, so this 1832-line page ships in the main chunk (audit P1 #1). Five tabs' worth of fetches fire on mount (`:491`: status, models catalog, installed, active, profiles, wake words, 4 installed-count calls, voice config, about 10 requests). Meter re-render at 60fps (above). `loadInstalledCount` issues 4 requests after every model action. Model catalog and transcript feed are unvirtualized. Monitor opens SSE only while its tab is active (good, `:186-191`).

**Suggested changes**
- Split into routes or tabs-as-routes with real `role=tablist`; extract hooks and tab components (`extract`, `layout`).
- Collapse the hero to a normal PageHeader; keep the two metric tiles inline (`distill`).
- Replace the Status "Next steps" checklist with a single "What to do next" CTA that deep-links to the right tab and highlights the first incomplete step (`onboard`).
- One shared ConfirmDialog flow for all six deletes/merges/promotes with named objects and consequences (`harden`).
- Add an error state to Status/Config/History fetches distinct from "not configured" (`harden`).
- Mobile: scrollable tab strip, stacked model-card actions, wrapped transcript lines, 44px IconButtons (`adapt`).
- Replace color-only states with icon+text and sr-only labels; fix tokens for emerald/stone/sky/cyan (`colorize`, `clarify`).
- Add real device management on Monitor (friendly names, last seen, link to the device/satellite config) or rename the section "Live sessions" so it does not promise management (`shape`).

**Not exercised (all of it, by instruction and by the browser outage)**
Never loaded live: every tab, the redirect at `/voice-onboarding`, the model sub-tabs, light theme, mobile and tablet. Even if it had loaded, I would not have clicked Download/Activate/Delete, Save settings, Enroll/Record/upload, Promote, Merge, Reassign, Delete clip/wake word/profile, audio play (it hits the server), or the Monitor stream (opens a live SSE connection to real voice sessions). The captions question, status semantics, audio controls and mobile layout are therefore inferred from source only and should be confirmed in a serial live pass.

---

#### Live pass (2026-10-14)

Light theme, 1440x900 and 375x812 (768 not tried for Voice). Every tab and Models sub-tab opened. Not clicked: Download, Activate, Delete, Save settings, Start enrollment, Record, audio play, Promote/Merge/Reassign (only 1 authorized profile exists, so Promote and Merge never render), Add Wake Word (blocked by the permission classifier as a possible state change; unclicked), Monitor stream actions. No page stalled: every tab rendered within 3s, the three slowest Models sub-tabs (Wake Word 3s, Speech Enhancement 2s, Speaker Verification 1s) showed "Loading kws models..." text first. No console errors. All API calls returned 200.

**Tab ARIA, routing, Back**
- [live] CONFIRMED: the five tab buttons have no `role`, no `aria-selected`, no `aria-current`, and the parent has no `role=tablist`. Active tab is only a background tint (`oklab(.51 .03 .10 / .1)` vs transparent), an amber-on-warm tint with no other cue.
- [live] CONFIRMED: URL stays `/voice-platform` on every tab and sub-tab; `history.length` did not change on click. I clicked Models, then used browser Back: it left Voice Platform entirely and returned to the previous page (`/`). Refresh always lands on Status.
- [live] CONFIRMED: `/voice-onboarding` redirects (replace) to `/voice-platform` on the Status tab, not Profiles or the wizard.
- Tab buttons are 44px tall on desktop and mobile (pass).

**Status tab**
- [live] CONFIRMED color-only: the 8-item "Next steps" list uses the same `lucide-check` icon for done (green `rgb(57,113,81)` on green tint) and not done (grey `rgb(98,104,97)`), no sr-only text. Not-done items on this instance: VAD, Wake word, Custom wake words. P2.
- [live] NEW, P1 (inconsistency): the checklist marks "Configure a VAD engine" and "Set up a wake-word detector" as not done, and the tiles say "silero_vad_v5 (loading...)" and "...kws-zipformer... (loading...)", but the Models tab shows both as Active/Installed. The Status page contradicts Models. The "(loading...)" never resolves; it persisted through repeated visits. Likely the tile reads engine-loaded state, not install state. Also the "Next steps" list always shows all 8 items regardless of what is configured; the intro sentence ("Wyoming is configured...") and the full to-do list disagree.
- [live] "Ready" tiles show only the model name; Custom Wake Words reads "Not configured" in plain text. The "Configured" pill in the header is text plus icon (OK). "Custom Wake Words" tile has no configure link (confirmed).
- [live] Hero is about 200px tall on desktop; on mobile the hero is a full screen: the tab bar starts at y=578 of 812.
- "Status failure reads as Setup needed": [not checkable] without forcing an API failure; I did not block requests. Source claim stands.

**Models tab**
- [live] NEW, P1: literal text **"undefined"** appears as a pill on 4 STT cards (Parakeet TDT 0.6B v2 Int8 installed, Granite 4.0 1B Speech, Parakeet TDT 0.6B v3 Int8, Parakeet TDT+CTC 110M Int8) where the Architecture should be. Cause: `archLabel(value)` returns `String(value)` for a missing architecture (`VoicePlatformPage.tsx:105`); STT models that map architecture as undefined (`:1521` maps `m.engineType`, and these models have none) render the string "undefined". Fix: `value ? ... : 'Unknown'`.
- [live] NEW, P2: the same pill shows wrong values on non-STT engines: Silero VAD shows "Zipformer CTC", NeMo SpeakerNet / 3D-Speaker show "Conformer", both GTCRN enhancement models show "NeMo FastConformer", the KWS model shows "Paraformer". The ASR architecture label is reused for engines where it is meaningless or wrong. Also "mul" is shown raw as the language for VAD and enhancement.
- [live] Active models carry both "Active" and "Installed" badges (redundant); the active card shows no Delete or Activate, no hint why.
- [live] Sub-tab labels wrap on mobile: each sub-tab button is 76px tall and 89-123px wide, "Speech-to-Text" breaks at the hyphen, and the strip scrolls horizontally inside its own container (so the page does not overflow, `scrollWidth` 375). Right edges are at 411px and 542px against a 375px viewport; "Speaker Verification" is clipped mid-word at the right edge and "Speech Enhancement" is entirely off screen, with no scroll affordance, fade or arrow. P1 on mobile, `VoicePlatformPage.tsx:1004-1025`. This REFINES the source claim (not an unscrollable overflow; a hidden scroller).
- [live] Card actions, desktop and mobile: "Download" 123x36, "Activate" 113x38, "Delete model" icon 34x34 (it does carry an `aria-label`). All under 44px, none under 24px. On mobile, "Streaming Zipformer English" wraps to 3 lines beside Activate + Delete (CONFIRMED title squeeze). Language/Architecture selects are 42px; "Streaming only" checkbox is 16x16 inside a 44px-ish label row.
- [live] Language filter lists 31 raw ISO codes (ar, bg, cs ...), not names.
- [live] The catalog has about 12 STT models and cards are long; no search. Other engines have 0-2 catalog entries, so the "No additional models available." state appears (acceptable, but VAD and enhancement catalogs read as empty rather than "all installed").
- [live] Each sub-tab click refetches: KWS list/installed/active were requested twice, speaker-embedding twice, on one pass through the sub-tabs. Not an error, extra requests.

**Profiles tab**
- [live] Voice Recognition Settings: the four checkboxes, number inputs and range are properly labeled (`labels.length` 1 each). REFUTES the "inputs lack labels" claim for this card. Checkbox rows put the label at the far left and a 16px checkbox at the far right (about 280px apart), which makes the pairing hard to scan. "Speech enhancement" is checked and greyed with no helper text. "Provisional default: 0.35" text is confirmed hard-coded copy next to a 0.50 slider.
- [live] CONFIRMED unlabeled: "Speaker name" and "Optional wake phrase" enrollment inputs are placeholder-only (`labels` 0, no `aria-label`). Start enrollment is disabled until name is typed. The "Enrollment notes" list reuses check icons for instructions, which look like completed items.
- [live] Profile card: icon buttons Rename 34x34, Show/Hide clips 34x34, Delete 34x34, all with `aria-label` (so refutes "no label" if any was claimed). "View memories" link 117x44. The clip count badge ("3 clips") appears only after clips are expanded: CONFIRMED. A single profile occupies half the row, leaving the right half empty.
- [live] NEW, P2: clip rows show the raw .NET duration `00:00:03.0240000` (7 fractional digits); the dates are `toLocaleString`. The `<audio>` has no `aria-label`, `preload=none`, height 32px, and reads `0:00 / 0:00` until played (the duration is known from the metadata but not shown). Transcript text is rendered visibly under each clip (so the caption concern reduces to the missing `aria-label`; there is nothing to caption). "Reassign clip" is a text button measuring **72x16**, under the 24px floor (P2); Delete clip icon 32x32. I did not play audio.
- [live] On mobile the same: 4 checkboxes 16x16 and range 301x16 (<24px), Reassign clip 72x16, delete-clip 32x32; no horizontal overflow at 375.
- [live] Not seen: Promote, Merge (no unauthorized profiles). Native `confirm()` dialogs not exercised.

**Wake Words tab**
- [live] Empty state is a single line "No wake words registered yet." with an "Add Wake Word" button (163x38). Heading copy mentions "calibration state and speaker association" that never show. The button's behavior (jumps to Profiles) was not clicked: permission denied by the auto-mode classifier, so that source claim is **not checkable** here.

**Monitor tab**
- [live] The feed connected: header read "Live · 0 devices" within 6s (sage dot plus the word "Live"); "Live Transcript Feed" showed "Waiting for transcripts..." in monospace 12px. There is no `aria-live` or `role=log` anywhere in the tab (CONFIRMED). The SSE connection used `/api/wyoming/sessions/live` plus `/api/wyoming/transcripts/recent?limit=50`, both 200. I could not exercise the live-error path.
- [live] Transcript History listed 5 entries with time, an uppercase "RAW" chip, text and "(100%)". The entry buttons are 24px tall (width full), the "Refresh" button 16px tall (P2). Expanding the first row showed audio duration (3808ms), sample rate, STT model, VAD, diarization, a pipeline stage table (stt 162ms, diarization 19ms, enhancement 693ms, enhanced_retranscription 146ms; status as a bare checkmark glyph), and a Response string that leaks markup: `<Unknown1 />Zach's light.` (P2, new, unrendered speaker tag in the user-visible response). Row has no `aria-expanded` (CONFIRMED, source did not claim it). Confidence "(100%)" for every row looks like a placeholder.
- [live] Mobile: history rows wrap well at 375 ("Turn off the lights in Zack's office" truncates, the rest fit), no overflow. Fine.

**Cross-cutting live**
- [live] Light theme legibility is good on both pages; no contrast leaks noticed, but 10-12px muted text dominates (clip metadata, Provisional default, filters).
- [live] `/api/tasks/background` was requested about 500 times in roughly 10 minutes (including many requests in bursts of 10 or more), on every page including Voice. Never slow, but wasteful; confirms the earlier note.
- Stalls: none over 20s; all pages loaded within 3s. The earlier connection-limit hang did not reproduce with a single tab.

---

## Cross-page observations

- Both pages hand-roll buttons, cards and tabs rather than using shared primitives; Voice defines its own `buttonPrimary/Secondary/inputClass` constants (`VoicePlatformPage.tsx:108-110`), Skill Optimizer inlines classes. Neither uses the 12px control radius consistently.
- Both silently swallow fetch failures and show an empty or "not configured" state instead of an error (Voice `:301-305,:381-387,:1497`; Optimizer `:193-196`).
- Neither page announces async results (no `aria-live`/`role=status`) and neither uses `ConfirmDialog` for state-changing actions, including Skill Optimizer's config write.
- Both have long-running jobs (model download, optimization) whose state lives only in component memory; a refresh loses them.
- The shell has no timeout state for a stuck auth/status request, and several open dashboard tabs can starve each other through persistent SSE connections.

## Top 5 for this group

1. **P1** Six native `confirm()` calls on Voice (`:664-826`) with generic wording, plus Promote with no confirm and Merge/Reassign firing from `<select onChange>`.
2. **P1** Skill Optimizer "Apply Results" writes live matcher config with no confirm, diff or undo, and a lost poll leaves the page "running" forever (`SkillOptimizerPage.tsx:183-243`).
3. **P1** Voice Platform is one 1832-line component with non-routable, non-ARIA tabs; `/voice-onboarding` redirects to the wrong tab, so deep links and Back are broken. Split into routes.
4. **P1** Mobile: Models sub-tabs and Skill Optimizer toolbar overflow at 375px; model-card and profile-card actions crowd out text; tap targets about 34px or 28px (inferred, to confirm live).
5. **P2** Status is color-led (checklist, tiles, results) and a failed status fetch reads as "Setup needed"; audio clips need transcript text and an `aria-label` rather than a suppressed caption rule, and the live feeds are not `aria-live`.

Counts (all inferred unless marked): P0 0, P1 7 (confirm() set, Apply Results, polling/resume, tabs-as-routes, merge/reassign select-commit, mobile overflow, stuck shell loader with SSE exhaustion), P2 17, P3 9.

## Live pass summary

Evidence tags on the findings above that I verified are now **[live]** (Voice tab ARIA, non-routability, Back, redirect target, color-only checklist, unlabeled enrollment inputs, clip count badge, Models sub-tab overflow, card tap targets, no aria-live on feeds; Skill Optimizer duplicate "Select an option" names, `flex-1` empty card). Items without a live tag in the older text remain [src].

- **Confirmed live:** 17 (tab ARIA absent, URL not routable, Back leaves the page, `/voice-onboarding` lands on Status, color-only Next steps, unlabeled enrollment inputs, clip badge after expand, hidden-scroll sub-tabs, 34px/36px card actions, narrow model title on mobile, no live region on Monitor feed, history row buttons no `aria-expanded`, hard-coded "Provisional default", SSE dot plus text OK, Optimizer duplicate select names, empty `htmlFor`, stretched empty card).
- **Refuted live:** 4 (Skill Optimizer toolbar horizontal overflow at 375: it wraps and crushes instead; "no focus-visible" on Optimizer: amber focus ring present; Voice settings inputs lack labels: they are labeled; Voice icon buttons lacking labels: all have `aria-label`).
- **New live findings:** 8 (literal "undefined" architecture pill P1; Status checklist and "(loading...)" contradict Models P1; wrong architecture labels on non-STT engines P2; hidden-scroll, 3-line wrapped sub-tabs on mobile P1; raw .NET duration and 72x16 "Reassign clip" P2; `<Unknown1 />` markup leaked in transcript Response P2; Optimizer empty-state does not explain "0 devices cached" P2; Optimizer disabled primary with no reason P3).
- **Not checkable / unverified:** Status failure reads "Setup needed" (needs forced API failure); Add Wake Word behavior (blocked by classifier); Promote, Merge, Reassign, all `confirm()` dialogs, recorder, audio playback, download progress, Apply Results, poll-failure state, trace import, Skill Optimizer results grid at 375, dark theme, Voice at 768.
- **Largest live-only items:** the "undefined" pill, the Status-versus-Models contradiction, and the Models sub-tab strip clipped at 375px with no scroll hint.
