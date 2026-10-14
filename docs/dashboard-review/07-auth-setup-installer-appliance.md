# Dashboard review 07: Auth, Setup, Installer, Appliance

Scope: `LoginPage.tsx` (93 lines), `SetupPage.tsx` (1152), `InstallerPage.tsx` (1185), `AppliancePage.tsx` (847), plus `App.tsx` routing and `ConfirmDialog.tsx`. Paths relative to `lucia-dashboard/src/`.

**Evidence basis.** All four pages were read in full. `impeccable detect --json` on the four files returned `[]` (no automated findings), so everything below is manual. The coordinator stopped live browsing mid-review (SSE connection exhaustion), so **every finding is inferred from source** unless marked "observed live". Observed live, before the stop:
- `/login` while signed in showed "Loading..." for several seconds (auth status slow under shared-pane load), then landed on `/`. Matches `App.tsx:196-206` + `*` redirect, with the `/login` route only mounted when unauthenticated.
- `/setup` behaved the same: long "Loading...", then `/` (`App.tsx:184-194` only mounts `/setup` when `!setupComplete`).
- Nav on this instance has no "Appliance" entry (last items: Voice Platform, User memories, Matcher Debug, Configuration), so this is not an appliance host (`App.tsx:250`). A direct `/appliance` visit could not be confirmed live; by source it redirects silently to `/` (`App.tsx:350`).
- No Vite fixture or story exists. Playwright specs `lucia-playwright/e2e/00-appliance.spec.ts` and `00-installer.spec.ts` mock APIs with `page.route`, which could render these pages without touching the session, but running them was not attempted.
- Screenshots in `docs/images` are **stale**: `login.png` matches current Login, but `setup-configure.png` shows a 4-step indicator (Welcome/Configure/Connect/Done) and a "Regenerate Dashboard Key" button that no longer exist (now 6 steps, `SetupPage.tsx:103-110`, resume-login form instead).

---

### Login (`/login`, `src/pages/LoginPage.tsx`)
- **Purpose:** paste the dashboard API key to sign in.
- **Scores:** Nielsen 29/40. Recovery/help (H9/H10) scored 1: no "lost your key" path, raw server message on failure. Cognitive-load failures 1/8 (no help for the one decision a returning user can get stuck on).
- **Desktop (inferred, plus stale screenshot):** minimal, centered card, autofocus on key field (`:60`), correct label/`htmlFor` (`:50`). Good.
- **Mobile (inferred):** `pt-24` (`:26`) clears the fixed ThemeSelector. Input `py-3 text-sm` = about 46px tall, but `text-sm` (14px) on iOS triggers focus zoom. Not tried at 375px.
- **Pain points:**
  1. P2 `:53-62` Password input has no `name`, `autoComplete`, or hidden username field. Password managers cannot reliably save/fill a `type=password` with no username; the key must be pasted by hand every session. Fix: add `name="apiKey" autoComplete="current-password"` and a visually hidden `username` input, or use `autoComplete="off"` deliberately.
  2. P2 `:65-69` Error shows `err.message` verbatim (`:19`); no `aria-invalid`/`aria-describedby` on the input, and the alert has no `role="alert"`, so a screen reader hears nothing on failure. Fix: `role="alert"`, link to input.
  3. P2 No "Lost your key?" help. Setup (`SetupPage.tsx:364`) has such a line; Login does not. Fix: one line under the form naming the recovery path (appliance owner key / reset config).
  4. P3 `:62,73` No show/reveal toggle for a long `lk_...` key; disabled button gives no reason when empty (it is obvious here, so low).
  5. P3 `:28-30` absolute glow container has no `relative` ancestor on the root flex div; it is positioned against the viewport. Harmless, but note for `polish`.
  6. P3 `:78` spinner `animate-spin` is not covered by the reduced-motion blocks (`index.css:599,642` target installer/appliance classes only). Tailwind spinners are conventionally exempt but flag for consistency.
- **Bugs/errors:** none found in source. Observed live: perceived hang on `/login` is the global "Loading..." (`App.tsx:172-182`) while `/api/auth/status` is pending; no timeout/error fallback if that request hangs (P2: a stuck status call leaves an infinite spinner with no retry).
- **Performance:** none; page is tiny.
- **Suggested changes:** `harden` (password-manager attrs, alert semantics), `clarify` (recovery line). Add a timeout + "Can't reach Lucia, retry" state to the auth bootstrap (`harden`).
- **Not exercised (first pass):** login submit, error state, mobile, light theme (no live pass; must not sign out).

