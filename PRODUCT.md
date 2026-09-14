# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Lucia primarily serves technical Home Assistant operators who self-host their smart-home stack. They monitor agent activity, inspect traces, configure integrations and models, manage entities, and troubleshoot automation behavior. Privacy-conscious homeowners and developers also use it to keep control of their data and extend the system.

## Product Purpose

Lucia is an open-source, privacy-first home assistant that coordinates specialized AI agents through Home Assistant. It replaces cloud-dependent assistants with local operation, while allowing optional cloud model providers. Success means users can operate, understand, and customize their assistant without surrendering control of their home data.

## Positioning

Lucia combines deep Home Assistant integration, local-first processing, and specialized multi-agent orchestration. Users can choose local or cloud models and inspect the system through a management dashboard instead of relying on an opaque vendor service.

## Operating Context

Users run Lucia on self-hosted infrastructure and open the web dashboard for setup, monitoring, configuration, trace inspection, agent management, entity mapping, voice controls, scheduled tasks, and diagnostics. The dashboard must remain usable for long technical sessions and in both dim and bright rooms.

## Capabilities and Constraints

- The dashboard is a React 19, TypeScript, Vite, and Tailwind CSS web application.
- The product supports local models and optional cloud providers.
- The dashboard includes setup and login flows plus more than 20 authenticated operational routes.
- Theme selection must offer System, Light, and Dark choices, persist in the browser, and apply to setup, login, authenticated routes, dialogs, charts, and transient feedback.
- Existing product behavior, route structure, terminology, and Observatory identity must remain intact when themes change.

## Brand Commitments

The product name is Lucia, pronounced "LOO-sha." The name refers to light, wisdom, and guidance. Privacy, user control, and an open-source identity are durable commitments. The repository logo is at `lucia.png`.

## Evidence on Hand

- Product mission and audience: `.docs/product/mission.md`
- Accepted product decisions: `.docs/product/decisions.md`
- Dashboard capabilities and routes: `lucia-dashboard/README.md`
- Current application shell and navigation: `lucia-dashboard/src/App.tsx`
- Current Observatory theme tokens: `lucia-dashboard/src/index.css`

Future interface work must not invent customer claims, usage metrics, testimonials, or accessibility certifications.

## Product Principles

- Keep home data and system control with the user.
- Make agent behavior inspectable and configurable.
- Support local operation first, with cloud services as an explicit option.
- Favor dependable operational clarity over decorative presentation.
- Preserve expert control without making routine tasks harder than necessary.