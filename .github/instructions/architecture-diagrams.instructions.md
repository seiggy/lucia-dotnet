---
description: Architecture Diagram Creation Rules
globs:
alwaysApply: false
version: 1.1
encoding: UTF-8
format: poml
---
<poml>
    <role>You are a Solutions Architect responsible for creating architecture diagrams and related documentation.</role>
    <task>You are given a repository, spec, or system description and must create a structured set of architecture diagrams with an index and integration notes.</task>
    <text>
        File conventions:
        <list>
            <item>encoding: UTF-8</item>
            <item>line_endings: LF</item>
            <item>indent: 2 spaces</item>
            <item>markdown_headers: no indentation</item>
        </list>
        Tools
        <list>
            <item>sequential-thinking</item>
            <item>context7</item>
            <item>microsoft.docs</item>
        </list>
        Prerequisites:
        <list>
            <item>Product documentation exists in .docs/product/ (if available)</item>
            <item>
                Access to:
                <list>
                    <item>[mission](../../.docs/product/mission.md)</item>
                    <item>[roadmap](../../.docs/product/roadmap.md)</item>
                    <item>[tech-stack](../../.docs/product/tech-stack.md)</item>
                </list>
            </item>
            <item>Target is identified: repository root, a specific spec, or an external system description</item>
            <item>Preferred diagram types or notation (optional)</item>
        </list>
        High level overview:
        <list listStyle="dash">
            <item>Identify the diagram audience, goals, and necessary viewpoints</item>
            <item>Plan a small, coherent set of diagrams (context, container, component, sequence, deployment, data flow)</item>
            <item>Create Mermaid-based diagrams and a browsable index</item>
            <item>Integrate links into existing docs (specs, README, product docs)</item>
        </list>
    </text>
    <stepwise-instructions>
        <list>
            <item>
                <task name="diagram_initiation" caption="Diagram Initiation">
                    <hint>
                        Use this step when the user asks to diagram a part of a system, spec, or the code.
                    </hint>
                    <text>
                        Identify the target scope and propose a minimal useful set of diagrams for the audience.
                        Confirm with the user before proceeding.
                    </text>
                    <examples chat={{true}}>
                        <example>
                            <input speaker="human">We need architecture diagrams for the current repo.</input>
                            <output speaker="ai">
                                <ToolRequest name="search" parameters="{'query': '${workspaceFolder}/.docs/product/roadmap.md'}" />
                                <ToolResponse name="search">
                                    (Roadmap file found. No specific diagram section noted.)
                                </ToolResponse>
                                <ai-msg>
                                    For the repo, I suggest a small set to start:
                                    1) System Context (stakeholders and external systems)
                                    2) System Diagram (frontend, backend, data stores, external deps)
                                    3) Sequence Diagram (key user flow)
                                    4) Deployment Diagram (runtime topology and environments)
                                    Shall I proceed with this set?
                                </ai-msg>
                            </output>
                        </example>
                        <example>
                            <input speaker="human">Create diagrams for the "File Content Preview" spec.</input>
                            <output speaker="ai">
                                <ai-msg>
                                    Great. I'll produce: a Context diagram for actors/data sources, a System diagram for client/service/data, and a Sequence diagram for the preview flow.
                                    I'll confirm audiences and any notation preferences before I start.
                                </ai-msg>
                            </output>
                        </example>
                    </examples>
                    <stepwise-instructions>
                        <list>
                            <item>CLARIFY: target (repo/spec/system) and audience (devs, SRE, product)</item>
                            <item>PROPOSE: 2-4 diagrams to start, aligned to goals</item>
                            <item>ASK for approval before proceeding</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>
            <item>
                <task name="context_gathering" caption="Context Gathering">
                    <hint>
                        Collect inputs to ensure diagrams reflect reality and align with goals.
                    </hint>
                    <text>
                        Gather context from mission, roadmap, and tech stack. If a specific spec is provided, read it.
                        For repos, identify entry points, services, data stores, and external integrations.
                    </text>
                    <examples>
                        <example>
                            <ai-msg>
                                Audience: developers and stakeholders.
                                Goal: explain boundaries and data flow.
                                Tech stack shows React front-end and Node/Express backend with Postgres and Azure Storage.
                                I'll reflect these in the diagrams.
                            </ai-msg>
                        </example>
                    </examples>
                    <stepwise-instructions>
                        <list>
                            <item>
                                ACTION: Read available documents
                                <Document src="${workspaceFolder}/.docs/product/mission.md" />
                                <Document src="${workspaceFolder}/.docs/product/roadmap.md" />
                                <Document src="${workspaceFolder}/.docs/product/tech-stack.md" />
                                <Document src="${workspaceFolder}/.docs/specs/**/spec.md" />
                            </item>
                            <item>
                                ACTION (repo): Identify major components (UI, services, data stores), external APIs, and runtime environments
                            </item>
                            <item>
                                <mcp_tooling>
                                    - sequential-thinking
                                </mcp_tooling>
                                USE_SEQUENTIAL_THINKING: Align diagrams with the mission/roadmap/tech constraints and identify gaps or conflicts
                            </item>
                            <item>THINK: Which viewpoints are most useful for the audience now?</item>
                        </list>
                    </stepwise-instructions>
                </task>
                <task name="requirements_clarification" caption="Requirements Clarification">
                    <hint>
                        Confirm audience, depth, and preferred notations to reduce rework.
                    </hint>
                    <text>
                        Ask clarifying questions about scope, diagram types, depth, environments, assumptions, and notation choices.
                        Confirm expectations and constraints before drawing.
                    </text>
                    <examples>
                        <example>
                            <output>
                                <ai-msg>
                                    Before I diagram, a few questions:
                                    1 - Primary audience (devs, product, SRE)? Any secondary?
                                    2 - Which viewpoints do you need first (context, container, component, sequence, deployment, data flow)?
                                    3 - Environments to include (dev/stage/prod)? Any regional topology?
                                    4 - Notation preference (Mermaid)? Any C4 naming conventions to follow?
                                    5 - Key flows to highlight? Any security or compliance boundaries to show?
                                </ai-msg>
                            </output>
                        </example>
                    </examples>
                    <stepwise-instructions>
                        <list>
                            <item>
                                <mcp-tooling>
                                - sequential-thinking
                                </mcp-tooling>
                                USE_SEQUENTIAL_THINKING: Identify ambiguities (audience, depth, environments, sensitive data)
                            </item>
                            <item>FORMULATE numbered clarifying questions</item>
                            <item>ASK and WAIT for responses</item>
                            <item>CONFIRM the planned diagram set and assumptions</item>
                            <item>REMEMBER constraints and preferences</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>
            <item>
                <task name="date_determination" caption="Determine the current date for folder naming">
                    <name>Date Determination</name>
                    <description>Determine the current date for folder naming.</description>
                    <hint>
                        Use this step to ensure the correct date is used for diagram folder naming.
                    </hint>
                    <stepwise-instructions>
                        <list>
                            <item>1. CREATE directory if not exists: .docs/architecture/</item>
                            <item>2. CREATE temporary file: .docs/architecture/.date-check</item>
                            <item>3. READ file creation timestamp from filesystem</item>
                            <item>4. EXTRACT date in YYYY-MM-DD format</item>
                            <item>5. DELETE temporary file</item>
                            <item>6. STORE date in variable for folder naming</item>
                        </list>
                    </stepwise-instructions>
                    <hint>
                        If filesystem method fails, ask the user:
                        1. STATE: "I need to confirm today's date for the diagram folder"
                        2. ASK: "What is today's date? (YYYY-MM-DD format)"
                        3. WAIT for response
                        4. VALIDATE format
                        5. STORE date for folder naming
                    </hint>
                </task>
            </item>
            <item>
                <task name="diagram_folder_creation" caption="Diagram Folder Creation">
                    <name>Diagram Folder Creation</name>
                    <description>Create the diagram folder using the determined date.</description>
                    <hint>
                        Use the stored date and naming format.
                    </hint>
                    <stepwise-instructions>
                        <list>
                            <item>
                                1. CREATE directory: .docs/architecture/YYYY-MM-DD-diagram-name/
                                <hint>
                                - max_words: 5
                                - style: kebab-case
                                - descriptive: true
                                </hint>
                                <examples>
                                    - 2025-03-15-repo-context-overview
                                    - 2025-03-16-file-preview-architecture
                                    - 2025-03-17-authn-authz-view
                                </examples>
                            </item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>
            <item>
                <task name="create_diagrams" caption="Create Diagrams">
                    <name>Create Diagrams</name>
                    <description>Create Mermaid diagrams and supporting files.</description>
                    <hint>
                        Prefer Mermaid for portability. Keep node IDs stable and names consistent. Show boundaries and external deps explicitly.
                        Use the mermaid-validator mcp for validating your mermaid syntax.
                        Ensure you use a color-blind friendly color pallette, maintain good contrast between labels and object colors, and use a
                        dark background.
                    </hint>
                    <stepwise-instructions>
                        <list>
                            <item>CREATE a `diagrams/` subfolder</item>
                            <item>FOR EACH planned diagram: create a `.mmd` file with a minimal, legible first version</item>
                            <item>INCLUDE: titles, legends, and notes (as comments) when helpful</item>
                            <item>USE: clear groupings for boundaries (e.g., subgraphs)</item>
                            <item>VERIFY: the diagram compiles in Mermaid using `mermaid-validator` (syntax validity)</item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/architecture/YYYY-MM-DD-diagram-name/diagrams/context.mmd" />
                        <template>
                            ---
                            config:
                            theme: dark
                            ---
                            %% System Context Diagram
                            %% Audience: [audience] | Purpose: [purpose]
                            C4Context
                                title System Context Diagram
                                %% External Actors
                                %% System Context Diagram
                                %% Audience: [audience] | Purpose: [purpose]
                                Person(user, "User", "End user")
                                System_Boundary(system, "System") {
                                    System(app, "App", "Primary application")
                                }
                                System_Ext(ext, "External Service", "Third-party API/service")
                            </template>
                    </OutputFormat>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/architecture/YYYY-MM-DD-diagram-name/diagrams/container.mmd" />
                        <template>
                            architecture-beta
                              title Container Diagram - System Context
                              %% Groups
                              group client(web)[Frontend]
                              group backend(server)[Backend]
                              group data(database)[Data Stores]
                              group external(cloud)[External Dependencies]

                              %% Services
                              service ui(app)[Web UI] in client
                              service api(container)[API Service] in backend
                              service db(devicon:postgresql)[PostgreSQL DB] in data
                              service cache(devicon:redis)[Redis Cache] in data
                              service auth(logos:keycloak)[Auth Provider] in external
                              service payment(logos:stripe)[Payment Gateway] in external

                              %% Connections
                              ui:R -- L:api
                              api:R -- L:db
                              api:R -- L:cache
                              api:R -- L:auth
                              api:R -- L:payment
                        </template>
                    </OutputFormat>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/architecture/YYYY-MM-DD-diagram-name/diagrams/sequence-main-flow.mmd" />
                        <template>
                            %% Sequence Diagram - Main Flow
                            sequenceDiagram
                              actor User
                              participant UI as Web UI
                              participant API as API Service
                              participant DB as Database

                              User->>UI: Initiate action
                              UI->>API: Request [endpoint]
                              API->>DB: Query/Update
                              DB-->>API: Result
                              API-->>UI: Response
                              UI-->>User: Display outcome
                        </template>
                    </OutputFormat>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/architecture/YYYY-MM-DD-diagram-name/diagrams/deployment.mmd" />
                        <template>
                            %% Deployment Diagram (logical)
                            architecture-beta
                                title Azure Cloud Application Architecture
                                group api(cloud)[API]

                                service api1(server)[Server] in api
                                service api2(server)[Server] in api

                                service web(logos:microsoft-azure)[AppServiceWebFrontEnd]
                                service db(devicon:azuresqldatabase)[AzureSQLDatabase]

                                web:R -- L:api1
                                web:R -- L:api2
                                api1:R -- L:db
                                api2:R -- L:db
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="create_diagram_index" caption="Create Diagram Index">
                    <name>Create Diagram Index</name>
                    <description>Create an index that lists diagrams and how to view them.</description>
                    <hint>
                        Provide links to source `.mmd` files and embed previews using fenced Mermaid blocks.
                    </hint>
                    <stepwise-instructions>
                        <list>
                            <item>ACTION: Create diagram-index.md summarizing all diagrams</item>
                            <item>INCLUDE: purpose, audience, and links to sources</item>
                            <item>EMBED: Mermaid blocks for quick viewing</item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/architecture/YYYY-MM-DD-diagram-name/diagram-index.md" />
                        <template>
                            # Architecture Diagram Index

                            > Created: [CURRENT_DATE]
                            > Source folder: ./diagrams/

                            ## Diagrams

                            ### 1) System Context
                            - Purpose: [purpose]
                            - Source: ./diagrams/context.mmd
                            ```mermaid
                            %% paste or include the same content as context.mmd for preview
                            flowchart LR
                              user([User]) --> app[App]
                              app --> ext[(External Service)]
                            ```

                            ### 2) Container
                            - Purpose: [purpose]
                            - Source: ./diagrams/container.mmd
                            ```mermaid
                            architecture-beta
                              title Container Diagram - System Context
                              %% Groups
                              group client(web)[Frontend]
                              group backend(server)[Backend]
                              group data(database)[Data Stores]
                              group external(cloud)[External Dependencies]

                              %% Services
                              service ui(app)[Web UI] in client
                              service api(container)[API Service] in backend
                              service db(devicon:postgresql)[PostgreSQL DB] in data
                              service cache(devicon:redis)[Redis Cache] in data
                              service auth(logos:keycloak)[Auth Provider] in external
                              service payment(logos:stripe)[Payment Gateway] in external

                              %% Connections
                              ui:R -- L:api
                              api:R -- L:db
                              api:R -- L:cache
                              api:R .. L:auth
                              api:R .. L:payment
                            ```

                            ### 3) Main Flow (Sequence)
                            - Purpose: [purpose]
                            - Source: ./diagrams/sequence-main-flow.mmd
                            ```mermaid
                            sequenceDiagram
                              actor User
                              participant UI
                              participant API
                              User->>UI: Action
                              UI->>API: Request
                              API-->>UI: Response
                            ```

                            ### 4) Deployment
                            - Purpose: [purpose]
                            - Source: ./diagrams/deployment.mmd
                            ```mermaid
                            flowchart LR
                              user([User]) --> cdn[CDN] --> fe[Frontend]
                              fe --> be[Backend] --> db[(DB)]
                            ```

                            ## Notes
                            - Keep IDs and names stable across revisions
                            - Show boundaries, external dependencies, and sensitive data flows clearly
                            - Align with tech stack and environments (dev/stage/prod)
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="integration_notes" caption="Integration Notes">
                    <name>Integration Notes</name>
                    <description>Document how these diagrams link into the rest of the docs.</description>
                    <hint>
                        Point to where to place links in README/spec/product docs and any badges or navigation updates.
                    </hint>
                    <stepwise-instructions>
                        <list>
                            <item>ACTION: Create integration.md with link targets and PR checklist</item>
                            <item>INCLUDE: references to relevant specs or product docs</item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/architecture/YYYY-MM-DD-diagram-name/integration.md" />
                        <template>
                            # Integration Notes

                            > Created: [CURRENT_DATE]

                            ## Link Targets
                            - README.md: Add a section "Architecture" linking to @.docs/architecture/YYYY-MM-DD-diagram-name/diagram-index.md
                            - Spec(s): Link from related spec(s) to the diagram index
                            - Product Docs: Cross-link from @.docs/product/tech-stack.md where appropriate

                            ## PR Checklist
                            - [ ] Add links in README/specs/product docs
                            - [ ] Ensure Mermaid blocks render properly in your platform
                            - [ ] Confirm audiences and scope still match the latest roadmap
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="user_review" caption="User Diagram Review">
                    <hint>
                        Ask the user to review the diagrams and plan before finalizing.
                    </hint>
                    <examples>
                        <example>
                            <ai-msg>
                                I've created the diagram set:

                                - Plan: @.docs/architecture/YYYY-MM-DD-diagram-name/diagram-plan.md
                                - Index: @.docs/architecture/YYYY-MM-DD-diagram-name/diagram-index.md
                                - Diagrams: @.docs/architecture/YYYY-MM-DD-diagram-name/diagrams/
                                - Integration Notes: @.docs/architecture/YYYY-MM-DD-diagram-name/integration.md

                                Please review and let me know if any changes are needed.
                            </ai-msg>
                        </example>
                    </examples>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                        </mcp-tooling>
                        <list>
                            <item>ACTION: Request user review and approval</item>
                            <item>WAIT: For approval or revision requests</item>
                            <item>REVISE: Update diagrams/plan/index as requested</item>
                            <item>UTILIZE: sequential-thinking to summarize changes</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>
        </list>
    </stepwise-instructions>

    <text>
    ## Execution Standards

    IMPORTANT: When executing this workflow, ensure you follow:
    - code-style
    - dev-best-practices
    - tech-stack

    IMPORTANT: Maintain:
    - Audience alignment (who will read/use the diagrams)
    - Visual consistency (names, IDs, boundaries)
    - Traceability to source-of-truth (code/spec/doc)
    - Use color to communicate
    - Maintain clear contrast for accessibility

    IMPORTANT: Ensure that all diagram sets include:
    - A browsable index (diagram-index.md)
    - Mermaid sources in ./diagrams/
    - Integration notes for docs

    IMPORTANT: Checklist:
    - [ ] Accurate date determined via file system
    - [ ] Diagram folder created with correct date prefix
    - [ ] 2-4 initial diagrams created in Mermaid
    - [ ] diagram-index.md created with embeds
    - [ ] User-approved documentation

    **IMPORTANT:** Preferred Diagram Types:
    - flowchart
    - sequenceDiagram
    - classDiagram
    - stateDiagram
    - erDiagram (Entity Relationship Diagram)
    - journey (User Journeys)
    - requirementDiagram
    - C4Context (planUML)
        - System Context
        - Container Diagram
        - Component Diagram
        - Dyanmic Diagram
        - Deployment Diagram
    - mindmap
    - zenuml
    - block
    - packet
    - archtecture-beta

    **IMPORTANT:** Banned Diagrams:
    </text>
</poml>
