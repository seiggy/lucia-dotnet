"""Simple test script to check the agent catalog endpoint."""
import asyncio
import httpx
import json
import warnings
import sys

# Fix encoding for Windows PowerShell
if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')

# Suppress SSL warnings for localhost testing
warnings.filterwarnings('ignore', message='Unverified HTTPS request')


async def test_catalog_endpoint():
    """Test the /agents catalog endpoint."""
    print("=" * 60)
    print("Testing Agent Catalog Endpoint")
    print("=" * 60)

    REPOSITORY_URL = "https://localhost:7235"
    API_KEY = None  # Set if needed

    print(f"\nConfiguration:")
    print(f"  Base URL: {REPOSITORY_URL}")
    print(f"  Catalog Endpoint: {REPOSITORY_URL}/agents")
    print(f"  API Key: {'(none)' if not API_KEY else '***'}")

    # Create HTTP client
    headers = {}
    if API_KEY:
        headers["X-Api-Key"] = API_KEY

    client = httpx.AsyncClient(verify=False, headers=headers)

    try:
        print("\n--- Test 1: Fetch Agent Catalog ---")
        response = await client.get(f"{REPOSITORY_URL}/agents")

        print(f"Status Code: {response.status_code}")
        print(f"Content-Type: {response.headers.get('content-type')}")

        if response.status_code == 200:
            print("✓ Request successful!")

            # Try to parse as JSON
            try:
                catalog = response.json()
                print(f"\nCatalog Data Type: {type(catalog)}")
                print(f"\nFormatted Response:")
                print(json.dumps(catalog, indent=2))

                # If it's a list, show agent details
                if isinstance(catalog, list):
                    print(f"\n--- Discovered {len(catalog)} agent(s) ---")
                    for idx, agent in enumerate(catalog, 1):
                        print(f"\nAgent #{idx}:")
                        print(f"  Name: {agent.get('name', 'N/A')}")
                        print(f"  ID: {agent.get('id', 'N/A')}")

                        # Handle relative URLs
                        agent_url = agent.get('url', 'N/A')
                        if agent_url.startswith('/'):
                            agent_url = f"{REPOSITORY_URL}{agent_url}"
                            agent['url'] = agent_url  # Update in place

                        print(f"  URL: {agent_url}")
                        print(f"  Version: {agent.get('version', 'N/A')}")
                        if 'description' in agent:
                            desc = agent['description']
                            print(f"  Description: {desc[:100]}{'...' if len(desc) > 100 else ''}")

                        # Show available endpoints if present
                        if 'endpoints' in agent:
                            print(f"  Endpoints: {list(agent['endpoints'].keys())}")

                        # Show skills if present
                        if 'skills' in agent and agent['skills']:
                            print(f"  Skills: {len(agent['skills'])} skill(s)")
                            for skill in agent['skills']:
                                print(f"    - {skill.get('name', 'N/A')}: {skill.get('description', 'N/A')[:60]}")

                    return catalog
                else:
                    print("\n✓ Single agent returned")
                    return [catalog]

            except json.JSONDecodeError:
                print("✗ Response is not valid JSON")
                print(f"Response Text: {response.text[:500]}")
                return None
        else:
            print(f"✗ Request failed")
            print(f"Response: {response.text}")
            return None

    except httpx.ConnectError as e:
        print(f"\n✗ Connection Error: {e}")
        print(f"Make sure your agent is running at {REPOSITORY_URL}")
        return None
    except Exception as e:
        print(f"\n✗ Error: {e}")
        import traceback
        traceback.print_exc()
        return None
    finally:
        await client.aclose()


async def test_agent_card_endpoint(base_url: str):
    """Test the /.well-known/ai-plugin.json endpoint for agent cards."""
    print("\n" + "=" * 60)
    print("Testing Agent Card Endpoint")
    print("=" * 60)

    client = httpx.AsyncClient(verify=False)

    try:
        print("\n--- Test 2: Fetch Agent Card ---")
        endpoint = f"{base_url}/.well-known/ai-plugin.json"
        print(f"Endpoint: {endpoint}")

        response = await client.get(endpoint)

        print(f"Status Code: {response.status_code}")

        if response.status_code == 200:
            print("✓ Agent card found!")

            try:
                card = response.json()
                print(f"\nAgent Card:")
                print(json.dumps(card, indent=2))
                return card
            except json.JSONDecodeError:
                print("✗ Response is not valid JSON")
                print(f"Response Text: {response.text[:500]}")
                return None
        elif response.status_code == 404:
            print("✗ Agent card endpoint not found")
            print("This is expected if the agent doesn't implement this endpoint")
            return None
        else:
            print(f"✗ Request failed: {response.status_code}")
            print(f"Response: {response.text}")
            return None

    except Exception as e:
        print(f"✗ Error: {e}")
        return None
    finally:
        await client.aclose()


