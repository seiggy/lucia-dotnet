---
name: Lucia Observatory
description: A compact operational dashboard built from warm mineral neutrals, amber focus, and restrained depth.
colors:
  void: "var(--color-void)"
  obsidian: "var(--color-obsidian)"
  charcoal: "var(--color-charcoal)"
  basalt: "var(--color-basalt)"
  slate-warm: "var(--color-slate-warm)"
  stone: "var(--color-stone)"
  ash: "var(--color-ash)"
  dust: "var(--color-dust)"
  fog: "var(--color-fog)"
  mist: "var(--color-mist)"
  cloud: "var(--color-cloud)"
  light: "var(--color-light)"
  bright: "var(--color-bright)"
  amber: "var(--color-amber)"
  amber-dim: "var(--color-amber-dim)"
  amber-glow: "var(--color-amber-glow)"
  amber-pale: "var(--color-amber-pale)"
  rose: "var(--color-rose)"
  rose-dim: "var(--color-rose-dim)"
  sage: "var(--color-sage)"
  sage-dim: "var(--color-sage-dim)"
  ember: "var(--color-ember)"
  info: "var(--color-info)"
  on-accent: "var(--color-on-accent)"
typography:
  headline:
    fontFamily: "Outfit, system-ui, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 600
    lineHeight: "2rem"
    letterSpacing: "normal"
  title:
    fontFamily: "Outfit, system-ui, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 600
    lineHeight: "1.75rem"
    letterSpacing: "normal"
  body:
    fontFamily: "DM Sans, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: "1.25rem"
    letterSpacing: "normal"
  control:
    fontFamily: "DM Sans, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 600
    lineHeight: "1.25rem"
    letterSpacing: "normal"
  label:
    fontFamily: "DM Sans, system-ui, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 500
    lineHeight: "1rem"
    letterSpacing: "0.05em"
rounded:
  md: "6px"
  lg: "8px"
  xl: "12px"
  "2xl": "16px"
  full: "9999px"
spacing:
  "1": "4px"
  "1.5": "6px"
  "2": "8px"
  "2.5": "10px"
  "3": "12px"
  "4": "16px"
  "5": "20px"
  "6": "24px"
  "8": "32px"
components:
  button-primary:
    backgroundColor: "{colors.amber}"
    textColor: "{colors.on-accent}"
    typography: "{typography.control}"
    rounded: "{rounded.xl}"
    padding: "10px 20px"
  button-secondary:
    backgroundColor: "{colors.basalt}"
    textColor: "{colors.fog}"
    typography: "{typography.body}"
    rounded: "{rounded.xl}"
    padding: "10px 20px"
  input:
    backgroundColor: "{colors.basalt}"
    textColor: "{colors.light}"
    typography: "{typography.body}"
    rounded: "{rounded.xl}"
    padding: "12px 16px"
  navigation-item:
    textColor: "{colors.fog}"
    typography: "{typography.body}"
    rounded: "{rounded.lg}"
    padding: "10px 12px"
  theme-selector:
    backgroundColor: "{colors.basalt}"
    rounded: "{rounded.lg}"
    padding: "4px"
---

# Design System: Lucia Observatory

## Overview

**Creative North Star: "The Daylight Observatory"**

Lucia Observatory is an Operate-mode system for long technical sessions. It keeps the authenticated dashboard dense and predictable while warm mineral neutrals, amber focus, and small changes in depth preserve the product's identity in both bright and dim rooms.

Theme choice extends the existing shell instead of becoming a separate destination. System, Light, and Dark use one semantic role vocabulary, so setup, login, navigation, charts, dialogs, and operational pages keep the same hierarchy when the resolved mode changes.

**Key Characteristics:**
- Compact operational controls and route-first composition
- Warm neutral surfaces with amber reserved for focus, selection, and progress
- One semantic token vocabulary across light and dark modes
- Borders, translucency, and low glows instead of heavy elevation

## Colors

The palette uses named material and state roles. Dark mode supplies the default values; `html[data-theme='light']` replaces their values without changing the names consumed by components.

### Primary

- **Amber:** The focus, active navigation, primary action, progress, and selected-control role. It resolves to `#e2a84b` in dark mode and `#8c5b0f` in light mode.
- **Dim Amber:** The quieter edge and connector role for active graph states. It resolves to `#c4923f` in dark mode and `#724808` in light mode.
- **Amber Glow:** The primary hover role. It resolves to `#c49030` in dark mode and `#714806` in light mode.

### Secondary

