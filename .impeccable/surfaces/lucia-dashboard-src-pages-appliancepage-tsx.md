---
version: 1
slug: "lucia-dashboard-src-pages-appliancepage-tsx"
primary_target: "lucia-dashboard/src/pages/AppliancePage.tsx"
related_targets: ["lucia-dashboard/src/App.tsx","lucia-dashboard/src/appliance-api.ts"]
---

scope: Authenticated `/appliance` page on installed Jetson appliances only.
mode: Operate
audience: Home Assistant operators maintaining a Lucia appliance from phone or desktop.
job: Understand appliance health at a glance, compare Lucia and OS update channels, restart one service, configure optional telemetry without exposing secrets, and deliberately reboot the host.
action: Resolve the one item that needs attention without searching through generic configuration.
proof: Live manager-backed service states, installed versions, GitHub manifest compatibility, redacted telemetry state, and explicit operation feedback.
constraints: Keep Lucia Observatory tokens and typography; hide the route outside Appliance:Mode=Installed; 44-pixel touch targets; no secret echo; confirmation before host reboot; update channels remain visually and operationally separate.
direction: A compact command deck. The appliance identity and attention state form the header; Lucia and OS run as two independent update rails; service controls form one operational strip; telemetry is a focused configuration bay; reboot stays isolated at the end.
memorable-moment: Checking updates sends one amber scan across both release rails, then each rail settles independently into current, update available, incompatible, or unavailable.
motion-thesis: Motion explains update checking and state refresh only. Routine controls respond within 200 ms and reduced motion keeps all state changes visible without scans.
unresolved: OS apply remains blocked until the NVIDIA A/B updater passes its physical rollback gate.
