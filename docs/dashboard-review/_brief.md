# Shared brief for per-page dashboard reviewers

You are reviewing part of the Lucia dashboard (React 19 + Vite + Tailwind) for UI pain points, friction, bugs and design problems, on desktop AND mobile. Output is a written review only.

## Ground rules (hard)

- **Read-only on code.** Do not edit anything under `lucia-dashboard/` or any source file. Your only write is your own review file named in your assignment, under `E:\github\lucia-dotnet\docs\dashboard-review\`.
- **Read-only on the live instance.** The app at `http://localhost:7233` is connected to the user's real Home Assistant and real config, and a session is already signed in. You may navigate, scroll, open dropdowns, open create/edit forms and dialogs, switch tabs, use filters and search. You must NOT click anything that saves, deletes, resets, restarts, applies, imports, runs, starts/stops, sends, or toggles real settings (Save, Delete, Reset, Apply, Restart, Run, Start, Stop, Send, Import, Auto-assign, Clear cache, enable/disable switches that persist). Cancel out of anything you open. Do NOT send messages in the Conversation page. Do NOT log out, change credentials, reveal or copy secrets, or visit any host other than localhost. If you are unsure whether a control mutates state, do not click it; note it as "not exercised" in the report.
- **Never change the theme setting.** Use `resize_window` with `colorScheme: "light"|"dark"` on your own tab to see both themes (the app follows System).
- **Browser:** use the built-in browser tools (`mcp__Claude_Browser__*`; load with ToolSearch `select:` if deferred). Other reviewers work at the same time in the same pane. First call `tabs_create` and ALWAYS pass your own `tabId` to every call. Never use, navigate or close another tab. Close your tab when finished and set your tab back to `preset: "desktop"` before closing.
- **Viewports:** desktop (default pane size, also try 1440x900 and 1024x768) and mobile (`preset: "mobile"` 375x812; also try tablet 768x1024 at least on the busiest pages). Reload after switching to mobile so mobile gates re-run.
- If a page shows real personal data (entity names, memories, tokens), describe it generically in your report; do not copy secrets.
- If a page errors, is empty, or cannot be reached, report exactly what you saw (empty state, error text, console error, failed request) — that is a finding, not a blocker.

## What to read first

1. `E:\github\lucia-dotnet\PRODUCT.md` and `E:\github\lucia-dotnet\DESIGN.md` (Operate mode; technical Home Assistant operators; the Observatory design system: warm neutral + restrained amber, Outfit/DM Sans, 12px radii on controls, System/Light/Dark themes).
2. `C:\Users\zackw\.claude\skills\impeccable\reference\critique.md` (Nielsen heuristics scoring guide, cognitive load checklist, personas) and `C:\Users\zackw\.claude\skills\impeccable\reference\audit.md` (a11y, performance, theming, responsive checks). Use their criteria and P0-P3 severity scale.
3. The existing summary `E:\github\lucia-dotnet\docs\dashboard-audit.md` so you do not just repeat it; extend it with per-page specifics. Known systemic issues there (native confirm(), unlabeled inputs, no focus-visible, raw palette colors, off-spec 4px radii, emoji as icons) should be cited by file:line on your pages, not rediscovered generically.

## Method for EACH page in your assignment

1. Read the page source fully (not just grep) plus the components it uses.
2. Open it live on desktop in dark and light; capture what the first viewport communicates, hierarchy, density, empty/loading/error states, what is clickable, and what happens on interaction that is safe to exercise. Check the browser console (`read_console_messages`) and network (`read_network_requests`) for errors, 4xx/5xx and slow or repeated requests.
3. Repeat on mobile (375px) and, for busy pages, tablet: horizontal overflow, clipped or overlapping content, tap targets under 44px, tables, modals and sticky elements, keyboard overlap on inputs, thumb reach of primary actions, text legibility.
4. Walk the page's main task as Alex (power user), Jordan (first-timer) and Sam (keyboard/screen reader) and count clicks and decision points.
5. Run `C:/Users/zackw/.claude/skills/impeccable/scripts/impeccable.cmd detect --json <your page files>` from `E:\github\lucia-dotnet` and verify findings.
6. Verify every claim you make in code or in the live UI. Cite `file:line`. Separate "observed live" from "inferred from source". No generic advice.

## Review file format (write to the file named in your assignment)

For each page, in this order:

### <Page name> (`/route`, `src/pages/<File>.tsx`)
- **Purpose and primary task** (one or two lines)
- **Scores:** Nielsen total /40 with one-line reasons for any heuristic scored 1 or below; cognitive-load failures out of 8
- **Desktop findings** (observed live vs inferred)
- **Mobile findings** (observed live vs inferred; include widths tried)
- **Pain points and friction** (ordered; each with P0-P3, file:line, why it hurts, concrete fix)
- **Bugs and errors** (console, network, layout, logic)
- **Performance notes**
- **Suggested changes** (specific: component, copy, layout, interaction; say which impeccable command fits: adapt, clarify, harden, layout, optimize, polish, typeset, colorize, distill, onboard, shape, extract)
- **Not exercised** (what you did not click and why)

End the file with a **Cross-page observations** section for your group and a **Top 5 for this group**.

## Final reply to the orchestrator

Under 300 words: the path of your file, pages covered, counts of P0/P1/P2/P3, the 5 most important findings, and anything you could not review and why. Do not paste the whole review back.