- **Sage:** Success and ready states. It resolves to `#7dab8c` in dark mode and `#397151` in light mode.
- **Rose and Ember:** Error text, borders, and low-opacity error fills. Rose resolves to `#d4756b` in dark mode and `#a13f3a` in light mode; ember resolves to `#c45a4a` and `#b42318`.
- **Information Blue:** Tool activity and informational graph states. It resolves to `#60a5fa` in dark mode and `#2563eb` in light mode.

### Neutral

- **Void:** The page canvas. It resolves to `#0d0c0a` in dark mode and `#f1f2ef` in light mode.
- **Obsidian:** The sidebar and shell background. It resolves to `#12110f` in dark mode and `#fbfcfa` in light mode.
- **Charcoal and Basalt:** Panel and control surfaces. They resolve to `#171512` and `#1a1816` in dark mode, then `#ffffff` and `#f5f6f3` in light mode.
- **Stone and Ash:** Borders, dividers, inactive graph edges, and scrollbars. They resolve to `#2e2b28` and `#3d3935` in dark mode, then `#d1d5ce` and `#b4bab1` in light mode.
- **Dust, Fog, Cloud, and Light:** Muted, secondary, body, and high-emphasis text. Their dark values run from `#918b84` to `#ece8e2`; their light values run from `#626861` to `#1c211c`.
- **On Accent:** Primary-button text. It resolves to `#17130d` in dark mode and `#fffdf9` in light mode.

### Named Rules

**The Semantic Remap Rule.** Page and component code uses semantic tokens such as `void`, `basalt`, `fog`, `light`, `amber`, `sage`, and `rose`. Only the root theme definition changes their values; page components do not carry light-specific or dark-specific classes.

**The Amber Restraint Rule.** Amber marks focus, selection, primary action, progress, or active system state. Neutral surfaces carry ordinary structure.

## Typography

**Display Font:** Outfit, with `system-ui` and `sans-serif` fallbacks
**Body Font:** DM Sans, with `system-ui` and `sans-serif` fallbacks
**Label/Mono Font:** DM Sans for labels; the browser monospace stack for keys and code

**Character:** Outfit gives headings and the Lucia wordmark a compact geometric shape. DM Sans keeps dense navigation, forms, status text, and data readable without making the dashboard feel like a generic admin template.

### Hierarchy

- **Headline** (600, `1.5rem`, `2rem`): Login, setup, and major page headings.
- **Title** (600, `1.25rem`, `1.75rem`): Setup steps and primary section titles.
- **Body** (400, `0.875rem`, `1.25rem`): Navigation labels, instructions, form text, and operational content.
- **Control** (600, `0.875rem`, `1.25rem`): Primary actions and high-emphasis controls.
- **Label** (500, `0.75rem`, `1rem`, `0.05em`): Field labels and compact shell captions; uppercase appears only on short structural labels.
- **Micro label** (500, `0.625rem`): Theme options, step metadata, and terse status details.

### Named Rules

**The Compact Type Rule.** Operational text stays between `0.625rem` and `1.5rem`. Larger display type is absent from the shipped dashboard.

## Layout

The authenticated shell reserves a fixed `256px` sidebar from the `768px` breakpoint upward. Main content uses `16px` horizontal padding on small screens, `24px` from `640px`, and `32px` from `1024px`. The current route remains the primary content in the first viewport.

Below `768px`, the sidebar becomes an off-canvas drawer with a dimmed overlay and a sticky `56px` top bar. The drawer keeps the full `256px` width. Its transform uses a `300ms` standard easing transition, while opacity changes use `200ms ease`.

Public setup and login screens center a single column over the observatory canvas. Login is capped at `384px`; setup is capped at `672px`. Mobile screens reserve `96px` above the content so the fixed theme selector does not overlap the task, then remove that offset from `640px` upward.

The theme selector sits at the upper-right shell edge on loading, setup, and login views with a fixed `170px` width. After authentication it moves into the sidebar footer under the Appearance label.

### Named Rules

**The Route-First Rule.** Theme controls stay at the shell edge. They do not displace the current workflow or become a page-level feature.

## Elevation & Depth

The system is layered rather than lifted. Surface changes and low-contrast borders separate the canvas, shell, panels, and controls. Public cards add an 80% basalt glass fill, a softened stone border, and `12px` backdrop blur. Amber glows are limited to identity marks and public cards: `0 0 20px -5px` at 15% amber and `0 0 10px -3px` at 12% amber. Active mesh nodes may add a compact `12px` state-colored glow.

### Shadow Vocabulary

