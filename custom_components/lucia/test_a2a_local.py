"""Local test script for A2A integration without Home Assistant."""
import asyncio
import httpx
from a2a.client import A2ACardResolver, ClientConfig, ClientFactory
from a2a.types import Message, Part, Role, TextPart


async def test_agent_catalog(repository_url: str, api_key: str | None):
    """Test fetching the agent catalog from /agents endpoint."""
    print(f"\n=== Testing Agent Catalog Discovery ===")
    print(f"Catalog URL: {repository_url}/agents")

    # Create HTTP client with X-Api-Key header if provided
    # Also disable SSL verification for localhost testing
    headers = {}
    if api_key:
        headers["X-Api-Key"] = api_key

    httpx_client = httpx.AsyncClient(
        headers=headers,
        verify=False  # Disable SSL verification for localhost
    )

    try:
        # Fetch the agent catalog directly
        print("\nFetching agent catalog...")
        response = await httpx_client.get(f"{repository_url}/agents")
        response.raise_for_status()

        catalog = response.json()
        print(f"\n✓ Catalog retrieved successfully")
        print(f"  Status Code: {response.status_code}")
        print(f"  Response Type: {type(catalog)}")

        # Check if it's a list of agents
        if isinstance(catalog, list):
            print(f"  Agent Count: {len(catalog)}")
            for idx, agent in enumerate(catalog):
                print(f"\n  Agent #{idx + 1}:")
                print(f"    Name: {agent.get('name', 'N/A')}")
                print(f"    ID: {agent.get('id', 'N/A')}")
                print(f"    URL: {agent.get('url', 'N/A')}")
                print(f"    Description: {agent.get('description', 'N/A')[:100]}")
            return catalog
        else:
            print(f"  Catalog Data: {catalog}")
            return [catalog] if catalog else []

    except httpx.HTTPStatusError as e:
        print(f"✗ HTTP Error: {e.response.status_code}")
        print(f"  Response: {e.response.text}")
        return None
    except Exception as e:
        print(f"✗ Error: {e}")
        import traceback
        traceback.print_exc()
        return None
    finally:
        await httpx_client.aclose()


async def test_agent_discovery(repository_url: str, api_key: str | None):
    """Test discovering agents using A2ACardResolver."""
    print(f"\n=== Testing A2A Agent Discovery ===")
    print(f"Repository: {repository_url}")

    # Create HTTP client with X-Api-Key header if provided
    headers = {}
    if api_key:
        headers["X-Api-Key"] = api_key

    httpx_client = httpx.AsyncClient(
        headers=headers,
        verify=False  # Disable SSL verification for localhost
    )

    try:
        # Create resolver to get agent cards
        resolver = A2ACardResolver(
            httpx_client=httpx_client,
            base_url=repository_url,
        )

        # Get the agent card
        print("\nFetching agent card via A2ACardResolver...")
        agent_card = resolver.get_agent_card()

        if agent_card:
            print(f"\n✓ Agent found: {agent_card.name}")
            print(f"  ID: {agent_card.id if hasattr(agent_card, 'id') else 'N/A'}")
            print(f"  Description: {agent_card.description if hasattr(agent_card, 'description') else 'N/A'}")
            print(f"  Version: {agent_card.version if hasattr(agent_card, 'version') else 'N/A'}")
            print(f"  URL: {agent_card.url if hasattr(agent_card, 'url') else 'N/A'}")
            return agent_card
        else:
            print("✗ No agent card found")
            return None

    except Exception as e:
        print(f"✗ Error: {e}")
        import traceback
        traceback.print_exc()
        return None
    finally:
        await httpx_client.aclose()