#### Live pass (2026-10-14)
Method: isolated Vite on :5199 + headless Playwright with every `/api` call mocked (unmocked paths returned 404); the real session and backend on 7233/5151 were never touched. Scratch scripts and PNGs are in the session scratchpad, not the repo.
- **Desktop dark/light 1440x900:** no overflow, zero targets under 44px, ThemeSelector focus ring visible [live]. Light theme: disabled "Sign In" is white text on pale tan (low contrast) and the `lk_...` placeholder is faint [live]. Tab order puts the 3 ThemeSelector buttons before the key field (autofocus is lost if focus is reset) [live].
- **Mobile 375 / tablet 768:** no overflow, no targets under 44px; `pt-24` clears the selector. Input font size is 14px (`text-sm`), so iOS focus zoom stays a risk (computed 14px, not testable in Chromium) [live].
- **Error state:** 401 renders "Invalid API key" with no `role="alert"`, no `aria-invalid`, no autocomplete/name attributes on the input: CONFIRMED live (P2 items 1-2).
- **Status never answers:** page sits on "Loading..." indefinitely (8s observed): CONFIRMED live (P2).
- **NEW P1 (live): `/api/auth/status` returning 500 drops an existing install onto the Setup wizard "Welcome" screen.** `AuthContext.tsx` `refresh()` catch sets `setupComplete=false`, so any transient backend error on a configured instance shows first-run setup instead of an error/retry. Fix: separate "unreachable" state from `setupComplete:false`.
- **Console:** only the expected 401. Network: one status call, one login call.

---

