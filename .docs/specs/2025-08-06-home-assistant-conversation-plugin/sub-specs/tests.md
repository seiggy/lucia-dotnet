# Tests Specification

This is the tests coverage details for the spec detailed in @.agent-os/specs/2025-08-06-home-assistant-conversation-plugin/spec.md

> Created: 2025-08-06
> Version: 1.0.0

## Test Coverage

### Unit Tests

**ConfigFlow (test_config_flow.py)**
- Test successful user flow with valid API endpoint and key
- Test connection failure with invalid API endpoint
- Test authentication failure with invalid API key
- Test abort when no agents available in registry
- Test reconfiguration flow updates existing entry
- Test form validation for missing required fields
- Test unique ID handling for preventing duplicates

**ConversationEntity (test_conversation.py)**
- Test async_process handles valid conversation input
- Test response formatting matches Home Assistant expectations
- Test conversation_id persistence across multi-turn dialogue
- Test language parameter passed correctly to API
- Test continue_conversation flag handling
- Test error response when API unavailable
- Test timeout handling for slow responses

**A2AClient (test_a2a_client.py)**
- Test agent registry fetch and parsing
- Test conversation request formatting
- Test response parsing for successful requests
- Test error handling for various HTTP status codes
- Test retry logic with exponential backoff
- Test API key included in authorization header
- Test connection pooling and session management

### Integration Tests

**Config Flow Integration**
- Test complete flow from UI to stored config entry
- Test integration appears in Home Assistant UI after setup
- Test reconfiguration accessible from integration page
- Test removal cleanly unloads integration

**Conversation Processing Integration**
- Test end-to-end conversation from Home Assistant to Lucia API
- Test conversation appears in Home Assistant conversation history
- Test multi-turn conversation maintains context
- Test error notifications appear in Home Assistant UI
- Test rate limiting handled gracefully

**API Communication Integration**
- Test real HTTP calls to mock Lucia API server
- Test SSL certificate validation (and bypass option)
- Test connection timeout and retry behavior
- Test concurrent request handling

### Feature Tests

**User Configuration Journey**
- User adds integration via UI
- User enters API endpoint and key
- System validates connection
- User selects orchestration agent
- Integration successfully configured

**Natural Language Processing**
- User types "turn on living room lights"
- Request sent to Lucia API
- Response received and processed
- Confirmation message displayed to user

**Error Recovery Scenario**
- API becomes unavailable during conversation
- Error logged appropriately
- Persistent notification created
- User can reconfigure integration
- System recovers when API returns

### Mocking Requirements

**External Services**
- **Lucia API Server:** Mock using `aioresponses` for unit tests
  - Mock successful agent registry response
  - Mock conversation responses
  - Mock various error conditions
- **Home Assistant Core:** Use `pytest-homeassistant-custom-component`
  - Mock config entry system
  - Mock persistent notifications
  - Mock conversation service

**Time-based Tests**
- **Rate Limiting:** Mock time.time() for rate limit testing
- **Timeout Testing:** Mock asyncio timeouts
- **Retry Backoff:** Mock sleep delays for faster tests

## Test Data Fixtures

### Sample Agent Registry Response
```python
MOCK_AGENTS = [
    {
        "id": "orchestrator",
        "name": "Test Orchestrator",
        "capabilities": {"domains": ["lights", "climate"]},
        "endpoint": "http://test/api/agents/orchestrator"
    }
]
```

### Sample Conversation Exchanges
```python
CONVERSATION_SAMPLES = [
    {
        "input": "turn on the lights",
        "response": "I've turned on the lights for you.",
        "continue": False
    },
    {
        "input": "what's the temperature?",
        "response": "The current temperature is 72°F.",
        "continue": True
    }
]
```

### Error Scenarios
```python
ERROR_SCENARIOS = [
    (401, "unauthorized", "Invalid API key"),
    (404, "not_found", "Agent not found"),
    (500, "server_error", "Internal server error"),
    (503, "unavailable", "Service temporarily unavailable")
]
```

## Test Execution Strategy

### Continuous Integration
- Run all unit tests on every commit
- Run integration tests on pull requests
- Use GitHub Actions for CI/CD pipeline
- Maintain >80% code coverage

### Local Development
```bash
# Run all tests
pytest tests/

# Run specific test file
pytest tests/test_config_flow.py

# Run with coverage
pytest --cov=custom_components.lucia tests/

# Run only unit tests (fast)
pytest tests/unit/

# Run integration tests (slower)
pytest tests/integration/
```

### Test Environment Setup
```python
# conftest.py fixtures
@pytest.fixture
def mock_lucia_api():
    """Mock Lucia API responses."""
    
@pytest.fixture
def hass_with_lucia():
    """Home Assistant with Lucia pre-configured."""
    
@pytest.fixture
def conversation_input():
    """Sample conversation input object."""
```

## Performance Testing

- Response time < 1 second for conversation processing
- Handle 10 concurrent conversations without degradation
- Memory usage stable over 1000 conversation turns
- Graceful degradation under API rate limiting