async def test_send_message(agent_url: str, api_key: str | None, agent_id: str, message_text: str):
    """Test sending a message to an agent."""
    print(f"\n=== Testing Message Sending ===")
    print(f"Agent URL: {agent_url}")
    print(f"Agent ID: {agent_id}")
    print(f"Message: {message_text}")

    # Create HTTP client with X-Api-Key header if provided
    headers = {}
    if api_key:
        headers["X-Api-Key"] = api_key

    httpx_client = httpx.AsyncClient(
        headers=headers,
        verify=False  # Disable SSL verification for localhost
    )

    try:
        # Create client configuration
        config = ClientConfig(
            agent_url=agent_url,
            httpx_client=httpx_client,
        )

        # Create client using factory
        print("\nCreating client connection...")
        client = ClientFactory.create(config)

        # Create a message
        import uuid
        message_id = str(uuid.uuid4())
        context_id = str(uuid.uuid4())
        task_id = str(uuid.uuid4())

        request_message = Message(
            role=Role.user,
            parts=[Part(root=TextPart(text=message_text))],
            message_id=message_id,
            context_id=context_id,
            task_id=task_id,
        )

        print(f"\nSending message (task_id: {task_id})...")
        # Send message to agent
        response = await client.send_message(request_message)

        print(f"\n✓ Response received:")
        print(f"  Type: {type(response)}")
        if hasattr(response, 'parts'):
            for part in response.parts:
                if hasattr(part, 'root') and isinstance(part.root, TextPart):
                    print(f"  Text: {part.root.text}")
        else:
            print(f"  Response: {response}")

        return response

    except Exception as e:
        print(f"✗ Error sending message: {e}")
        import traceback
        traceback.print_exc()
        return None
    finally:
        await httpx_client.aclose()


async def main():
    """Main test function."""
    print("=" * 60)
    print("A2A Integration Local Test")
    print("=" * 60)

    # Configuration - UPDATE THESE VALUES
    REPOSITORY_URL = "https://localhost:7235"  # Your agent repository URL
    API_KEY = None  # Set to None if no auth required, or "your-api-key-here"

    print(f"\nConfiguration:")
    print(f"  Repository: {REPOSITORY_URL}")
    print(f"  API Key: {'(none)' if not API_KEY else '*' * len(API_KEY)}")

    # Test 1: Fetch agent catalog
    print("\n" + "=" * 60)
    catalog = await test_agent_catalog(REPOSITORY_URL, API_KEY)

    if not catalog:
        print("\n✗ Cannot proceed without agent catalog")
        return

    # Test 2: Try A2ACardResolver discovery
    print("\n" + "=" * 60)
    agent_card = await test_agent_discovery(REPOSITORY_URL, API_KEY)

    # Test 3: Send a test message to the first agent
    print("\n" + "=" * 60)
    if catalog and len(catalog) > 0:
        first_agent = catalog[0]
        agent_url = first_agent.get('url', REPOSITORY_URL)
        agent_id = first_agent.get('id', 'default')
        test_message = "Hello! Can you tell me what you can do?"

        response = await test_send_message(
            agent_url,
            API_KEY,
            agent_id,
            test_message
        )

        if response:
            print("\n" + "=" * 60)
            print("✓ All tests passed!")
            print("=" * 60)
        else:
            print("\n" + "=" * 60)
            print("✗ Message sending failed")
            print("=" * 60)
    elif agent_card:
        # Fallback to agent_card if catalog failed
        agent_url = agent_card.url if hasattr(agent_card, 'url') else REPOSITORY_URL
        agent_id = agent_card.id if hasattr(agent_card, 'id') else "default"
        test_message = "Hello! Can you tell me what you can do?"

        response = await test_send_message(
            agent_url,
            API_KEY,
            agent_id,
            test_message
        )

        if response:
            print("\n" + "=" * 60)
            print("✓ All tests passed!")
            print("=" * 60)
        else:
            print("\n" + "=" * 60)
            print("✗ Message sending failed")
            print("=" * 60)
    else:
        print("\n✗ No agents available to test message sending")


if __name__ == "__main__":
    print("\n" + "=" * 60)
    print("INSTRUCTIONS:")
    print("=" * 60)
    print("1. Update REPOSITORY_URL and API_KEY in the main() function")
    print("2. Make sure your Lucia agent is running")
    print("3. Run: python test_a2a_local.py")
    print("=" * 60 + "\n")

    asyncio.run(main())