### Setup wizard (`/setup`, `src/pages/SetupPage.tsx`)
- **Purpose and primary task:** first-run: create dashboard key, connect Home Assistant, add an AI provider, wait for agents, link the HA plugin, finish.
- **Scores:** Nielsen 22/40. Scored 1: H3 user control (no Back anywhere), H9 error recovery (silent failures, discarded test messages), H5 error prevention (key can be lost on reload, instant provider delete). Cognitive-load failures 4/8: visibility of progress (mobile indicator has no text), too many simultaneous panels in step 2, undisclosed gating on Next, jargon ("propagate", `LUCIA_HA_API_KEY`).
- **Desktop (inferred):** a single glass card, max-w-2xl, 6-dot indicator. Step content is clear and consistently styled (section cards, amber headings). Stale screenshot shows the old 4-step design.
- **Mobile (inferred):** indicator labels are `sr-only` below `sm` (`:128`), so the user sees six anonymous icons and no "Step 2 of 6" or current-step name (the Installer solved this with a mobile "Step n of N" bar, `InstallerPage.tsx:493-513`). Icon-only action buttons are under 44px (below). Long key text can overflow (below).
- **Pain points (ordered):**
  1. **P1 `:1119`, `:86-90`** Final "Complete Setup" calls `onComplete`, which does `await completeSetup(); await refresh(); setStep('done')` with no try/catch and no busy state. A failed `completeSetup` is an unhandled rejection: button does nothing, no message. Button is also not disabled while in flight (`busy` is not set), so double-submits are possible. Fix: wrap with try/catch -> `setError`, set `busy` during the call.
  2. **P1 `:304-324`, `:472`** One-time dashboard key is shown with only a copy icon; Next is enabled without copying or confirming. State lives only in component memory, so a reload on this step loses the only copy ("you won't see it again", `:298`). The Installer solves this with an explicit "I saved this key" gate (`InstallerPage.tsx:1082-1100`). Fix: add a required "I saved this key" checkbox/button before Next; warn on `beforeunload` while unsaved; also reset `keyCopied` and use `break-all` on the `<code>` (`:310` has no wrapping, so a long key overflows on 375px).
  3. **P1 `:664-677`** Provider row actions (Test / Select model / Delete) are icon-only with `title=` only (not reliable for touch or screen readers), about 29px tall (`px-2.5 py-1.5` + 14px icon). Delete (`:675`) executes immediately with no confirm. Fix: `aria-label`, 44px hit area, confirm using the existing `ConfirmDialog` (not native `confirm()`).
  4. **P1 `:659-663`** Provider test result renders only "✓ OK" / "✗ Failed"; the server `message` (`testResults[p.id].message`) is stored (`:580`) but never shown. A failed connectivity test gives no reason (bad key vs wrong endpoint vs model). Fix: show `result.message` beneath the row; announce with `role="status"`; show a spinner while testing (`handleTest` has no pending state, so the button looks dead).
  5. **P1 `:236-247`, `:438`** HA step: "Save" and "Test Connection" are separate, and Test is enabled before Save and tests the *saved* config, not the typed values. A user who types URL+token and clicks Test first tests stale or empty config. Save gives no confirmation (it only sets `haSaved`, silently enabling Next); `haSaved` is not reset if fields are edited afterwards; `testResult` goes stale. Fix: one primary "Save and test" that saves then tests, shows the result inline, and resets on edit.
  6. **P1 unlabeled inputs** `:346`, `:416`, `:423`, `:702`, `:777`, `:817` rely on placeholder only; labels at `:736,747,768,814` have no `htmlFor`/id association. Cited in the audit as a systemic issue; here it is the whole wizard. Fix: real `<label htmlFor>` and `autoComplete` (HA token = `off`).
  7. **P2 no Back / forward-only** `:61-93` No step can be revisited; mistakes in the HA URL after "Next" require a different page later. Also no `<form>` elements (`:345`, `:415`, `:728`), so Enter does not submit and there is no autofocus on any step's first field or on "Get Started" (`:171`). Keyboard users tab from step to step by hand; focus is not moved to the new heading on step change.
  8. **P2 `:162-169` vs `:103-110`** Welcome lists 4 tasks; the indicator has 6 stages (Agents wait step is not mentioned; "Done" counted). Expectation mismatch for first-timers. Also no time estimate or "what you need ready" (HA URL, long-lived token, an AI API key). Fix: "Have ready: ..." checklist on Welcome (`onboard`).
  9. **P2 `:853-867`** "Skip for now" and a disabled "Next" do the same thing (`onComplete`), contradicting "Lucia needs at least one Chat provider" (`:641`). Disabled Next has no explanation (also `:472`: Next disabled with no reason when HA is unsaved or key absent). Fix: helper text next to the disabled button; make Skip a clear "I'll do this later".
  10. **P2 `:875-895`** Agent wait step: polling swallows every error and loops forever (`:887`); the only feedback is an elapsed-seconds counter. No failure/timeout copy, no `aria-live`. When ready, the left "Skip" button renders an empty string (`:955`), leaving an empty focusable button (a11y bug). Jargon "Waiting for configuration to propagate..." (`:927`). Fix: after ~60s show "Taking longer than usual" + link to logs; remove the empty button.
  11. **P2 `:999-1017`** Plugin wait: requires a manual "Start Waiting for Plugin" click (`:1106`), then polls 60x5s with no cancel and keeps looping after unmount (no cleanup; setState on unmounted component). Timeout is shown in the error style although it is not an error (`:1016`). Fix: start polling automatically once a key exists; cancel via ref/AbortController.
  12. **P2 `:1119-1121`** "Skip & Complete Later" is visually identical to the happy-path "Complete Setup" and finishes setup with no confirmation. Make the skip a secondary/ghost button.
  13. **P2 `:1044`, `:1083`** Copy exposes implementation jargon (`LUCIA_HA_API_KEY`), a hard-coded `http://lucia-host:5151` example (the dashboard is on a different port; use `window.location.origin`), and "Paste the API key you just generated above" even when the key is already configured and not displayable.
  14. **P2 `:35-41`** Initial `fetchSetupStatus()` has no `.catch`: on failure `status` stays `null` and the wizard silently starts at Welcome with no error. Likewise `:468-471` Next's `fetchSetupStatus()` is unguarded.
  15. **P2 `:54`, `App.tsx:187`** Card starts at 16px from the top while the fixed ThemeSelector (170px wide, `right-4 top-3 z-50`) overlays the top-right, so on 640-1040px wide windows with tall steps it sits over the step indicator ("Done" dot). `pt-24` only applies below `sm` (`sm:p-4`). Fix: reserve space or render the selector inside the card.
  16. **P3** Emoji/Unicode glyph status marks `✓`/`✗` (`:384`, `:452`, `:661`) instead of Lucide icons (audit systemic: emoji as icons). Test result can print `(vundefined)` when `haVersion` is missing (`:452` lacks the null guard used at `:385`). Raw `bg-red-900/30` (`:691`) and `rounded` 4px (`:691`) are off-spec. Model picker shows both a `CustomSelect` and a free-text input for the same value (`:693-708`), and the picker opens below the provider list with no scroll/focus after "Save Connection" (`:566`). Random provider names like "OpenAI Connection x7k2" (`:550`).
- **Bugs/errors:** items 1, 5, 10 (empty button), 11 (leak), 14 above; `:38` sets `done` if status already complete (unreachable via routing but resumes a finished wizard on direct render).
- **Performance:** polling every 3s (agents) and 5s (plugin) is fine; no SSE.
- **Suggested changes:** `harden` (error handling, key-save gate, delete confirm, test message), `clarify` (welcome checklist, button gating copy), `onboard` (progress text on mobile, Back, auto-poll), `adapt` (44px targets, `break-all` key), `polish` (glyphs to icons, tokens).
- **Not exercised (first pass):** everything live (setup is complete; no reset, no key generation, no provider create/test/delete, no mobile).

