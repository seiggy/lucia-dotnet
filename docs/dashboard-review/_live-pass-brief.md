# Serial live pass: brief

This is a second pass. A source review already exists for your page group (the file named in your assignment). Your job is the live verification the first pass could not do, then to update that file.

Read first: `_brief.md` in this folder (ground rules and method still apply, including every read-only restriction) and your group's existing review file.

## Why the first pass failed, and how to avoid it

Parallel tabs each held streaming connections and exhausted Chrome's six-per-host limit, so fetches hung. You are the only agent using the browser now.

- Use ONLY the existing tab `seed`. Do NOT call `tabs_create`. Pass `tabId: "seed"` on every call. Never open a second tab or window.
- Navigate by URL to reach each page (this reloads and drops stale streams). Do not keep a page open longer than needed.
- If a page hangs on "Loading..." for more than 15 seconds, run `curl -s -m 5 -o /dev/null -w "%{http_code}" http://localhost:7233/api/auth/status` to check the server is fine, then reload once. If it still hangs, record it as a finding and move on.
- Take screenshots at scale 0.5 unless detail is needed. Prefer `get_page_text` / `read_page` for text.
- Reset the viewport to `preset: "desktop"` and `colorScheme` back to the app's own (System) when you finish; leave the tab on `http://localhost:7233/`.

## What to verify, per page

1. Desktop 1440x900 dark, then light (`resize_window` with `colorScheme`). Note contrast failures, raw-color leaks, anything unreadable in either theme.
2. Mobile 375x812 (`preset: "mobile"`), reload after switching. Then tablet 768x1024 on the busiest pages. Check horizontal overflow (`document.documentElement.scrollWidth > innerWidth` via `javascript_tool`), clipped or overlapping content, tap targets (measure bounding boxes of buttons and links with `javascript_tool`; list those under 44px and under 24px), modal behavior, sticky bars, text size.
3. Console (`read_console_messages`) and network (`read_network_requests`): errors, 4xx/5xx, repeated or slow calls.
4. Confirm or refute each `[src]`/inferred claim in the existing review that is checkable live. Say "CONFIRMED live", "REFUTED live", or "not checkable".
5. Walk the page's main task by keyboard (Tab, Shift+Tab, Enter, Esc) and note focus visibility and traps.

## Update the existing review file

Edit your group's file in place:
- Add a **Live pass (2026-10-14)** subsection under each page: desktop dark/light, mobile, tablet results; confirmed/refuted claims; new findings (P0-P3, file:line when you can find the cause).
- Change evidence tags on findings you verified from `[src]`/inferred to `[live]`.
- Append `## Live pass summary` at the end: counts confirmed/refuted/new, remaining unverified items.

## Final reply

Under 250 words: confirmed, refuted and new counts, the 3 most important live-only findings, anything that could not be reached and why.