- **Identity glow** (`0 0 20px -5px color-mix(in srgb, var(--color-amber) 15%, transparent)`): Lucia marks on public screens.
- **Panel glow** (`0 0 10px -3px color-mix(in srgb, var(--color-amber) 12%, transparent)`): Login and setup glass panels.
- **Active-state glow** (`0 0 12px` with the current state border at 25% alpha): Processing mesh nodes only.

### Named Rules

**The Restrained Depth Rule.** Borders and tonal layering do the structural work. Glow identifies a live or branded focal point; it does not decorate ordinary containers.

## Shapes

Controls use gently curved corners in a short scale: `6px` for compact icon and segmented buttons, `8px` for navigation items and small containers, `12px` for fields, actions, graph nodes, and operational cards, and `16px` for public login and setup panels. Full circles are reserved for progress steps, spinners, and status dots.

Borders are one pixel and low contrast. Active navigation adds a `3px` amber bar on the left with only its right corners rounded, preserving the vertical edge of the shell.

## Components

### Buttons

- **Shape:** Primary and secondary actions use a `12px` radius and compact `10px 20px` padding.
- **Primary:** Amber background, on-accent text, `0.875rem` semibold label. Hover changes to amber-glow; disabled controls keep their shape and drop to 40% opacity with a blocked cursor.
- **Secondary:** Basalt background, stone border, fog text. Hover shifts the border toward amber and the text toward light.
- **Success:** A low-opacity sage fill with sage text, used for connection tests and positive actions rather than general confirmation copy.

### Cards / Containers

- **Corner Style:** Operational cards use `12px`; public auth and setup panels use `16px`.
- **Background:** Operational cards use basalt at 50% or charcoal. Public panels use the shared glass treatment.
- **Shadow Strategy:** Most cards have no shadow. Public panels use the small amber panel glow.
- **Border:** One-pixel stone borders separate nested operational regions.
- **Internal Padding:** `20px` for operational cards and `24px` to `32px` for public panels.

### Inputs / Fields

- **Style:** Basalt fill, stone border, light text, muted dust placeholder, `12px` radius, and `12px 16px` padding.
- **Focus:** Remove the default outline, change the border to amber, and add a one-pixel amber ring at 30% alpha.
- **Error / Disabled:** Error feedback uses rose text with ember border and fill at low opacity. Disabled actions use opacity rather than a separate color system.

### Navigation

The desktop navigation is a vertically scrolling list of `0.875rem` medium labels with `18px` Lucide icons. Items use an `8px` radius and `10px 12px` padding. Hover adds a low stone fill; the active route changes to amber, adds an 8% amber fill, and shows the left indicator. On mobile, labeled icon buttons open and close the same navigation in an off-canvas drawer.

### Theme Selector

System, Light, and Dark form a three-column segmented control. Each option has a Lucide icon, a visible `0.625rem` label, a descriptive tooltip, an accessible name, and `aria-pressed` state. The group has `role="group"` and the accessible name Theme. Selected options use the obsidian surface, amber text, and a small shadow; unselected options use dust text and a stone hover fill. Keyboard focus uses a two-pixel amber ring at 60% alpha.

System is the fallback for missing or invalid stored values. It follows `prefers-color-scheme` and updates when the operating system changes. Explicit Light or Dark choices persist under `lucia-theme`. A head script resolves the saved preference before React renders, and the provider keeps `data-theme` and the native `color-scheme` property synchronized.

### Agent Mesh

The mesh is a fixed-height `420px` charcoal field with a stone border and `12px` corners. Nodes share the same shape and semantic state colors: amber for processing, blue for tool calls, sage for response generation, rose for errors, and neutral basalt for idle. Active edges thicken from one to two pixels and animate; active non-error nodes pulse without becoming draggable or selectable.

## Do's and Don'ts

### Do:

- **Do** use the semantic color roles from `index.css` in every page and component.
- **Do** keep the System, Light, and Dark options together and preserve their storage, pre-render resolution, and operating-system behavior.
- **Do** place the selector at the upper-right edge on public screens and in the sidebar footer after authentication.
- **Do** expose selection with `aria-pressed`, label icon-only controls, hide decorative icons from assistive technology, and retain visible keyboard focus.
- **Do** reserve amber for focus, selection, primary action, progress, and active system state.

### Don't:

- **Don't** add mode-specific classes or raw light/dark palette values to page components; remap semantic tokens at the root.
- **Don't** promote appearance controls into the primary route content.
- **Don't** use heavy shadows or broad glows on ordinary operational cards.
- **Don't** replace semantic success, error, information, and progress states with amber alone.