#### Live pass (2026-10-14)
Isolated render with a stateful mocked backend; all six steps walked, including failures.
- **Desktop dark/light 1440x900:** no overflow. Step indicator labels visible. Targets under 44px on every step: Get Started 143x40, Generate Key 208x40, Save 72x40, Test Connection 148x40, Next 95x40, Copy key 42x38, "Skip for now" 79x20 (under 24px) [live]. Light theme renders cleanly; disabled Next (tan on cream) is low contrast [live].
- **Mobile 375: CONFIRMED P1 overflow.** With a 67-char key, the document is 238px wider than the viewport (scrollWidth 613). The `<code>` is 501px wide inside a 313px card; the Copy button is pushed outside the card at x=571-613, off-screen. Cause `SetupPage.tsx:310` (no `break-all`/`min-w-0`) [live]. Mobile step indicator: six anonymous icons, labels only in `sr-only` [live]. Provider row buttons 34x26 (Test/Select model/Delete), `title` only, no `aria-label` [live].
- **Tablet 768:** no overflow, same under-44 list.
- **ThemeSelector overlap: CONFIRMED.** On the key/HA step (card 878px tall) the fixed selector (top 12-70px) intersects the card at 640-1040px wide and covers the step indicator ("Done" dot) at 640-900px; clear at 1100px+ [live]. Screenshot `theme-overlap-800.png`.
- **Failure walk (all CONFIRMED live):**
  - Complete Setup with a 500: the click does nothing visible, an unhandled `pageerror` is thrown, no message, button not disabled (`:86-90,1119`) (P1).
  - Provider test failure: row shows only "✗ Failed"; the server message is discarded (P1). No pending state while testing.
  - Provider Delete: removes the row at once, no native or custom confirm (P1).
  - HA step: Test Connection is enabled before Save and fires `test-ha-connection` with no `configure-ha` call first; Save shows no confirmation, it only enables Next (P1). A failed HA test does show the server reason inline (better than the provider step).
  - Dashboard key: shown with copy icon only; Next is enabled without copy/ack; after a reload the wizard restarts at Welcome, the key is gone, and step 2 reads "Authenticated - continue setup below" with no warning that the key was never saved (P1).
  - Agents step: no `aria-live`/`role=status`; when ready the left button renders with empty text and 0px height but stays in the tab order (confirmed `[]0`).
  - Plugin step: example URL `http://lucia-host:5151` and `LUCIA_HA_API_KEY`-style jargon shown; "Start Waiting for Plugin" is manual; Start 193x42, Skip & Complete Later 215x40.
  - Provider step: model picker shows a `CustomSelect` plus a duplicate free-text input for the same value (screenshot `setup-3c-provider-saved-d-light.png`).
- **Placeholder-only inputs: CONFIRMED** (HA URL, HA token, API key inputs have no `<label>`; the Purpose/Type labels have no `htmlFor`).
- **Focus:** after every step change `document.activeElement` is `body` (focus lost with the removed button); Welcome "Get Started" shows no focus ring in the Tab walk (`ring=N`) [live].
- **Not reproduced:** Setup "Done" step never visible after Complete: `refresh()` flips `setupComplete` and `App.tsx` swaps to the main app (mock: success path not captured visually).

---

