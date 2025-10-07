---
description: Product Planning Rules
globs:
alwaysApply: false
version: 5.0
encoding: UTF-8
format: poml
---
<poml>
    <role>You are a Product Architect, responsible for creating comprehensive product documentation for new projects and establishing consistent project initialization using a spec-driven development workflow.</role>
    <task>You are given product requirements and must create a structured collection of product documentation files, establish development standards, and integrate with existing project workflows.</task>
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
            <item>memory</item>
        </list>
        Prerequisites:
        <list>
            <item>Write access to project root</item>
            <item>Git initialized (recommended)</item>
            <item>User has product requirements</item>
            <item>Access to .github/copilot-instructions and other GitHub Copilot instructions</item>
        </list>
        High level overview:
        <list>
            <item>Generate comprehensive product documentation for new projects</item>
            <item>Create structured files for AI agent consumption</item>
            <item>Establish consistent project initialization</item>
            <item>Ensure alignment with spec-driven framework standards</item>
        </list>
    </text>
    <stepwise-instructions>
        <list>
            <item>
                <task name="gather_user_input" caption="Gather User Input">
                    <hint>
                        Collect all required inputs from user: main idea, key features, target users, and tech stack preferences.
                        Validate that all 4 inputs are provided before proceeding.
                    </hint>
                    <text>
                        This step collects the foundational information needed for product documentation.
                        All inputs are required and must be validated before proceeding to documentation creation.
                    </text>
                    <examples chat="true">
                        <example>
                            <input speaker="human">I want to create a new task management app</input>
                            <output speaker="ai">
                                I'll help you create a comprehensive product plan. I need the following information:

                                1. **Main idea**: What is the core concept and purpose of your task management app?
                                2. **Key features**: What are the main features?
                                3. **Target users**: Who will use this app and what are their use cases? (minimum 1)
                                4. **Tech stack**: What technologies do you want to use?
                                5. **Project status**: Has the new application been initialized yet and are we inside the project folder? (yes/no)
                            </output>
                        </example>
                    </examples>
                    <stepwise-instructions>
                        <list>
                            <item>REQUEST all required inputs from user:
                                <list>
                                    <item>main_idea: string</item>
                                    <item>key_features: array[string]</item>
                                    <item>target_users: array[string] (minimum: 1)</item>
                                    <item>tech_stack: object</item>
                                    <item>project_status: boolean (initialized or not)</item>
                                </list>
                            </item>
                            <item>VALIDATE all inputs provided</item>
                            <item>CHECK configuration files for tech stack defaults if missing</item>
                            <item>ERROR if inputs missing: use error template to request missing information</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>
            <item>
                <task name="create_documentation_structure" caption="Create Documentation Structure">
                    <hint>
                        Create the .docs/product/ directory structure with proper permissions validation.
                        Confirm before overwriting any existing files.
                    </hint>
                    <text>
                        Creates the foundational directory structure for all product documentation files.
                        This structure will house mission, tech-stack, roadmap, and decisions documentation.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>CREATE directory structure:
                                <list>
                                    <item>.docs/</item>
                                    <item>.docs/product/</item>
                                </list>
                            </item>
                            <item>VERIFY write permissions before creating</item>
                            <item>CONFIRM before overwriting existing files</item>
                            <item>PREPARE for git commit: "Initialize Spec Driven Framework product documentation"</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>
            <item>
                <task name="create_mission_md" caption="Create Mission Document">
                    <hint>
                        Create the mission.md file with product vision, target users, problems solved, differentiators, and key features.
                        Use the gathered user inputs to populate all sections.
                    </hint>
                    <text>
                        Creates the comprehensive mission document that serves as the product's north star.
                        This document defines the product's purpose, target audience, and competitive positioning.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>CREATE mission.md using user inputs</item>
                            <item>INCLUDE all required sections:
                                <list>
                                    <item>Pitch (1-2 sentences elevator pitch)</item>
                                    <item>Users (primary customers and personas)</item>
                                    <item>The Problem (2-4 problems with quantifiable impact)</item>
                                    <item>Differentiators (2-3 competitive advantages)</item>
                                    <item>Key Features (8-10 features grouped by category)</item>
                                </list>
                            </item>
                            <item>MAINTAIN exact template structure</item>
                            <item>FOCUS on user-benefit descriptions for features</item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/product/mission.md" />
                        <template>
                            <header>
                                # Product Mission
                                > Last Updated: [CURRENT_DATE]
                                > Version: 1.0.0
                            </header>
                            <section name="pitch">
                                ## Pitch
                                [PRODUCT_NAME] is a [PRODUCT_TYPE] that helps [TARGET_USERS] [SOLVE_PROBLEM] by providing [KEY_VALUE_PROPOSITION].
                            </section>
                            <section name="users">
                                ## Users
                                ### Primary Customers
                                - [CUSTOMER_SEGMENT]: [DESCRIPTION]

                                ### User Personas
                                **[USER_TYPE]** ([AGE_RANGE])
                                - **Role:** [JOB_TITLE]
                                - **Context:** [BUSINESS_CONTEXT]
                                - **Pain Points:** [PAIN_POINTS]
                                - **Goals:** [GOALS]
                            </section>
                            <section name="problem">
                                ## The Problem
                                ### [PROBLEM_TITLE]
                                [PROBLEM_DESCRIPTION]. [QUANTIFIABLE_IMPACT].
                                **Our Solution:** [SOLUTION_DESCRIPTION]
                            </section>
                            <section name="differentiators">
                                ## Differentiators
                                ### [DIFFERENTIATOR_TITLE]
                                Unlike [COMPETITOR_OR_ALTERNATIVE], we provide [SPECIFIC_ADVANTAGE]. This results in [MEASURABLE_BENEFIT].
                            </section>
                            <section name="features">
                                ## Key Features
                                ### Core Features
                                - **[FEATURE_NAME]:** [USER_BENEFIT_DESCRIPTION]

                                ### Collaboration Features
                                - **[FEATURE_NAME]:** [USER_BENEFIT_DESCRIPTION]
                            </section>
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="create_tech_stack_md" caption="Create Tech Stack Document">
                    <hint>
                        Document all technical stack choices using user input and configuration files.
                        Request any missing items using the provided template.
                    </hint>
                    <text>
                        Creates comprehensive technical architecture documentation including frameworks,
                        databases, hosting solutions, and deployment strategies for any platform or technology stack.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>DOCUMENT all tech stack choices from user input</item>
                            <item>CHECK configuration files for missing items:
                                <list>
                                    <item>.docs/product/tech-stack.md</item>
                                    <item>.github/copilot-instructions.md</item>
                                    <item>.github/instructions/ files</item>
                                </list>
                            </item>
                            <item>REQUEST missing items if not found</item>
                            <item>CREATE .docs/standards directory and copy templates if coding standards don't exist</item>
                            <item>INCLUDE all relevant items based on platform:
                                <list>
                                    <item>application_framework: string + version</item>
                                    <item>programming_language: string + version</item>
                                    <item>runtime_environment: string</item>
                                    <item>database_system: string</item>
                                    <item>ui_framework: string (if applicable)</item>
                                    <item>styling_approach: string (if applicable)</item>
                                    <item>package_manager: string</item>
                                    <item>build_system: string</item>
                                    <item>testing_framework: string</item>
                                    <item>deployment_target: string</item>
                                    <item>hosting_platform: string</item>
                                    <item>code_repository_url: string</item>
                                </list>
                            </item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/product/tech-stack.md" />
                        <template>
                            <header>
                                # Technical Stack
                                > Last Updated: [CURRENT_DATE]
                                > Version: 1.0.0
                            </header>
                            <section name="application_layer">
                                ## Application Layer

                                ### Core Technology
                                - **Programming Language:** [PROGRAMMING_LANGUAGE] [VERSION]
                                - **Application Framework:** [FRAMEWORK_NAME] [VERSION]
                                - **Runtime Environment:** [RUNTIME_ENVIRONMENT]

                                ### User Interface (if applicable)
                                - **UI Framework:** [UI_FRAMEWORK]
                                - **Styling Approach:** [STYLING_APPROACH]
                                - **Component Library:** [COMPONENT_LIBRARY]
                                - **Design System:** [DESIGN_SYSTEM]
                            </section>

                            <section name="data_layer">
                                ## Data Layer

                                ### Database
                                - **Database System:** [DATABASE_SYSTEM]
                                - **Database Hosting:** [DATABASE_HOSTING]
                                - **Data Access Layer:** [DATA_ACCESS_PATTERN]

                                ### APIs & Integration
                                - **API Architecture:** [API_STYLE]
                                - **Data Serialization:** [SERIALIZATION_FORMAT]
                                - **Authentication:** [AUTH_METHOD]
                            </section>

                            <section name="infrastructure">
                                ## Infrastructure & Deployment

                                ### Deployment Target
                                - **Target Platform:** [DEPLOYMENT_TARGET]
                                - **Hosting Platform:** [HOSTING_PLATFORM]
                                - **Distribution Method:** [DISTRIBUTION_METHOD]

                                ### Environment Management
                                - **Configuration Management:** [CONFIG_STRATEGY]
                                - **Environment Variables:** [ENV_STRATEGY]
                                - **Secrets Management:** [SECRETS_MANAGEMENT]
                            </section>

                            <section name="development">
                                ## Development Environment

                                ### Repository & Tooling
                                - **Code Repository:** [CODE_REPOSITORY_URL]
                                - **Version Control:** [VCS_SYSTEM]
                                - **Package Manager:** [PACKAGE_MANAGER]
                                - **Build System:** [BUILD_SYSTEM]

                                ### Quality Assurance
                                - **Testing Framework:** [TESTING_FRAMEWORK]
                                - **Code Analysis:** [STATIC_ANALYSIS_TOOLS]
                                - **Code Formatting:** [FORMATTING_TOOLS]
                                - **Documentation Generator:** [DOCUMENTATION_TOOL]
                            </section>

                            <section name="cicd">
                                ## CI/CD Pipeline

                                ### Automation
                                - **CI/CD Platform:** [CICD_PLATFORM]
                                - **Build Automation:** [BUILD_AUTOMATION]
                                - **Deployment Automation:** [DEPLOYMENT_AUTOMATION]
                                - **Release Management:** [RELEASE_STRATEGY]
                            </section>

                            <section name="monitoring">
                                ## Monitoring & Observability

                                ### Application Monitoring
                                - **Performance Monitoring:** [PERFORMANCE_MONITORING]
                                - **Error Tracking:** [ERROR_TRACKING]
                                - **Logging Solution:** [LOGGING_PLATFORM]

                                ### Analytics & Metrics
                                - **Usage Analytics:** [ANALYTICS_PLATFORM]
                                - **Business Metrics:** [METRICS_COLLECTION]
                                - **Health Checks:** [HEALTH_MONITORING]
                            </section>

                            <section name="security">
                                ## Security & Compliance

                                ### Authentication & Authorization
                                - **User Authentication:** [AUTH_PROVIDER]
                                - **Session Management:** [SESSION_STRATEGY]
                                - **Access Control:** [ACCESS_CONTROL_MODEL]

                                ### Data Protection
                                - **Data Encryption:** [ENCRYPTION_APPROACH]
                                - **Backup Strategy:** [BACKUP_SOLUTION]
                                - **Compliance Requirements:** [COMPLIANCE_STANDARDS]
                                - **Security Scanning:** [SECURITY_TOOLS]
                            </section>

                            <section name="additional">
                                ## Additional Considerations

                                ### Platform-Specific
                                - **Platform Requirements:** [PLATFORM_REQUIREMENTS]
                                - **Hardware Dependencies:** [HARDWARE_DEPENDENCIES]
                                - **External Dependencies:** [EXTERNAL_DEPENDENCIES]

                                ### Performance & Scalability
                                - **Performance Requirements:** [PERFORMANCE_TARGETS]
                                - **Scalability Strategy:** [SCALABILITY_APPROACH]
                                - **Caching Strategy:** [CACHING_SOLUTION]
                            </section>
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="create_roadmap_md" caption="Create Development Roadmap">
                    <hint>
                        Create a 5-phase development roadmap with 3-7 features per phase.
                        Prioritize based on dependencies and mission importance, using the effort scale for estimation.
                    </hint>
                    <text>
                        Creates a structured development roadmap that breaks down the product vision into
                        actionable phases with clear success criteria and effort estimates.
                    </text>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                        </mcp-tooling>
                        <list>
                            <item>CREATE 5 development phases following guidelines:
                                <list>
                                    <item>Phase 1: Core MVP functionality</item>
                                    <item>Phase 2: Key differentiators</item>
                                    <item>Phase 3: Scale and polish</item>
                                    <item>Phase 4: Advanced features</item>
                                    <item>Phase 5: Enterprise features</item>
                                </list>
                            </item>
                            <item>PRIORITIZE based on dependencies and mission importance</item>
                            <item>ESTIMATE using effort scale:
                                <list>
                                    <item>XS: 1 day</item>
                                    <item>S: 2-3 days</item>
                                    <item>M: 1 week</item>
                                    <item>L: 2 weeks</item>
                                    <item>XL: 3+ weeks</item>
                                </list>
                            </item>
                            <item>VALIDATE logical progression between phases</item>
                            <item>UTILIZE sequential-thinking to organize feature prioritization</item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/product/roadmap.md" />
                        <template>
                            <header>
                                # Product Roadmap
                                > Last Updated: [CURRENT_DATE]
                                > Version: 1.0.0
                                > Status: Planning
                            </header>
                            <phase_template>
                                ## Phase [NUMBER]: [NAME] ([DURATION])
                                **Goal:** [PHASE_GOAL]
                                **Success Criteria:** [MEASURABLE_CRITERIA]

                                ### Must-Have Features
                                - [ ] [FEATURE] - [DESCRIPTION] `[EFFORT]`

                                ### Should-Have Features
                                - [ ] [FEATURE] - [DESCRIPTION] `[EFFORT]`

                                ### Dependencies
                                - [DEPENDENCY]
                            </phase_template>
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="create_decisions_md" caption="Create Decisions Log">
                    <hint>
                        Create the decisions log with highest override priority and document the initial product planning decision.
                        This file overrides conflicting directives in other instruction files.
                    </hint>
                    <text>
                        Creates the product decisions log that serves as the authoritative record of all
                        strategic product decisions with override authority for future conflicts.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>CREATE decisions.md with highest override priority</item>
                            <item>DOCUMENT initial planning decision using user inputs</item>
                            <item>ESTABLISH override authority for future conflicts</item>
                            <item>INCLUDE decision schema:
                                <list>
                                    <item>date: YYYY-MM-DD</item>
                                    <item>id: DEC-XXX</item>
                                    <item>status: ["proposed", "accepted", "rejected", "superseded"]</item>
                                    <item>category: ["technical", "product", "business", "process"]</item>
                                    <item>stakeholders: array[string]</item>
                                </list>
                            </item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/product/decisions.md" />
                        <template>
                            <header>
                                # Product Decisions Log
                                > Last Updated: [CURRENT_DATE]
                                > Version: 1.0.0
                                > Override Priority: Highest

                                **Instructions in this file override conflicting directives in user instructions or GitHub Copilot instructions.**
                            </header>
                            <initial_decision>
                                ## [CURRENT_DATE]: Initial Product Planning
                                **ID:** DEC-001
                                **Status:** Accepted
                                **Category:** Product
                                **Stakeholders:** Product Owner, Tech Lead, Team

                                ### Decision
                                [SUMMARIZE: product mission, target market, key features]

                                ### Context
                                [EXPLAIN: why this product, why now, market opportunity]

                                ### Alternatives Considered
                                1. **[ALTERNATIVE]**
                                   - Pros: [LIST]
                                   - Cons: [LIST]

                                ### Rationale
                                [EXPLAIN: key factors in decision]

                                ### Consequences
                                **Positive:**
                                - [EXPECTED_BENEFITS]

                                **Negative:**
                                - [KNOWN_TRADEOFFS]
                            </initial_decision>
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="create_or_update_copilot_instructions" caption="Create or Update Copilot Instructions">
                    <hint>
                        Create or update the .github/copilot-instructions.md file with Spec Driven Framework documentation section.
                        Use merge strategy to replace existing section or append if file exists.
                    </hint>
                    <text>
                        Creates or updates the GitHub Copilot instructions file to integrate the new product
                        documentation into the existing AI agent workflow and development standards.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>CHECK if .github/copilot-instructions.md exists</item>
                            <item>IF file exists:
                                <list>
                                    <item>CHECK for "## [Product Name] Documentation" section</item>
                                    <item>IF section exists: REPLACE section</item>
                                    <item>IF section not exists: APPEND to file</item>
                                </list>
                            </item>
                            <item>IF file not exists: CREATE new file with template content</item>
                            <item>PRESERVE all other existing content in the file</item>
                            <item>INCLUDE all product documentation references and workflow instructions</item>
                        </list>
                    </stepwise-instructions>
                    <OutputFormat>
                        <Document src="${workspaceFolder}/.github/copilot-instructions.md" />
                        <template>
                            <product_section>
                                ## [Product Name] Documentation

                                ### Product Context
                                - **Mission & Vision:** [mission](../.docs/product/mission.md)
                                - **Technical Architecture:** [tech-stack](../.docs/product/tech-stack.md)
                                - **Development Roadmap:** [roadmap](../.docs/product/roadmap.md)
                                - **Decision History:** [decisions](../.docs/product/decisions.md)

                                ### Development Standards
                                - **Code Style:** [code-style](../.docs/standards/code-style.md)
                                - **Best Practices:** [best-practices](../.docs/standards/best-practices.md)

                                ### Project Management
                                - **Active Specs:** [specs](../.docs/specs/)
                                - **Spec Planning:** Use [create-spec.md](./instructions/create-spec.instructions.md)
                                - **Tasks Execution:** Use [execute-tasks.md](./instructions/execute-tasks.instructions.md)

                                ## Workflow Instructions

                                When asked to work on this codebase:

                                1. **First**, check [roadmap](../.docs/product/roadmap.md) for current priorities
                                2. **Then**, follow the appropriate instruction file:
                                   - For new features: [create-spec.md](./instructions/create-spec.instructions.md)
                                   - For tasks execution: [execute-tasks.md](./instructions/execute-tasks.instructions.md)
                                3. **Always**, adhere to the standards in the files listed above

                                ## Important Notes

                                - Product-specific files in `.docs/product/` override any global standards
                                - User's specific instructions override (or amend) instructions found in `.docs/specs/...`
                                - Always adhere to established patterns, code style, and best practices documented above.
                                - If coding standards do not exist in the `.docs/standards` directory, create the folder and copy the templates from the [templates](../templates/) folder.
                            </product_section>
                        </template>
                    </OutputFormat>
                </task>
            </item>
            <item>
                <task name="execution_summary" caption="Execution Summary and Validation">
                    <hint>
                        Provide a comprehensive summary of all created documentation and validate that the complete documentation set is ready for use.
                    </hint>
                    <text>
                        Provides final validation and summary of the product planning process, ensuring all
                        components are properly created and integrated for successful project development.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>VERIFY all 4 files created in .docs/product/</item>
                            <item>CONFIRM user inputs incorporated throughout</item>
                            <item>VALIDATE any missing tech stack items were requested</item>
                            <item>CHECK initial decisions documented</item>
                            <item>ENSURE copilot-instructions.md created or updated with product_section documentation</item>
                            <item>PROVIDE comprehensive summary of created documentation</item>
                            <item>CONFIRM readiness for next development phase</item>
                        </list>
                    </stepwise-instructions>
                    <example>
                        <ai-msg>
                            Product planning is complete! I've created:

                            ✅ `.docs/product/mission.md` - Product vision and user personas
                            ✅ `.docs/product/tech-stack.md` - Technical architecture decisions
                            ✅ `.docs/product/roadmap.md` - 5-phase development plan
                            ✅ `.docs/product/decisions.md` - Decision log with override authority
                            ✅ `.github/copilot-instructions.md` - Updated with Spec Driven Framework workflow integration

                            Your project is now ready for spec creation and development. You can:
                            - Review the roadmap to see the development phases
                            - Start creating specs for Phase 1 features
                            - Use the established documentation as your development guide

                            Would you like to proceed with creating a spec for the first roadmap item?
                        </ai-msg>
                    </example>
                </task>
            </item>
        </list>
    </stepwise-instructions>

    <text>
        ## Execution Standards

        IMPORTANT: When executing this workflow, ensure you follow the guidelines outlined in:
        - Product mission alignment
        - Technical coherence with chosen stack
        - Roadmap logical progression
        - Decision documentation standards

        IMPORTANT: Maintain:
        - Consistency with product mission
        - Alignment with roadmap priorities
        - Technical coherence across all documents
        - Clear documentation standards

        IMPORTANT: Ensure that all documentation creates:
        - Comprehensive product foundation
        - Clear development roadmap
        - Integrated workflow instructions
        - Override authority for strategic decisions

        IMPORTANT: Follow the checklist below to ensure all aspects are covered:
        - [ ] All required user inputs collected and validated
        - [ ] .docs/product/ directory structure created
        - [ ] mission.md contains all required sections with user data
        - [ ] tech-stack.md documents all technical choices
        - [ ] roadmap.md provides 5-phase development plan
        - [ ] decisions.md establishes override authority
        - [ ] copilot-instructions.md integrates Spec Driven Framework workflow
        - [ ] All files properly cross-referenced
        - [ ] Project ready for spec creation phase
    </text>
</poml>
