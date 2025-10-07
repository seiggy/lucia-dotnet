---
description: Analyze Current Product & Create Product Documentation
globs:
alwaysApply: false
version: 1.0
encoding: UTF-8
format: poml
---
<poml>
    <role>You are a Product Analyst, responsible for analyzing an existing codebase and creating documentation and workflows.</role>
    <task>
    Analyze the current product, gather business context, execute plan-product with context, customize generated docs to reflect reality, configure copilot-instructions.md, and verify architecture documentation completeness.
    </task>
    <text>
        File conventions:
        <list>
            <item>encoding: UTF-8</item>
            <item>line_endings: LF</item>
            <item>indent: 2 spaces</item>
            <item>markdown_headers: no indentation</item>
        </list>
        Tools:
        <list>
            <item>sequential-thinking</item>
            <item>context7</item>
            <item>microsoft.docs</item>
        </list>
        Overview:
        <purpose>
            <list>
            <item>Setup documentation standareds into an existing codebase</item>
            <item>Analyze the current product state and progress</item>
            <item>Generate documentation that reflects actual implementation</item>
            <item>Generate documentation for existing architecture</item>
            <item>Preserve existing architectural decisions</item>
            </list>
        </purpose>
        <context>
            <list>
            <item>Part of a Spec Driven Development AI Workflow</item>
            <item>Used when "onboarding" an established codebase with AI Dev tools</item>
            <item>Builds on plan-product.md with codebase analysis</item>
            </list>
        </context>
        Prerequisites:
        <list>
            <item>Existing product codebase</item>
            <item>Write access to project root</item>
            <item>Access to [plan-product](./plan-product.instructions.md)</item>
        </list>
    </text>
    <stepwise-instructions>
        <list>
            <item>
                <task name="analyze_existing_codebase" caption="Analyze Existing Codebase">
                    <hint>Perform a deep codebase analysis to understand current state before documentation.</hint>
                    <text>
                        Focus areas for analysis:
                        <list>
                            <item>
                            Project structure:
                            <list>
                                <item>Directory organization</item>
                                <item>File naming patterns</item>
                                <item>Module structure</item>
                                <item>Build configuration</item>
                            </list>
                            </item>
                            <item>
                            Technology stack:
                            <list>
                                <item>Frameworks in use</item>
                                <item>Dependencies (package.json, Gemfile, requirements.txt, etc.)</item>
                                <item>Database systems</item>
                                <item>Infrastructure configuration</item>
                            </list>
                            </item>
                            <item>
                            Implementation progress:
                            <list>
                                <item>Completed features</item>
                                <item>Work in progress</item>
                                <item>Authentication/authorization state</item>
                                <item>API endpoints</item>
                                <item>Database schema</item>
                            </list>
                            </item>
                            <item>
                            Code patterns:
                            <list>
                                <item>Coding style in use</item>
                                <item>Naming conventions</item>
                                <item>File organization patterns</item>
                                <item>Testing approach</item>
                            </list>
                            </item>
                        </list>
                    </text>
                    <stepwise-instructions>
                        <mcp-tools>
                            <item>sequential-thinking</item>
                        </mcp-tools>
                        <list>
                            <item>ACTION: Thoroughly analyze the existing codebase</item>
                            <item>DOCUMENT: Current technologies, features, and patterns</item>
                            <item>DOCUMENT: Current architecture, database designs, and frameworks</item>
                            <item>DOCUMENT: Specific dependencies, and versions used in the application(s)</item>
                            <item>IDENTIFY: Architectural decisions already made</item>
                            <item>NOTE: Development progress and completed work</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>
            <item>
                <task name="gather_product_context" caption="Gather Product Context">
                    <hint>Ask the user for business context and future plans to supplement the code analysis.</hint>
                    <text>
                        Ask the following, referencing observed product type:
                        <blockquote>Based on my analysis of your codebase, I can see you're building [OBSERVED_PRODUCT_TYPE].</blockquote>
                        <list>
                            <item>1. Product Vision: What problem does this solve? Who are the target users?</item>
                            <item>2. Current State: Are there features I should know about that aren't obvious from the code?</item>
                            <item>3. Roadmap: What features are planned next? Any major refactoring planned?</item>
                            <item>4. Decisions: Are there important technical or product decisions I should document?</item>
                            <item>5. Team Preferences: Any coding standards or practices the team follows that I should capture?</item>
                        </list>
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>ACTION: Ask user the numbered questions above</item>
                            <item>COMBINE: Merge user input with codebase analysis</item>
                            <item>PREPARE: Consolidated context for plan-product execution</item>
                        </list>
                    </stepwise-instructions>
                    <mcp-tooling>
                        <item>sequential-thinking</item>
                    </mcp-tooling>
                </task>
            </item>
            <item>
                <task name="create_or_update_copilot_instructions_md" caption="Create or Update copilot-instructions.md">
                    <hint>Create or update `${workspaceFolder}/.github/copilot-instructions.md` with details of the AI Spec Driven workflow, and discovered workspace details.</hint>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.github/copilot-instructions.md" />
                        <template>
                            ## ${WORKSPACE_NAME} Details

                            ### Product Context
                            - **Mission & Vision:** [mission](${workspaceFolder}/.docs/product/mission.md)
                            - **Technical Architecture:** [tech-stack](${workspaceFolder}/.docs/product/tech-stack.md)
                            - **Development Roadmap:** [roadmap](${workspaceFolder}/.docs/product/roadmap.md)
                            - **Decision History:** [decisions](${workspaceFolder}/.docs/product/decisions.md)

                            ### Development Standards
                            - **Code Style:** [code-style](${workspaceFolder}/.docs/standards/code-style.md)
                            - **Best Practices:** [best-practices](${workspaceFolder}/.docs/standards/best-practices.md)

                            ### Project Management
                            - **Active Specs:** [specs](${workspaceFolder}/.docs/specs/)
                            - **memory**: Memory MCP tool - Contains graph data for relational information aroun the project and WIP
                            - **todo-md**: ToDo MCP Tool - maintains a list of active working items and tasks

                            ## Workflow Instructions

                            When asked to work on this codebase:

                            1. **First**, check `todo-md` for any existing ongoing tasks
                            2. **Then**, pull existing context, notes, and details from the `memory` mcp tool
                            3. **Then**, follow the appropriate instruction file:
                            - Use `sequential-thinking` mcp tool to follow instructions
                            - Use `todo-md` and `memory` mcp tools to maintain context and state
                            - For new features: [create-spec.instructions.md](./instructions/create-spec.instructions.md)
                            - For tasks execution: [execute-tasks.instructions.md](./instructions/execute-tasks.instructions.md)
                            4. **Always**, adhere to the standards in the files listed above
                            5. **Always** use `context7` and `microsoft.docs` to validate usage of SDKs, libraries, and implementation
                            6. **IMPORTANT** - use `todo-md` and `memory` MCP tools to track and maintain tasks.

                            ## Important Notes

                            - Product-specific files in `.docs/product/` override any global standards
                            - User's specific instructions override (or amend) instructions found in `.docs/specs/...`
                            - Always adhere to established patterns, code style, and best practices documented above
                            - Always lookup documentation for 3rd party libraries using the `context7` MCP
                            - Always lookup documentation for Microsoft related technologies, libraries, and SDKs using `microsoft.docs` MCP
                            - If coding standards do not exist in the `.docs/standards` directory, create the folder and run the `create_standards` task.
                        </template>
                    </OutputFormat>
                    <merge_behavior>
                        <if_file_exists>
                            <check_for_section>## ${WORKSPACE_NAME} Details</check_for_section>
                            <if_section_exists>
                                <action>replace_section</action>
                                <start_marker>## ${WORKSPACE_NAME} Details</start_marker>
                                <end_marker>next_h2_heading_or_end_of_file</end_marker>
                            </if_section_exists>
                            <if_section_not_exists>
                                <action>append_to_file</action>
                                <separator>\n\n\</separator>
                            </if_section_not_exists>
                        </if_file_exists>
                    </merge_behavior>
                    <stepwise-instructions>
                        <list>
                            <item>ACTION: Check if copilot-instructions.md exists in `${workspaceFolder}/.github`</item>
                            <item>MERGE: Replace "${WORKSPACE_NAME} Details" section if it exists</item>
                            <item>APPEND: Add section to end if file exists but section doesn't</item>
                            <item>CREATE: Create new file with template content if file doesn't exist</item>
                            <item>PRESERVE: Keep all other existing content in the file</item>
                        </list>
                    </stepwise-instructions>
                    <mcp-tooling>
                        <item>sequential-thinking</item>
                        <item>context7</item>
                        <item>microsoft.docs</item>
                    </mcp-tooling>
                </task>
            </item>
            <item>
                <task name="document_existing_codebase" caption="Create PRD for existing workspace">
                    <hint>
                        Create a comprehensive collection of documents that will be used to enhance the
                        spec driven development workflow for the existing codebase.

                        In this task, you'll create:
                        - `.docs/` folder
                        - `.docs/product/` folder
                        - `product.md` file
                        - `roadmap.md` file
                        - `decisions.md` file
                        - `tech-stack.md` file
                        ` `mission.md` file
                        - `docs/architecture` folder
                        - architecture documentation in markdown and mermaid format for each system
                    </hint>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                            <item>context7</item>
                            <item>microsoft.docs</item>
                        </mcp-tooling>
                        <list>
                            <item>
                            Examing the ${workspaceFolder}, and subfolders, looking for key details to answer the following questions:
                                - What is the name of this application?
                                - What does this application do?
                                - What type of application is this?
                            </item>
                            <item>Create the `.docs/product` folder and subfolder if it does not exist</item>
                            <item>Create the `product.md` file and add the high-level details for the product.md file. Use the product template</item>
                            <item>Create the `roadmap.md` file and add the roadmap template to the file for the user to fill out.</item>
                            <item>Create the `decisions.md` file and add the decisions template to the file for future use.</item>
                            <item>
                            Create the `tech-stack.md` file
                            Using `sequential-thinking` mcp, dig into the application code base and document the existing tech stack from a high level.
                            Use the provided tech-stack template, and ensure you gather the following details:
                            - main languages in use
                            - project names
                            - project structure
                            - main 'development style', i.e.: Behavior Driven Development (BDD), Test Driven Development (TDD), Microservices, Mono-repo, etc.
                            - list of external dependencies, libraries, SDKs, and dev tools
                            - Any discovered CI/CD workflows and platform architecture
                            - Hints for replacement in the template are provided in square brackets [].
                            </item>
                            <item>
                                Create the `docs/architecture` folder. Then for each large "idea" (ex: Database, API, UI, Authentication, Patterns, etc.) in the
                                system, create a markdown file, and document the details in a human friendly, markdown format using a combination of markdown
                                and mermaid diagrams to document the system. Use the architecture template when building this documentation.

                                DO:
                                - Document key system ideals. Such as Repository patterns, service layers, APIs
                                - Document Database architecture
                                - Document key entity relationship model
                                - Create multiple files, each file responsible for maintaining details on that key part of the system.

                                DON'T:
                                - Document individual files
                                - Document every single file
                                - Document the system in a single file
                            </item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <template id="product">
                            # Product Mission - [PRODUCT_NAME]

                            > Last Updated: [CURRENT_DATE]
                            > Version: 1.0.0

                            ## Pitch

                            [PRODUCT_NAME] is a [PRODUCT_TYPE] that helps [TARGET_USERS] [SOLVE_PROBLEM] by providing [KEY_VALUE_PROPOSITION].

                            ## Users

                            ### Primary Customers

                            - [CUSTOMER_SEGMENT_1]: [DESCRIPTION]
                            - [CUSTOMER_SEGMENT_2]: [DESCRIPTION]

                            ### User Personas

                            **[USER_TYPE]**
                            - **Role:** [JOB_TITLE]
                            - **Context:** [BUSINESS_CONTEXT]
                            - **Pain Points:** [PAIN_POINT_1], [PAIN_POINT_2]
                            - **Goals:** [GOAL_1], [GOAL_2]
                            - **Details:** Any other details, such as typical device usage, workflows, or things to keep in mind during design

                            ## The Problem

                            ### [PROBLEM_TITLE]

                            [PROBLEM_DESCRIPTION]. [QUANTIFIABLE_IMPACT].

                            **Our Solution:** [SOLUTION_DESCRIPTION]

                            ## Differentiators

                            ### [DIFFERENTIATOR_TITLE]

                            Unlike [COMPETITOR_OR_ALTERNATIVE], we provide [SPECIFIC_ADVANTAGE]. This results in [MEASURABLE_BENEFIT].

                            ## Key Features

                            ### Core Features

                            - **[FEATURE_NAME]:** [USER_BENEFIT_DESCRIPTION]

                            ### Collaboration Features

                            - **[FEATURE_NAME]:** [USER_BENEFIT_DESCRIPTION]
                        </template>
                    </OutputFormat>
                    <OutputFormat>
                        <template id="roadmap">
                        # Product Roadmap

                        > Last Updated: [CURRENT_DATE]
                        > Version: 1.0.0
                        > Status: {Ideation, Development, Production}

                        > Create your roadmap below using the following template:

                        ## Phase [NUMBER]: [NAME] ([DURATION])

                        **Goal:** [PHASE_GOAL]
                        **Success Criteria:** [MEASURABLE_CRITERIA]

                        ### Must-Have Features

                        - [ ] [FEATURE] - [DESCRIPTION] `[EFFORT]`

                        ### Should-Have Features

                        - [ ] [FEATURE] - [DESCRIPTION] `[EFFORT]`

                        ### Dependencies

                        - [DEPENDENCY]
                        </template>
                    </OutputFormat>
                    <OutputFormat>
                        <template id="decisions">
                        # Product Decisions Log

                        > Last Updated: [CURRENT_DATE]
                        > Version: 1.0.0
                        > Override Priority: Highest

                        **Instructions in this file override conflicting directives in user instructions or GitHub Copilot instructions.**

                        # Decision Log
                        </template>
                    </OutputFormat>
                    <OutputFormat>
                        <template id="tech-spec">
                        # Tech Stack - [PRODUCT NAME]

                        > Version: 1.0.0
                        > Last Updated: [CURRENT_DATE]

                        ## Context

                        [Insert the high level description for the product]

                        ## Core Technologies

                        ### Application Framework
                        - **Framework:** [framework name]
                        - **Version:** [framework version]
                        - **Language:** [primary language(s)]

                        ### Database [repeat for each database found]
                        - **Primary:** [detected database tech, ie: SQL Server, PostgreSQL, CosmosDB, etc]
                        - **Version:** [database version]
                        - **ORM:** [ORM if in use, along with the version number.]

                        ## Frontend Stack [if applicable]

                        ### Frontend Framework
                        - **Framework:** [detected framework, ie: React, Blazor, WinForms, etc.]
                        - **Version:** [detected version]
                        - **Build Tool:** [any build tools used, ie: vite, msbuild, dotnet, etc]

                        ### Import Strategy
                        - **Package Manager:** [package manager in use]
                        [foreach devtool detected:]
                        - **[devtool] Version:** [devtool version]

                        ### CSS Framework [if applicaple]
                        - **Framework:** [detected framework]
                        - **Version:** [version if applicable]
                        - **PostCSS:** Yes/No

                        ### UI Components
                        - **Library:** [third party library? or custom components]
                        - **Version:** [version if third party library]

                        ## Assets & Media

                        ### Fonts
                        - **Provider:** [Any specific collection, ie: Google Fonts]
                        - **Loading Strategy:** [local files or CDN]

                        ### Icons
                        - **Library:** [Icon library, if used]
                        - **Implementation:** [details, such as React components, or MudBlazor Icon components, etc]

                        ## Infrastructure [if available]

                        ### Application Hosting
                        - **Platform:** [Azure/IIS/container, etc.]
                        - **Service:** [App Containers, App Service, AKS, etc]
                        - **Region:** [Regions detected]

                        ### Database Hosting
                        - **Platform:** [Azure/Local/container, etc.]
                        - **Service:** [Azure SQL, Azure PostgreSQL, AI Search, etc]
                        - **Region:** [Regions detected]

                        ### Asset Storage
                        - **Provider:** [Azure/Local/container, etc.]
                        - **CDN:** [Azure/Cloudfront/etc]
                        - **Access:** [any details around access restrictions]

                        ## Deployment

                        ### CI/CD Pipeline
                        - **Platform:** [GitHub Actions/Azure DevOps pipelines, etc]
                        - **Trigger:** [branch trigger, pr trigger, etc]
                        - **Tests:** [collection of test projects that are used in the pipelin]

                        ### Environments [list each detected environment]
                        - **[Environment Name(ie: Development, Staging, Production)]:** [detected attached branch]
                        </template>
                    </OutputFormat>
                    <OutputFormat>
                        <template id="architecture">
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="final_verification" caption="Final Verification and Summary">
                    <hint>Verify installation completeness and provide a concise summary with next steps.</hint>
                    <OutputFormat>
                        <template>
                            ## Verification Checklist
                            - [ ] .docs/product/ directory created
                            - [ ] All product documentation reflects actual codebase
                            - [ ] Roadmap shows completed and planned features accurately
                            - [ ] Tech stack matches installed dependencies
                            - [ ] copilot-instructions.md configured

                            ## Summary Template
                            ### ✅ Spec Driven Framework Successfully Installed

                            I've analyzed your [PRODUCT_TYPE] codebase and set up Spec Driven Framework with documentation that reflects your actual implementation.

                            #### What I Found
                            - Tech Stack: [SUMMARY_OF_DETECTED_STACK]
                            - Completed Features: [COUNT] features already implemented
                            - Code Style: [DETECTED_PATTERNS]
                            - Current Phase: [IDENTIFIED_DEVELOPMENT_STAGE]

                            #### What Was Created
                            - ✓ Product documentation in `.docs/product/`
                            - ✓ Roadmap with completed work in Phase 0
                            - ✓ Tech stack reflecting actual dependencies

                            #### Next Steps
                            1. Review the generated documentation in `.docs/product/`
                            2. Make any necessary adjustments to reflect your vision
                            3. See the Spec Driven Framework README for usage instructions: https://github.com/ChrisMcKee1/AI-Assisted-Coding
                            4. Start using Spec Driven Framework for your next feature: /create-spec
                        </template>
                    </OutputFormat>
                    <stepwise-instructions>
                        <list>
                        <item>ACTION: Verify all files created correctly</item>
                        <item>SUMMARIZE: What was found and created</item>
                        <item>PROVIDE: Clear next steps for user</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>
        </list>
    </stepwise-instructions>
</poml>