### Installer (`/install`, `src/pages/InstallerPage.tsx`)
Route note: `App.tsx:70` serves it outside `AuthProvider` and bounces to `/` when not in installer mode (`:90-92`), so it cannot be visited on this host.
- **Purpose and primary task:** headless appliance first boot: claim session, choose NVMe, Wi-Fi/name/recovery password, confirm erase, watch installation, save owner key.
- **Scores:** Nielsen 31/40. Best-in-group. Scored 1: none. Weakest: H9 (failure diagnosis), H1 (failed stage not marked). Cognitive-load failures 2/8 (failure state, long single-page identity step).
- **Desktop (inferred):** two-column layout, sticky signal path rail, good copy ("One careful look"), 48px controls, `text-base` inputs, explicit erase-phrase confirmation. Strong.
- **Mobile (inferred):** dedicated compact header with ThemeSelector, "Step n of 5" bar with `role="progressbar"` (`:493-513`), `min-h-12` controls, safe-area insets, stacked reverse button order. This is the reference implementation for the other wizards.
- **Positive reference, reduced motion:** boot splash shortens to 100/200ms under `prefers-reduced-motion` (`:115-118`, `:161-165`) and `index.css:599-617` removes all installer animations; Appliance scan bar is disabled at `index.css:642-648`. Copy this pattern into Login/Setup spinners and the global `animate-pulse`.
- **Pain points (ordered):**
  1. **P1 `:1082-1100`** The owner key (the only credential) has no copy button and is not `select-all`; user must hand-select a `break-all` string on a phone, then press "I saved this key", which is irreversible (`:311-313`, key removed from storage). Fix: add Copy with feedback; require a deliberate acknowledgement (checkbox) and say "you cannot see it again".
  2. **P1 `:1128-1156`, `:1131`** Failure state never marks which stage failed: when `phase === 'failed'` no step is active or red; the list just stops. The icon at `:1030` stays an amber `Cpu`. A user at a headless box learns "Installation needs attention" but not whether erase, write or provisioning broke. `status.message` may be absent, falling to generic copy (`:1043-1046`) that says to "restart the appliance" with no error code or log path. Fix: mark the failed stage with a rose icon + `aria-label`; show error code and a copyable diagnostics block.
  3. **P2 `:236`** Wi-Fi validation requires an 8+ char password whenever an SSID is typed, so an open network cannot be used even though the datalist shows security (`:740`). Same in retry (`:284`, `:1074`).
  4. **P2 `:234-253`** Validation runs only on "Review installation" and shows one error at a time in a block at the bottom (`:875`), off-screen on a phone after a long form. No `aria-invalid`, no focus to the offending field. Hostname rule (`:240`) is stated only after the fact; show it as inline help under the field (`:829-843`).
  5. **P2 `:703-745`** Wi-Fi list is a native `<datalist>` populated once at claim (`:221-225`): no rescan button, poor rendering on mobile Safari/Android, signal/security shown only in option labels. Fix: rescan control + list of selectable rows.
  6. **P2 `:1041`** Success copy gives `https://{host}.local:8099` as plain text: not a link, not copyable, no IP fallback (mDNS `.local` often fails on Android). The port differs from the dashboard port used elsewhere; confirm intended.
  7. **P2 `:1128-1156`** Stage changes and the connection-lost text (`:190`) have no `aria-live`; active step lacks `aria-current="step"`. Focus is not moved to the new `<h1>` when steps change, so keyboard/screen reader users are left on a removed button. The "Connection lost... may be powering off" error appears on any transient poll failure and persists until the next success, which can scare users mid-write.
  8. **P2 `:644-657`, `:655-657`** Disk cards are `aria-pressed` toggles with an `aria-label` that replaces content ("Use Samsung..."), hiding size/serial; two identical drives are indistinguishable to a screen reader. Use a `radiogroup` and keep visible text in the accessible name. Single installable disk is not preselected (extra click).
  9. **P3 `:117-118`, `:424-456`** Mandatory 1.65s boot splash on every first visit per session (waits on logo `onLoad` before timing starts). Pretty, but no skip; with sessionStorage blocked it replays each load.
  10. **P3 `:312`** `sessionStorage.removeItem` is outside the try/catch used elsewhere; a SecurityError after a successful ack would surface as "could not confirm key handoff" although it was acknowledged. `:76-83,:91-96` stores the plaintext owner key in sessionStorage until acknowledged (security note: acceptable only because short-lived; document it).
  11. **P3 `:1064`** Retry-Wi-Fi password field has no show/hide toggle that exists on the identity step (`:788-800`). Typed values (wifi/recovery password) are lost on reload (`:106-156` restores only claimed-session state).
  12. **P3 `:963-967`** Install button is red/destructive with white raw `text-white`; fine semantically, check contrast on light theme.
- **Bugs/errors:** `:380` `step === 'review' && selectedDisk` renders an empty card if `selectedDisk` is ever null (e.g. disk list refetched); low likelihood.
- **Performance:** 1s polling during install (`:195`); acceptable for a LAN appliance, but pause when the tab is hidden.
- **Suggested changes:** `harden` (failed-stage marking, owner-key copy/ack), `clarify` (open Wi-Fi, inline hostname rule, success URL as link), `onboard` (preflight "you'll need" list on the claim step: SD card, Wi-Fi password, password manager), `adapt` (rescan list instead of datalist), `polish`.
- **Not exercised (first pass):** all live (not reachable on this host; no mocked run done).