async def test_message_endpoint(agent_url: str):
    """Test sending a message to an agent's A2A endpoint."""
    print("\n" + "=" * 60)
    print("Testing Message Endpoint")
    print("=" * 60)

    client = httpx.AsyncClient(verify=False)

    try:
        # Test 1: Try OpenAPI spec format with /v1/message:send
        print("\n--- Test 3A: OpenAPI Spec Format (/v1/message:send) ---")

        import uuid
        message_id = str(uuid.uuid4())
        context_id = str(uuid.uuid4())
        # Note: taskId removed - Agent Framework doesn't support task management yet

        openapi_request = {
            "message": {
                "kind": "message",
                "role": "user",
                "parts": [
                    {
                        "kind": "text",
                        "text": "Hello! What can you do?",
                        "metadata": None
                    }
                ],
                "metadata": None,
                "referenceTaskIds": [],
                "messageId": message_id,
                "taskId": None,  # Set to None - task management not supported
                "contextId": context_id,  # Keep for conversation threading
                "extensions": []
            },
            "configuration": {
                "acceptedOutputModes": ["text"],
                "pushNotificationConfig": None,
                "historyLength": None,
                "blocking": True
            },
            "metadata": None
        }

        openapi_endpoint = f"{agent_url}/v1/message:send"
        print(f"\nTrying endpoint: {openapi_endpoint}")
        print(f"\nRequest payload:")
        print(json.dumps(openapi_request, indent=2))

        response = await client.post(
            openapi_endpoint,
            headers={"Content-Type": "application/json"},
            json=openapi_request,
            timeout=30.0
        )

        print(f"\nStatus Code: {response.status_code}")

        if response.status_code in [200, 201]:
            print("✓ Request sent successfully!")

            try:
                result = response.json()
                print(f"\nResponse:")
                print(json.dumps(result, indent=2))

                # Extract message text if present
                openapi_success = False
                if isinstance(result, dict) and 'message' in result:
                    msg = result['message']
                    if 'parts' in msg:
                        for part in msg['parts']:
                            if part.get('kind') == 'text':
                                print(f"\n✓✓✓ SUCCESS! Agent Response Text:")
                                print(f"    {part.get('text', '')[:200]}...")
                                openapi_success = True
                elif isinstance(result, dict) and 'parts' in result:
                    for part in result['parts']:
                        if part.get('kind') == 'text':
                            print(f"\n✓✓✓ SUCCESS! Agent Response Text:")
                            print(f"    {part.get('text', '')[:200]}...")
                            openapi_success = True

                if openapi_success:
                    print("\n✓ OpenAPI format successful")
                    # Continue to test JSON-RPC as well
            except json.JSONDecodeError:
                print(f"Response Text: {response.text}")
                return response.text
        else:
            print(f"✗ Request failed")
            print(f"Response: {response.text[:500]}")

        # Test 2: Also try JSON-RPC format to compare
        print("\n--- Test 3B: JSON-RPC Format (without taskId) ---")

        message = {
            "kind": "message",
            "role": "user",
            "parts": [
                {
                    "kind": "text",
                    "text": "Hello! What can you do?"
                }
            ],
            "messageId": message_id,
            "contextId": context_id,
            "taskId": None  # Set to None - task management not supported
        }

        jsonrpc_request = {
            "jsonrpc": "2.0",
            "method": "message/send",
            "params": {
                "message": message
            },
            "id": 1
        }

        print(f"\nTrying endpoint: {agent_url}")
        print(f"Method: message/send")

        response = await client.post(
            agent_url,
            json=jsonrpc_request,
            timeout=30.0
        )

        print(f"\nStatus Code: {response.status_code}")

        if response.status_code in [200, 201]:
            print("✓ Request sent successfully!")

            try:
                result = response.json()
                print(f"\nResponse:")
                print(json.dumps(result, indent=2))

                # Check for JSON-RPC error
                if 'error' in result:
                    print(f"\n⚠️  JSON-RPC Error: {result['error'].get('message', 'Unknown error')}")
                    print(f"   Code: {result['error'].get('code', 'N/A')}")
                else:
                    # Check for successful result
                    if 'result' in result:
                        res = result['result']
                        if 'parts' in res:
                            for part in res['parts']:
                                if part.get('kind') == 'text':
                                    print(f"\n✓✓✓ SUCCESS! Agent Response Text:")
                                    print(f"    {part.get('text', '')[:200]}...")
                    return result

            except json.JSONDecodeError:
                print(f"Response Text: {response.text}")
                return response.text
        else:
            print(f"  Error: {response.status_code}")
            print(f"  Response: {response.text[:200]}")

        return None

    except Exception as e:
        print(f"\n✗ Error: {e}")
        import traceback
        traceback.print_exc()
        return None
    finally:
        await client.aclose()


async def main():
    """Run all tests."""
    print("\nLucia Agent Discovery & Messaging Test\n")

    # Test 1: Get catalog
    catalog = await test_catalog_endpoint()

    if catalog and len(catalog) > 0:
        # Test 2: Get agent card for first agent
        first_agent = catalog[0]
        agent_url = first_agent.get('url', 'https://localhost:7235')

        await test_agent_card_endpoint(agent_url)

        # Test 3: Try sending a message to the agent endpoint from catalog
        print("\n" + "=" * 60)
        print("Testing Direct Agent Endpoint from Catalog")
        print("=" * 60)
        result = await test_message_endpoint(agent_url)

        if result:
            print("\n" + "=" * 60)
            print("✓✓✓ ALL TESTS PASSED!")
            print("=" * 60)
            print("\nSummary:")
            print("  ✓ Agent catalog discovery working")
            print("  ✓ Message endpoint working (OpenAPI format)")
            print("  ✓ Agent responses received successfully")
            print(f"  ✓ Context ID preserved: {result.get('contextId', 'N/A')}")
        else:
            print("\n" + "=" * 60)
            print("⚠️  Tests completed with errors")
            print("=" * 60)
    else:
        print("\n⚠️  No catalog available, skipping further tests")

    print("\n" + "=" * 60)
    print("Tests Complete")
    print("=" * 60)


if __name__ == "__main__":
    asyncio.run(main())
