---
description: Generate Status Report for Spec-Driven Development
globs:
alwaysApply: false
version: 2.0
encoding: UTF-8
format: poml
---

<poml>
    <role>You are a Project Analyst and Technical Lead, responsible for generating comprehensive project status reports for software development projects.</role>
    <task>You are tasked with analyzing all specification documents, product documentation, and project artifacts to create a detailed status report that provides visibility into project health, progress, and recommendations.</task>

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
            <item>memory</item>
            <item>microsoft-docs</item>
        </list>

        Prerequisites:
        <list>
            <item>Product documentation exists in .docs/product/</item>
            <item>One or more spec folders exist in .docs/specs/</item>
            <item>
                Access to:
                <list>
                    <item>[mission](../../.docs/product/mission.md)</item>
                    <item>[roadmap](../../.docs/product/roadmap.md)</item>
                    <item>[tech-stack](../../.docs/product/tech-stack.md)</item>
                    <item>[decisions](../../.docs/product/decisions.md)</item>
                </list>
            </item>
        </list>

        High level overview:
        <list listStyle="dash">
            <item>Generate comprehensive project status reports</item>
            <item>Analyze all specification documents in .docs/specs/</item>
            <item>Provide architectural overview of system progress</item>
            <item>Track completion status across all features</item>
            <item>Identify blockers, risks, and recommendations</item>
        </list>
    </text>

    <stepwise-instructions>
        <list>
            <item>
                <task name="discover_documentation" caption="Discover Documentation Structure">
                    <hint>
                        Scan the project structure to catalog all available documentation files and identify missing components.
                    </hint>
                    <text>
                        Systematically discover and catalog all documentation in the .docs/ directory, including product documents and specification folders.

                        Expected structure:
                        .docs/
                        ├── product/
                        │   ├── mission.md
                        │   ├── roadmap.md
                        │   ├── tech-stack.md
                        │   ├── decisions.md
                        │   └── code-style.md
                        └── specs/
                            ├── YYYY-MM-DD-spec-name-1/
                            │   ├── spec.md
                            │   ├── tasks.md
                            │   └── sub-specs/
                            │       ├── technical-spec.md
                            │       ├── api-spec.md
                            │       ├── database-schema.md
                            │       └── tests.md
                            └── YYYY-MM-DD-spec-name-2/
                                └── ...
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>SCAN .docs/ directory structure recursively</item>
                            <item>CATALOG all product documents in .docs/product/</item>
                            <item>IDENTIFY all spec folders in .docs/specs/ (pattern: YYYY-MM-DD-spec-name/)</item>
                            <item>CHECK for expected documents: mission.md, roadmap.md, tech-stack.md, decisions.md</item>
                            <item>NOTE any missing expected documents</item>
                            <item>PREPARE file list for systematic parsing</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>

            <item>
                <task name="parse_product_documentation" caption="Parse Product Documentation">
                    <hint>
                        Extract strategic context from core product documents to understand project vision, goals, and technical decisions.
                    </hint>
                    <text>
                        Read and parse all product documentation to extract strategic context including mission, roadmap progress, technology stack, and key decisions.

                        Data extraction targets:
                        - Mission: vision statement, target users, value propositions, success metrics
                        - Roadmap: completed phases, current phase progress, upcoming features, timeline estimates
                        - Tech Stack: current technologies, architecture decisions, infrastructure setup, development tools
                        - Decisions: strategic decisions made, technical debt items, architectural changes, process improvements
                    </text>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                            <item>memory</item>
                        </mcp-tooling>
                        <list>
                            <item>READ mission.md and extract: vision, target_users, value_props</item>
                            <item>READ roadmap.md and extract: phases, current_phase, completion_percentage</item>
                            <item>READ tech-stack.md and extract: frontend, backend, database, infrastructure</item>
                            <item>READ decisions.md and extract: decisions with id, date, title, status, category</item>
                            <item>VALIDATE data completeness and consistency</item>
                            <item>STORE parsed data in memory for report generation</item>
                            <item>UTILIZE sequential-thinking to analyze alignment and consistency</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>

            <item>
                <task name="analyze_spec_folders" caption="Analyze Specification Folders">
                    <hint>
                        Process each specification folder to extract progress data, completion status, and identify blockers.
                    </hint>
                    <text>
                        Systematically analyze each specification folder to determine completion status, extract requirements, and calculate progress metrics.

                        Status determination rules:
                        - Planning: tasks.md not created or no tasks checked
                        - InProgress: some tasks checked, some unchecked
                        - Completed: all tasks checked
                        - Blocked: explicit blocking indicators or overdue

                        Completion calculation: (checked_tasks / total_tasks) * 100

                        Priority inference:
                        - High: affects core user workflows
                        - Medium: enhances existing features
                        - Low: nice-to-have improvements
                        - Critical: security or stability issues
                    </text>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                            <item>memory</item>
                        </mcp-tooling>
                        <list>
                            <item>FOR each folder in .docs/specs/:</item>
                            <item>PARSE folder name for date and spec name</item>
                            <item>READ spec.md for: overview, user_stories, scope_items, out_of_scope, deliverables</item>
                            <item>READ tasks.md for completion status and calculate: total_tasks, completed_tasks, in_progress_tasks, blocked_tasks</item>
                            <item>READ sub-specs/ folder for: new_dependencies, database_changes, api_changes, testing_requirements</item>
                            <item>CALCULATE completion percentage from checked/unchecked tasks</item>
                            <item>DETERMINE status using status rules</item>
                            <item>IDENTIFY blocked or overdue items</item>
                            <item>STORE spec data in memory with relationships</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>

            <item>
                <task name="identify_system_patterns" caption="Identify System-Wide Patterns">
                    <hint>
                        Analyze cross-spec patterns to identify architectural trends, risks, and development patterns.
                    </hint>
                    <text>
                        Examine patterns across all specifications to identify technology trends, architectural evolution, and potential risks.

                        Pattern analysis areas:
                        - Technology trends: most used technologies, new adoption patterns, deprecated technologies
                        - Feature patterns: feature types, user story patterns, integration points
                        - Development patterns: average completion time, common blocking factors, test coverage trends
                        - Architectural evolution: database schema evolution, API design patterns, service architecture changes

                        Risk assessment:
                        - Technical risks: outdated dependencies, architectural debt, performance bottlenecks, security concerns
                        - Project risks: consistently blocked specs, resource allocation issues, timeline slippage, scope creep
                    </text>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                            <item>memory</item>
                        </mcp-tooling>
                        <list>
                            <item>ANALYZE technology usage patterns across specs</item>
                            <item>IDENTIFY most frequently used technologies</item>
                            <item>TRACK new technology adoption patterns</item>
                            <item>IDENTIFY common integration points and dependencies</item>
                            <item>EVALUATE architectural evolution trends</item>
                            <item>ASSESS development velocity and blocking patterns</item>
                            <item>CALCULATE average spec completion time</item>
                            <item>IDENTIFY technical risks and debt accumulation</item>
                            <item>EVALUATE resource allocation effectiveness</item>
                            <item>UTILIZE sequential-thinking to synthesize findings</item>
                        </list>
                    </stepwise-instructions>
                </task>
            </item>

            <item>
                <task name="generate_executive_summary" caption="Generate Executive Summary">
                    <hint>
                        Create a high-level overview with key metrics and health indicators for leadership consumption.
                    </hint>
                    <text>
                        Generate an executive summary that provides project health metrics, completion percentages, and strategic alignment assessment.

                        Health indicators:
                        - Green: >80% specs on track, <5% blocked items, strong velocity trend
                        - Yellow: 60-80% specs on track, 5-15% blocked items, declining velocity
                        - Red: <60% specs on track, >15% blocked items, critical issues present

                        Summary components:
                        - Project health: overall completion percentage, active specs count, velocity metrics, quality indicators
                        - Key metrics: features completed this month, features in progress, blocked items, technical debt
                        - Strategic alignment: mission alignment assessment, roadmap progress, resource allocation effectiveness
                    </text>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                        </mcp-tooling>
                        <list>
                            <item>CALCULATE overall completion percentage</item>
                            <item>COUNT total specifications, completed, in progress, and blocked</item>
                            <item>CALCULATE average completion time in days</item>
                            <item>ASSESS project health using health indicator criteria</item>
                            <item>EVALUATE mission alignment and roadmap adherence</item>
                            <item>ASSESS resource utilization efficiency</item>
                            <item>IDENTIFY immediate actions required (top 3)</item>
                            <item>GENERATE executive summary with template format</item>
                        </list>
                    </stepwise-instructions>

                    <template>
                        # Project Status Executive Summary

                        > Generated: [CURRENT_DATE]
                        > Report Period: [DATE_RANGE]
                        > Project Health: [HEALTH_INDICATOR]

                        ## Key Metrics

                        - **Total Specifications**: [TOTAL_SPECS]
                        - **Completed**: [COMPLETED_COUNT] ([COMPLETION_PERCENTAGE]%)
                        - **In Progress**: [IN_PROGRESS_COUNT]
                        - **Blocked**: [BLOCKED_COUNT]
                        - **Average Completion Time**: [AVG_DAYS] days

                        ## Strategic Alignment

                        **Mission Progress**: [ALIGNMENT_ASSESSMENT]
                        **Roadmap Adherence**: [TIMELINE_STATUS]
                        **Resource Utilization**: [EFFICIENCY_METRIC]

                        ## Immediate Actions Required

                        1. [ACTION_ITEM_1]
                        2. [ACTION_ITEM_2]
                        3. [ACTION_ITEM_3]
                    </template>
                </task>
            </item>

            <item>
                <task name="create_detailed_status_sections" caption="Create Detailed Status Sections">
                    <hint>
                        Organize specifications by status categories and provide detailed progress information with comprehensive templates.
                    </hint>
                    <text>
                        Create comprehensive status sections organized by active specifications, completed features, blocked items, and upcoming work.
                        Use detailed templates for each section to ensure consistency and completeness.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>GROUP specifications by status (Active, Completed, Blocked, Upcoming)</item>
                            <item>CREATE Active Specifications section using active_specs_template</item>
                            <item>CREATE Completed Features section using completed_features_template</item>
                            <item>CREATE Blocked Items section using blocked_items_template</item>
                            <item>CREATE Upcoming Work section with resource requirements</item>
                            <item>INCLUDE completion percentages, ETAs, and next actions for each</item>
                        </list>
                    </stepwise-instructions>

                    <templates>
                        <active_specs_template>
                            ## Active Specifications

                            ### [SPEC_NAME] ([COMPLETION_PERCENTAGE]%)
                            **Created**: [DATE] | **Priority**: [PRIORITY] | **ETA**: [ESTIMATED_DATE]

                            **Scope**: [BRIEF_DESCRIPTION]

                            **Progress**:
                            - ✅ [COMPLETED_TASK_COUNT] completed
                            - 🔄 [IN_PROGRESS_TASK_COUNT] in progress
                            - ⏳ [PENDING_TASK_COUNT] pending

                            **Next Actions**: [NEXT_STEPS]

                            ---
                        </active_specs_template>

                        <completed_features_template>
                            ## Recently Completed Specifications

                            ### [SPEC_NAME] ✅
                            **Completed**: [DATE] | **Duration**: [DAYS] days

                            **Delivered**:
                            - [DELIVERABLE_1]
                            - [DELIVERABLE_2]

                            **Impact**: [USER_IMPACT_SUMMARY]

                            ---
                        </completed_features_template>

                        <blocked_items_template>
                            ## Blocked Specifications ⚠️

                            ### [SPEC_NAME]
                            **Blocked Since**: [DATE] | **Priority**: [PRIORITY]

                            **Blocking Factor**: [BLOCKING_REASON]

                            **Impact**: [IMPACT_DESCRIPTION]

                            **Proposed Resolution**: [RESOLUTION_STRATEGY]

                            **Owner**: [RESPONSIBLE_PARTY]

                            ---
                        </blocked_items_template>
                    </templates>
                </task>
            </item>

            <item>
                <task name="generate_technical_insights" caption="Generate Technical Insights">
                    <hint>
                        Provide architectural guidance and technical trend analysis for development teams with comprehensive technical analysis.
                    </hint>
                    <text>
                        Analyze technical patterns across specifications to provide insights on architecture evolution, technology adoption, and code quality trends.
                        Include detailed analysis of database changes, API evolution, technology stack updates, and quality metrics.
                    </text>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                            <item>microsoft-docs</item>
                        </mcp-tooling>
                        <list>
                            <item>ANALYZE database schema evolution across specs</item>
                            <item>TRACK API design patterns and breaking changes</item>
                            <item>EVALUATE new technology adoption with justifications</item>
                            <item>IDENTIFY deprecated technologies and migration plans</item>
                            <item>ASSESS technical debt accumulation and trends</item>
                            <item>CALCULATE development velocity metrics and performance trends</item>
                            <item>GENERATE technical recommendations with rationale</item>
                            <item>UTILIZE microsoft-docs for technology best practices</item>
                        </list>
                    </stepwise-instructions>

                    <template>
                        ## Technical Insights

                        ### Architecture Evolution

                        **Database Changes**:
                        - [SCHEMA_CHANGE_SUMMARY]
                        - Impact on existing systems: [IMPACT_ASSESSMENT]

                        **API Evolution**:
                        - New endpoints: [ENDPOINT_COUNT]
                        - Breaking changes: [BREAKING_CHANGES]
                        - Version strategy: [VERSIONING_APPROACH]

                        ### Technology Stack Updates

                        **New Dependencies**:
                        - [DEPENDENCY_1]: [JUSTIFICATION]
                        - [DEPENDENCY_2]: [JUSTIFICATION]

                        **Deprecated Technologies**:
                        - [DEPRECATED_1]: [REPLACEMENT_PLAN]
                        - [DEPRECATED_2]: [MIGRATION_STATUS]

                        ### Quality Metrics

                        **Test Coverage**: [COVERAGE_PERCENTAGE]% ([TREND])
                        **Technical Debt**: [DEBT_ASSESSMENT]
                        **Performance**: [PERFORMANCE_TREND]

                        ### Recommendations

                        1. **[RECOMMENDATION_1]**: [RATIONALE]
                        2. **[RECOMMENDATION_2]**: [RATIONALE]
                        3. **[RECOMMENDATION_3]**: [RATIONALE]
                    </template>
                </task>
            </item>

            <item>
                <task name="create_timeline_visualization" caption="Create Timeline Visualization">
                    <hint>
                        Generate comprehensive timeline charts showing historical progress, current state, and future projections with detailed visualizations.
                    </hint>
                    <text>
                        Create visual representations of project timeline including historical progress bars, current sprint status, upcoming milestones, and velocity metrics.
                        Include detailed charts and progress visualizations.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>CALCULATE historical completion rates by month for last 6 months</item>
                            <item>GENERATE progress charts with ASCII progress bars</item>
                            <item>CREATE current month/week progress visualization</item>
                            <item>IDENTIFY upcoming milestones and deadlines</item>
                            <item>CALCULATE velocity metrics: average specs per month, trend analysis</item>
                            <item>ASSESS capacity utilization percentage</item>
                            <item>PROJECT future completion estimates</item>
                        </list>
                    </stepwise-instructions>

                    <template>
                        ## Project Timeline

                        ### Historical Progress (Last 6 Months)

                        ```
                        [MONTH-6] |████████░░| 80% - [SPECS_COMPLETED] specs completed
                        [MONTH-5] |██████████| 100% - [SPECS_COMPLETED] specs completed
                        [MONTH-4] |██████░░░░| 60% - [SPECS_COMPLETED] specs completed
                        [MONTH-3] |████████░░| 80% - [SPECS_COMPLETED] specs completed
                        [MONTH-2] |██████████| 100% - [SPECS_COMPLETED] specs completed
                        [MONTH-1] |███████░░░| 70% - [SPECS_COMPLETED] specs completed
                        ```

                        ### Current Month Progress

                        ```
                        Week 1: ████████░░ 80% complete
                        Week 2: ██████░░░░ 60% complete
                        Week 3: ████░░░░░░ 40% complete
                        Week 4: ░░░░░░░░░░ 0% planned
                        ```

                        ### Upcoming Milestones

                        - **[DATE]**: [MILESTONE_1]
                        - **[DATE]**: [MILESTONE_2]
                        - **[DATE]**: [MILESTONE_3]

                        ### Velocity Metrics

                        - **Average Specs/Month**: [VELOCITY]
                        - **Trend**: [IMPROVING/STABLE/DECLINING]
                        - **Capacity Utilization**: [UTILIZATION_PERCENTAGE]%
                    </template>
                </task>
            </item>

            <item>
                <task name="generate_recommendations" caption="Generate Strategic Recommendations">
                    <hint>
                        Provide prioritized, actionable recommendations based on analysis findings using impact/effort matrix and comprehensive categorization.
                    </hint>
                    <text>
                        Generate strategic recommendations categorized by urgency and impact, including immediate actions, strategic initiatives, and risk mitigation strategies.
                        Use prioritization matrix: High Impact/Low Effort (quick wins), High Impact/High Effort (strategic initiatives),
                        Low Impact/High Effort (avoid unless strategic), Low Impact/Low Effort (consider if resources available).
                    </text>
                    <stepwise-instructions>
                        <mcp-tooling>
                            <item>sequential-thinking</item>
                        </mcp-tooling>
                        <list>
                            <item>IDENTIFY critical blockers requiring immediate attention (next 2 weeks)</item>
                            <item>CATEGORIZE recommendations: Immediate Actions (2 weeks), Strategic Initiatives (quarter), Risk Mitigation</item>
                            <item>PRIORITIZE using impact/effort matrix</item>
                            <item>SEPARATE into Critical, Important, and Strategic categories</item>
                            <item>ASSIGN responsible parties and specific timelines</item>
                            <item>PROVIDE clear action plans with issue description, impact assessment, and solution</item>
                            <item>INCLUDE both technical and project risk mitigation strategies</item>
                            <item>UTILIZE sequential-thinking to validate recommendation logic and priorities</item>
                        </list>
                    </stepwise-instructions>

                    <template>
                        ## Strategic Recommendations

                        ### Immediate Actions (Next 2 Weeks)

                        #### 🔴 Critical
                        1. **[ACTION_1]**
                           - **Issue**: [PROBLEM_DESCRIPTION]
                           - **Impact**: [IMPACT_ASSESSMENT]
                           - **Solution**: [RECOMMENDED_ACTION]
                           - **Owner**: [RESPONSIBLE_PARTY]
                           - **Timeline**: [TIMEFRAME]

                        #### 🟡 Important
                        2. **[ACTION_2]**
                           - **Issue**: [PROBLEM_DESCRIPTION]
                           - **Solution**: [RECOMMENDED_ACTION]
                           - **Timeline**: [TIMEFRAME]

                        ### Strategic Initiatives (Next Quarter)

                        #### Architecture & Technology
                        - **[INITIATIVE_1]**: [DESCRIPTION_AND_RATIONALE]
                        - **[INITIATIVE_2]**: [DESCRIPTION_AND_RATIONALE]

                        #### Process Improvements
                        - **[IMPROVEMENT_1]**: [DESCRIPTION_AND_EXPECTED_OUTCOME]
                        - **[IMPROVEMENT_2]**: [DESCRIPTION_AND_EXPECTED_OUTCOME]

                        ### Risk Mitigation

                        #### Technical Risks
                        - **[RISK_1]**: [MITIGATION_STRATEGY]
                        - **[RISK_2]**: [MITIGATION_STRATEGY]

                        #### Project Risks
                        - **[RISK_1]**: [MITIGATION_STRATEGY]
                        - **[RISK_2]**: [MITIGATION_STRATEGY]
                    </template>
                </task>
            </item>

            <item>
                <task name="compile_final_report" caption="Compile Final Status Report">
                    <hint>
                        Assemble all sections into a comprehensive, professional status report document with complete structure and appendices.
                    </hint>
                    <text>
                        Compile all generated sections into a final status report with proper formatting, metadata, comprehensive appendices, and professional structure.
                        Save to .docs/reports/ directory with timestamped filename.
                    </text>
                    <stepwise-instructions>
                        <list>
                            <item>CREATE .docs/reports/ directory if it doesn't exist</item>
                            <item>DETERMINE current date for filename: status-report-YYYY-MM-DD.md</item>
                            <item>ASSEMBLE complete report using final report template</item>
                            <item>INCLUDE all generated sections in proper order</item>
                            <item>POPULATE project health dashboard with calculated metrics</item>
                            <item>CREATE comprehensive appendices: specification inventory, technical debt register, decision log</item>
                            <item>VALIDATE report completeness against checklist</item>
                            <item>SAVE final report to .docs/reports/</item>
                            <item>VERIFY all sections are properly formatted and complete</item>
                        </list>
                    </stepwise-instructions>

                    <OutputFormat>
                        <Document src="${workspaceFolder}/.docs/reports/status-report-YYYY-MM-DD.md" />
                        <template>
                            # Project Status Report

                            > **Generated**: [CURRENT_DATE]
                            > **Period**: [REPORT_PERIOD]
                            > **Next Review**: [NEXT_REVIEW_DATE]

                            ## Executive Summary

                            [EXECUTIVE_SUMMARY_CONTENT]

                            ## Project Health Dashboard

                            | Metric | Current | Target | Trend |
                            |--------|---------|---------|-------|
                            | Completion Rate | [RATE]% | [TARGET]% | [TREND] |
                            | Active Specs | [COUNT] | [CAPACITY] | [TREND] |
                            | Blocked Items | [COUNT] | 0 | [TREND] |
                            | Velocity | [SPECS_PER_MONTH] | [TARGET] | [TREND] |

                            ## Current Status

                            [ACTIVE_SPECIFICATIONS_CONTENT]

                            [COMPLETED_FEATURES_CONTENT]

                            [BLOCKED_ITEMS_CONTENT]

                            ## Technical Analysis

                            [TECHNICAL_INSIGHTS_CONTENT]

                            ## Timeline & Progress

                            [TIMELINE_VISUALIZATION_CONTENT]

                            ## Strategic Recommendations

                            [RECOMMENDATIONS_CONTENT]

                            ## Appendices

                            ### A. Specification Inventory
                            [COMPLETE_SPEC_LIST]

                            ### B. Technical Debt Register
                            [TECHNICAL_DEBT_ITEMS]

                            ### C. Decision Log Summary
                            [RECENT_DECISIONS]

                            ---

                            **Report Prepared By**: ${AI_MODEL}
                            **Next Status Review**: [NEXT_REVIEW_DATE]
                        </template>
                    </OutputFormat>
                </task>
            </item>
        </list>
    </stepwise-instructions>

    <text>
        ## Report Quality Standards

        IMPORTANT: When executing this workflow, ensure you follow these quality standards:

        Accuracy:
        <list>
            <item>Data must be current and verified</item>
            <item>Calculations must be correct</item>
            <item>Status assessments must be objective</item>
        </list>

        Completeness:
        <list>
            <item>All specs must be analyzed</item>
            <item>No critical information omitted</item>
            <item>Recommendations must be actionable</item>
        </list>

        Clarity:
        <list>
            <item>Executive summary for leadership</item>
            <item>Technical details for architects</item>
            <item>Action items clearly prioritized</item>
        </list>

        Timeliness:
        <list>
            <item>Report reflects current state</item>
            <item>Trends based on recent data</item>
            <item>Projections include latest changes</item>
        </list>

        ## Error Handling

        IMPORTANT: Handle these scenarios gracefully:

        Missing Files:
        <list>
            <item>Condition: Expected documentation files not found</item>
            <item>Action: Note missing files and continue with available data</item>
        </list>

        Corrupted Data:
        <list>
            <item>Condition: Spec files have invalid format</item>
            <item>Action: Report parsing issues and skip corrupted files</item>
        </list>

        Empty Directories:
        <list>
            <item>Condition: No spec folders found</item>
            <item>Action: Generate minimal report with recommendations to create specs</item>
        </list>

        ## Success Criteria

        IMPORTANT: Ensure the final report provides:
        <list>
            <item>Architects have clear project visibility</item>
            <item>Blockers and risks clearly identified</item>
            <item>Progress trends accurately represented</item>
            <item>Actionable recommendations provided</item>
            <item>Technical health assessed</item>
            <item>Resource needs identified</item>
        </list>

        IMPORTANT: Final checklist to verify completion:
        <list>
            <item>All .docs/specs/ folders analyzed</item>
            <item>Product documentation parsed</item>
            <item>Status calculations accurate</item>
            <item>Recommendations prioritized</item>
            <item>Report format professional</item>
            <item>Action items clearly defined</item>
            <item>Technical insights provided</item>
            <item>Timeline visualization created</item>
            <item>All templates properly populated</item>
            <item>Comprehensive appendices included</item>
            <item>Progress bars and charts generated</item>
            <item>Health indicators properly calculated</item>
        </list>
    </text>
</poml>