#### Live pass (2026-10-14)
Rendered at `/install` with mocked `/api/installer/*` (claim, disks, networks, install, status, acknowledge).
- **Desktop dark/light 1440x900:** no overflow, no targets under 44px. Clean two-column layout with a signal-path rail; light theme legible (screenshot `inst-3identity-d-light.png`).
- **Mobile 375 / tablet 768:** no overflow; "Step n of 5" with `role="progressbar"` present (desktop DOM contains the text but no progressbar role). Only misses: the three ThemeSelector buttons are 38x44 (under 44 wide). Boot splash plays once per session [live].
- **Failed-stage marking: CONFIRMED P1.** With `phase:'failed', stage:'failed'` no step is complete, active or red; with `stage:'provisioning'` the first three steps show green checks and the rest stay neutral dots, with no failure marker on "Configure both system slots" (0 rose elements in the list). Without `message`, the generic "restart the appliance" copy shows (screenshot `inst-failed-provisioning-mobile.png`).
- **Owner key: CONFIRMED P1.** Key is displayed (`break-all`, wraps cleanly at 375, no overflow) with only "I saved this key"; zero copy buttons. Recovered-key state after reload behaves the same.
- **Two identical disks: CONFIRMED.** Both cards have `aria-label="Use Samsung 990 PRO"`; serials are not in the accessible name. Continue stays disabled until one is clicked. Too-small disk is labeled "unavailable: Smaller than the 61.2 GB minimum" (good).
- **Open Wi-Fi: CONFIRMED.** Typing SSID "Cafe" (open network in the scan) without a password returns "Enter the password for your home Wi-Fi, or choose Ethernet only." Error is a single `role=alert` block at the bottom; 0 fields get `aria-invalid`.
- **Live regions: CONFIRMED.** 0 `aria-live`, 0 `aria-current` on the progress page. Connection-lost text appears as a `role=alert` ("Lucia may be powering off...") after one failed poll, which is alarming mid-write.
- Not a bug: a mid-transition screenshot showed the mobile bar at ~20% with "Step 5 of 5"; it is the 300ms width transition, steady state is full.

---

### Appliance (`/appliance`, `src/pages/AppliancePage.tsx`)
- **Purpose and primary task:** manage a Jetson appliance: see service health, check/install/rollback Lucia and OS updates, restart services, configure telemetry, reboot.
- **Scores:** Nielsen 28/40. Scored 1: H5 error prevention (service restart and OS update lack warning/confirm parity), H1 visibility after reboot (stale page). Cognitive-load failures 2/8.
- **Desktop (inferred):** header with hostname, identity strip of 4 cells, Release channels card with per-channel rails, Host services, Telemetry, a rose-tinted reboot zone. Clear sectioning and good use of `<progress>` with textual per-phase percentages (`:582-591`), honest "Progress not measurable in this phase".
- **Mobile (inferred):** header stacks; 44px min-heights everywhere (positive). Not tried at 375px.
- **Gating (observed live + inferred):** nav entry only when `/api/appliance/capabilities` reports enabled (`App.tsx:250`); not present on this host. `/appliance` on non-appliance hosts silently redirects to `/` (`App.tsx:350`), no "not an appliance" message. `App.tsx:159-163`: any non-404 failure retries every 2s forever while the route shows "Checking appliance capability..." with no error surface (P2).
- **Pain points (ordered):**
  1. **P1 `:182-194`, `:396-405`** "Restart" on a host service runs immediately, no confirmation. Restarting AgentHost kills the dashboard session and active voice/conversations (the reboot section itself warns this, `:429`), and Redis holds active tasks (`:46-49`). Fix: ConfirmDialog with consequence text; for `agenthost` show "Reconnecting..." and poll status.
  2. **P1 `:196-205`, `:112-125`** After "Reboot Jetson", the page only shows a notice and keeps rendering stale healthy status; the reconnect loop runs only when `status` is null (`:113`), so nothing indicates the device is offline, buttons stay enabled, and Reboot can be clicked twice (no busy state). Notice says "disconnect briefly" (`:201`) while the confirm says "several minutes" (`:446`). Fix: enter an explicit `rebooting` state, disable actions, poll `/status` until it returns, then refresh.
  3. **P1 `:453-457`** Update confirmations lack key risk facts: no from/to versions or release notes, no "do not power off" during an OS write, no expected downtime for the Lucia update, and for OS: when exactly the reboot occurs ("when it is staged", `:218`, not in the dialog). The confirm button is red `bg-rose/20` (`ConfirmDialog.tsx:75`) for every action including "Verify and install", which signals destruction for a routine update; there is no variant prop (confirm default label is "Delete", `ConfirmDialog.tsx:22`). Fix: `variant` prop, show `Installed -> Latest`, add power-loss warning.
  4. **P2 `:617-641`** Roll back and Install share identical secondary styling and sit side by side; the more consequential action (Install, usually) is not primary and the risky one is not distinguished. Fix: Install primary, Roll back quiet/ghost with a subtext about availability.
  5. **P2 `:834-844`, `:838`** Telemetry toggle only edits local state until "Save telemetry" (`:726`), so the switch looks live but is not; no dirty indicator. Save is disabled whenever the endpoint is empty (`!endpoint`), so a user cannot save "disabled" after clearing the endpoint. Fix: label "Save to apply"; allow save when `enabled` is false.
  6. **P2 `:595-642`** Right cluster `flex items-center gap-3` (status text + Roll back + Install) has no `flex-wrap`; at 375px with "Verification required" it likely overflows the p-5 card (inferred, verify live at 375px).
  7. **P2 `:253-266`, `:268-283`** `notice` is never cleared, so a stale success banner coexists with a new error banner (e.g. "restart requested" plus "Update status is unavailable"). Auto-dismiss or clear on next action.
  8. **P2 `:112-125`** If the initial load fails, the page shows an error, then silently re-fires `load()` every 2s forever with no backoff and no "Reconnecting" text (only the update poll sets `reconnecting`, `:152`).
  9. **P3 `:488-494`** Wi-Fi cell prints an empty or "Ethernet" value when the appliance is wired (installer supports Ethernet-only, `InstallerPage.tsx:809`); `ssid` is required string, so wired units may show a blank value.
  10. **P3 `:436`, `:399`, `:414`** Controls disabled during an update (`isUpdateBusy`) give no on-control reason; add a note ("Disabled while an update is running").
  11. **P3 `:164-168`** Dependency-driven effect re-subscribes polling when `phase` changes; harmless. Check-for-updates is manual only ("Not checked" state until clicked); consider showing last-checked time.
  12. **P3** `rounded-2xl` sections use `border-rose/25 bg-rose/5` tints well; raw palette not used here (positive). `ConfirmDialog` cancel is focused first (safe default, positive) and restores focus; no native `confirm()` here (positive vs. rest of the app).
