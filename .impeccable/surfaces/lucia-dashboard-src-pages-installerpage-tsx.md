---
version: 1
slug: "lucia-dashboard-src-pages-installerpage-tsx"
primary_target: "lucia-dashboard/src/pages/InstallerPage.tsx"
related_targets: ["lucia-dashboard/src/App.tsx","lucia-dashboard/src/installer-api.ts"]
---

scope: Captive microSD installer at `/install`, from first connection through NVMe write and reboot.
mode: Operate
audience: Home Assistant users ranging from first-time self-hosters to experienced operators, usually holding a phone beside the appliance.
job: Securely claim the installer, choose the correct storage and home Wi-Fi, set a recovery password, understand the erase consequence, and start installation without needing a terminal.
action: Review the named target drive, enter its exact displayed identity to approve erasure, then keep the page open while Lucia installs.
proof: Live disk inventory, Wi-Fi scan, selected hostname, persistent installer phase, and a clear handoff address for the installed appliance.
constraints: Keep Lucia Observatory tokens and typography; work from 320px phones through desktop; use no new motion dependency; respect reduced motion; never hide destructive details; never expose the recovery or Wi-Fi password after submission; appliance modes only.
direction: A continuous signal ribbon joins the SD installer, selected NVMe, home network, and Lucia. Desktop keeps the live device path beside the current task; mobile compresses it into a stage header above one focused action.
memorable-moment: After final approval, one amber signal travels from the SD node into the NVMe node and resolves into the chosen `hostname.local` destination while installation continues.
motion-thesis: The signal ribbon explains continuity between steps. Routine transitions stay under 300ms; the one install handoff runs once, remains readable without animation, and stops under reduced motion.
unresolved: Physical phone testing will determine whether captive browser chrome leaves enough vertical room at 320px.
