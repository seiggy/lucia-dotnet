# Spec Tasks

These are the tasks to be completed for the spec detailed in @.agent-os/specs/2025-08-06-home-assistant-conversation-plugin/spec.md

> Created: 2025-08-06
> Status: Ready for Implementation

## Tasks

- [ ] 1. Migrate A2A Implementation to Official Package
  - [ ] 1.1 Write tests for A2A package integration
  - [x] 1.2 Add A2A NuGet package (0.1.0-preview.2) to lucia.Agents project
  - [x] 1.3 Remove custom A2A implementation files (A2A/ directory)
  - [x] 1.4 Update AgentRegistryApi to use official A2A AgentCard model
  - [ ] 1.5 Implement JSON-RPC 2.0 conversation endpoints for agents
  - [ ] 1.6 Update service registration to use official A2A services
  - [ ] 1.7 Update all agent implementations to use official A2A interfaces
  - [ ] 1.8 Verify all tests pass with new A2A implementation

- [ ] 2. Create Home Assistant Custom Component Structure
  - [ ] 2.1 Write tests for custom component structure
  - [ ] 2.2 Create custom_components/lucia/ directory structure
  - [ ] 2.3 Implement manifest.json with proper metadata and dependencies
  - [ ] 2.4 Create __init__.py with integration setup and entry point
  - [ ] 2.5 Create const.py with constants and configuration keys
  - [ ] 2.6 Create strings.json with UI text and translations
  - [ ] 2.7 Verify component loads correctly in Home Assistant

- [ ] 3. Implement Configuration Flow
  - [ ] 3.1 Write tests for config flow functionality
  - [ ] 3.2 Implement ConfigFlow class with user step for API setup
  - [ ] 3.3 Add API connectivity validation during config
  - [ ] 3.4 Implement agent selection from registry API
  - [ ] 3.5 Add reconfiguration flow for updating settings
  - [ ] 3.6 Implement error handling with user-friendly messages
  - [ ] 3.7 Verify config flow works end-to-end in Home Assistant UI

- [ ] 4. Build A2A Protocol Client
  - [ ] 4.1 Write tests for A2A client communication
  - [ ] 4.2 Create a2a_client.py with async HTTP client using aiohttp
  - [ ] 4.3 Implement agent registry fetching with proper error handling
  - [ ] 4.4 Implement JSON-RPC 2.0 conversation messaging
  - [ ] 4.5 Add authentication handling with API key headers
  - [ ] 4.6 Implement retry logic with exponential backoff
  - [ ] 4.7 Verify client successfully communicates with Lucia API

- [ ] 5. Create Conversation Entity
  - [ ] 5.1 Write tests for conversation processing
  - [ ] 5.2 Implement ConversationEntity class extending Home Assistant base
  - [ ] 5.3 Implement _async_handle_message for processing user input
  - [ ] 5.4 Add conversation ID tracking for multi-turn dialogues
  - [ ] 5.5 Format responses for Home Assistant conversation system
  - [ ] 5.6 Handle language parameter forwarding to agents
  - [ ] 5.7 Verify conversations work in Home Assistant interface

- [ ] 6. Add Error Handling and Notifications
  - [ ] 6.1 Write tests for error scenarios and notification system
  - [ ] 6.2 Implement comprehensive exception handling in A2A client
  - [ ] 6.3 Add persistent notifications for critical errors
  - [ ] 6.4 Implement connection timeout and retry mechanisms
  - [ ] 6.5 Add logging for debugging and monitoring
  - [ ] 6.6 Handle API rate limiting gracefully
  - [ ] 6.7 Verify error messages are user-friendly and actionable

- [ ] 7. Integration Testing and Documentation
  - [ ] 7.1 Write integration tests for complete user workflows
  - [ ] 7.2 Test installation via HACS and manual methods
  - [ ] 7.3 Test conversation flow from Home Assistant to Lucia agents
  - [ ] 7.4 Verify multi-turn conversations maintain context
  - [ ] 7.5 Test error recovery and reconfiguration scenarios
  - [ ] 7.6 Update Home Assistant custom component documentation
  - [ ] 7.7 Verify all functionality works as specified


### Changelog:

9/17/2025: Refactor agent registry and chat history management

- Removed `ChatHistoryExtensions` and `SummarizingChatHistoryReducer` classes to streamline chat history handling.
- Introduced `AgentRegistry` as an abstract class and implemented `LocalAgentRegistry` for in-memory agent management.
- Updated `IAgentRegistry` interface to reflect new abstract methods.
- Added `LightControlSkill` for Home Assistant light control with caching and similarity search capabilities.
- Updated project dependencies to target .NET 10.0 and included new packages for AI and agent orchestration.
- Enhanced `A2AClientService` with improved logging and error handling.
- Adjusted `AgentInitializationService` to utilize the new `AgentRegistry` structure.
- Cleaned up project files and ensured compatibility with the latest SDK versions.