- **Bugs/errors:** items 2, 5, 8 above.
- **Performance:** two pollers (2s) while updating plus 2s reconnect loop; no visibility pause. Fine for LAN.
- **Suggested changes:** `harden` (reboot/restart state machines, confirmations, telemetry save), `clarify` (risk copy, from->to versions), `adapt` (wrap controls at 375px), `polish` (button hierarchy).
- **Not exercised (first pass):** all live (feature not enabled on this host); no restart/reboot/update/rollback/telemetry actions would have been clicked in any case.

#### Live pass (2026-10-14)
Rendered at `/appliance` with mocked appliance, auth and capability endpoints (fixtures adapted from `00-appliance.spec.ts`); mutating calls were answered by the mock only.
- **Desktop dark/light 1440x900:** no overflow. Page content targets are all 44px (positive). The shared sidebar's 23 links and Sign Out are 40px tall (outside this page's scope, but present here). Light theme is clean; disabled "Save telemetry" is pale tan on cream (low contrast) (`appl-2updates-d-light.png`).
- **Mobile 375: update-rail cluster NOT reproduced.** With "Update available" + Roll back (93x44) + Install (73x44) the row fits, no horizontal overflow, every control 44px tall. Rails stack the cluster under the title. The longer "Verification required" state was not reachable with plausible mocks, so the original inferred risk (no `flex-wrap`) is downgraded to unverified P3.
- **Tablet 768:** no overflow; "Check for updates" wraps to two lines (162x60).
- **Confirmations (all CONFIRMED live):**
  - Install Lucia dialog: "Lucia will verify every release part, back up Redis and SQLite, switch releases atomically, and restart services." No from/to versions, no downtime, no power-loss warning. Install OS dialog: no "do not power off", no reboot timing. Confirm button is `bg-rose/20` red text (rgb 212,117,107) for both routine installs (P1).
  - Restart AgentHost and Redis fire immediately: dialog count 0, request sent (P1).
  - Reboot dialog says "disconnect for several minutes"; after confirming, the notice says "disconnect briefly" (contradiction confirmed).
  - Esc closes dialogs and focus returns to the invoking button (positive).
- **Stale-after-reboot/offline: CONFIRMED P1.** After confirming Reboot and then making every appliance call fail for 7s: page still shows healthy "active" services, Reboot Jetson and both Restart buttons stay enabled, no alert or offline banner appears (status is non-null so the reconnect loop never runs).
- **Telemetry:** switch is local state; "Save telemetry" is disabled with an empty endpoint even when disabled, so it cannot be saved off after clearing (CONFIRMED). Reboot zone heading says "Restart Jetson OS" while its button says "Reboot Jetson" and the services section says "Restart" (naming inconsistency, NEW P3).
- **Console/network:** clean with the fixtures (only a 404 from an initially unmocked reboot path, an artifact of the mock).

---

## Cross-page observations
- **Capability gating and redirects are silent.** Visiting `/login`, `/setup` or `/appliance` in the wrong state redirects to `/` with no explanation; the global "Loading..." has no error/timeout (observed multi-second hang under load, `App.tsx:172-182`).
- **Three wizard patterns, one good.** Installer has the progress bar, 48px controls, mobile step text, key acknowledgement and reduced-motion handling. Setup has none of those; Login is minimal. Port Installer's patterns (`StepHeading`, mobile progress, key gate) into Setup.
- **Credential display is inconsistent:** Setup shows a copy button but no acknowledgement; Installer has acknowledgement but no copy. Merge into one `OneTimeSecret` component (`extract`).
- **Unlabeled inputs and icon-only buttons** (audit systemic) are concentrated in Setup; Installer and Appliance use proper labels.
- **Reduced motion is a strength** in Installer/Appliance (`index.css:599`, `:642`), not applied to Login/Setup spinners or the app loading pulse.
- **Stale docs:** `docs/images/setup-*.png` show a different wizard.

## Top 5 for this group
1. **P1** Setup: "Complete Setup" has no error handling or busy state (`SetupPage.tsx:86-90,1119`), and the one-time dashboard key has no save-acknowledgement and is lost on reload (`:304-324,472`).
2. **P1** Appliance: reboot and AgentHost/Redis restart give no confirm/offline state; page stays stale and enabled (`AppliancePage.tsx:196-205,396-405`).
3. **P1** Appliance update confirmations omit versions, power-loss warning and downtime, and use a destructive red confirm for routine installs (`:453-457`, `ConfirmDialog.tsx:75`).
4. **P1** Installer: failed installs do not mark the failing stage or give diagnostics, and the owner key cannot be copied (`InstallerPage.tsx:1131,1082-1100`).
5. **P1** Setup provider step: icon-only 29px actions, immediate delete, and test message discarded (`SetupPage.tsx:659-677`), plus placeholder-only unlabeled inputs across the wizard.

Counts (all inferred): P0 0, P1 9 (Setup 5, Installer 2, Appliance 3 minus overlap counted once in Top 5 = see per-page lists), P2 ~27, P3 ~19.

## Not exercised (group)
Live visual passes (dark/light, 375/768/1024/1440), console and network inspection, and every mutating control were not done: the coordinator halted browser use due to shared SSE connection exhaustion, and Login/Setup/Installer cannot be viewed signed in anyway. Mock-based Playwright rendering was not attempted. A serial live pass should verify the mobile overflow items (Setup key `<code>`, Appliance rail cluster at 375px, ThemeSelector overlap at 640-1040px).

## Live pass summary
Method: separate Vite dev server (:5199, stopped afterwards) plus headless Playwright with all `/api` requests mocked; the user's session and the real backend were not touched. Dark and light at 1440x900, 375x812, 768x1024; keyboard Tab; console and request logs per page.

- **Confirmed live:** 26 (Login error semantics, infinite loading, Setup 375px key overflow with Copy button pushed off-card, ThemeSelector overlap 640-1040px, Complete Setup unhandled failure, provider test message discarded, instant provider Delete, Test-before-Save, key lost on reload, empty focusable button, placeholder-only inputs, 29px (measured 34x26) icon buttons, Installer failed-stage unmarked, no owner-key copy, identical disk labels, open Wi-Fi blocked, no aria-live, Appliance missing versions and power-loss warning, red confirm for installs, immediate service restart, reboot copy contradiction, stale page after reboot, telemetry save gating).
- **Refuted / not reproduced:** 2 (Appliance update-rail overflow at 375px: fits; Installer mobile progress bar mismatch: only a transition frame).
- **New:** 3 (P1 `/api/auth/status` 500 shows the first-run Setup wizard on a configured install, `AuthContext.tsx` catch; P3 "Restart Jetson OS" heading vs "Reboot Jetson"; P3 Installer ThemeSelector buttons 38px wide on mobile / "Skip for now" 20px tall in Setup).
- **Remaining unverified:** real-device iOS input zoom, real password-manager behavior, Setup "Done" step after a successful complete (App swaps routes), Appliance "Verification required" cluster width, real-hardware polling behavior and the real `/appliance`, `/install` pages on a Jetson. Real-instance redirects for `/login` and `/setup` were already recorded in the first pass.
