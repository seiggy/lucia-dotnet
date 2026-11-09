# Family member app API

This guide is for building the Lucia mobile and desktop apps that family members use. These apps talk to the endpoints under `/api/me` and to `POST /api/pairing/redeem`. Everything else under `/api` is the administration API used by the web portal; a family member's credentials cannot reach it.

> **Status: experimental.** The member API, the action broker, and the V2 assistant are new and may change before they are declared stable.

## Concepts

- **Family member.** A person in the household, with a role: `Administrator`, `Adult`, `Child`, or `Guest`. An administrator creates members in the portal under **Family**.
- **Credentials.** A member signs in with a device key from QR pairing, or with an OpenID Connect token when single sign-on is configured. Both resolve to the same member. A device key belongs to one device; removing it in the portal stops that device immediately.
- **Permissions.** Each request runs as the member. Lucia checks every agent and tool the request uses against the member's rules. A rule can allow it, block it, or hold it for a guardian's approval.
- **Model controls.** Quiet hours, spending limits, blocked topics, and guardrails can refuse a request before it runs. The app shows the refusal like any other reply.

## Pairing a device

The administrator opens **Family → member → Devices & sign-in → Create pairing code**. The portal shows a QR code that encodes:

```text
lucia://pair?server=<url-encoded server URL>&code=<url-encoded code>
```

- `server` is the Lucia base URL. It comes from the `Auth:PublicBaseUrl` setting, or from the address the administrator used to open the portal.
- `code` is a one-time code. It expires after 10 minutes by default (at most 1 day) and can be redeemed once.

The portal also shows the server and code as text, so the app should offer manual entry too.

### `POST /api/pairing/redeem`

No authentication. Limited to 10 requests per minute per client address.

```http
POST /api/pairing/redeem
Content-Type: application/json

{ "code": "Xk3v9QpLm2Rt7WbYc5Hd8Nf1Gs4Jz6Ae0UoIiKlMnB", "deviceName": "Sam's iPad" }
```

`deviceName` is optional and is cut to 64 characters. Without it, the device is named "<member>'s device". The administrator sees this name in the portal.

```json
{
  "apiKey": "lk_...",
  "keyId": "3f2a9c...",
  "member": { "id": "k1", "displayName": "Sam", "role": "Child" }
}
```

| Status | Meaning |
| --- | --- |
| 200 | Paired. `apiKey` is shown only once. |
| 400 | The code is unknown, expired, already used, or the member is paused or their access has ended. Ask the administrator for a new code. |
| 429 | Too many attempts. Retry after a minute. |

Store `apiKey` in the platform's secure storage (Keychain, Android Keystore, Windows Credential Manager). Lucia keeps only its hash, so a lost key cannot be recovered; pair the device again.

## Authenticating requests

### Device key

Send the key on every request:

```http
X-API-Key: lk_...
```

A device key has the `member` scope. It works only on member endpoints, and only while the member is active and their access hasn't ended. It cannot sign in to the portal.

### OpenID Connect

When the administrator enables single sign-on (**Family → App sign-in**), the app can sign in with the configured provider and send the access token:

```http
Authorization: Bearer <access token>
```

- The token's audience (`aud`) must match the configured audience.
- Lucia accepts the token only if the administrator linked that account to a member, using the token's issuer (`iss`) and subject (`sub`). The link is made under the member's **Devices & sign-in** tab.
- Lucia ignores any roles or `lucia:*` claims in the token. Permissions always come from the member's settings.
- Configuration changes take effect after Lucia restarts. The desktop app can fall back to a device key when single sign-on isn't configured.

### Authentication errors

| Status | Meaning |
| --- | --- |
| 401 | No credentials, an invalid or revoked key, an expired token, or a key or account not linked to an active member. Send the user back to pairing or sign-in. |
| 403 | Valid credentials, but not for this endpoint. Member credentials get 403 on the administration API, and a portal key gets 403 on `/api/me`. |

## Member endpoints

All of these require member credentials. Request and response bodies are JSON with camelCase names. Error bodies have the form `{ "error": "..." }`.

### `GET /api/me`

The signed-in member.

```json
{ "id": "k1", "displayName": "Sam", "role": "Child", "guardianIds": ["a1", "a2"] }
```

### `POST /api/me/conversation`

Send what the member typed or dictated.

```json
{
  "text": "turn on the kitchen lights",
  "context": {
    "conversationId": "c7e1...",
    "deviceId": "sams-ipad",
    "deviceArea": "Kitchen",
    "timestamp": "2026-11-09T19:12:00Z"
  }
}
```

| Field | Required | Notes |
| --- | --- | --- |
| `text` | Yes | The member's words. |
| `context.conversationId` | No | Send the `conversationId` from the previous reply to continue a conversation. Omit it to start a new one. Conversations are private to the member, so another member can't continue one by reusing the ID. |
| `context.deviceId` | No | A stable identifier for the device. |
| `context.deviceArea` | No | The area the device is in, used to resolve "the lights" to the right room. |
| `context.timestamp`, `context.location` | No | Optional hints. |

Lucia sets the member's identity itself. It ignores `context.userId`, `context.speakerId`, and `promptOverride` on this endpoint, so an app cannot read another person's memories or history, or change the system prompt.

App requests are never treated as voice. The **Ignore unenrolled voices** setting doesn't apply to them, and they don't need a voice profile.

The reply:

```json
{
  "type": "llm",
  "text": "The kitchen lights are on.",
  "conversationId": "c7e1...",
  "needsInput": false
}
```

| `type` | What the app should do |
| --- | --- |
| `command` | Lucia ran a direct home command. Show `text`. A `command` object has details for debugging. |
| `llm` | Lucia's assistant answered. Show `text`. |
| `refused` | Quiet hours, a spending limit, a blocked topic, or a guardrail stopped the request before it ran. Show `text`; nothing was done. |
| `error` | The request couldn't be processed. Show `text`. |

When `needsInput` is `true`, the reply is a question. Keep the conversation open and send the answer with the same `conversationId`.

Under normal operation the reply is a single JSON body. If the server's assistant engine isn't available, it responds with `text/event-stream` instead, sending `metadata`, then `done` or `error` events, as described in the [conversation API guide](conversation-api.md). Apps should handle both content types.

#### Blocked actions and guardian approval

When the member asks for something their rules block, nothing happens and the reply says so in `text`. When a rule requires a guardian's approval, Lucia saves a request for their guardians, doesn't do the action, and says in `text` that a guardian has been asked.

An approval covers that exact action once. After a guardian approves, the member asks again with the same request, and Lucia carries it out. Different details, such as another temperature, create a new request. Requests expire after one hour.

### `GET /api/me/approvals`

Pending requests this member can decide as a guardian, oldest first. Expired requests are left out.

```json
[
  {
    "id": "9b0c...",
    "requester": { "id": "k1", "displayName": "Sam" },
    "capabilityId": "SetClimateTemperature",
    "agentId": "climate-agent",
    "arguments": { "entityId": "climate.bedroom", "temperature": 24 },
    "summary": "Sam asked to run SetClimateTemperature (climate-agent) with {...}",
    "status": "AwaitingDecision",
    "createdAt": "2026-11-09T19:12:03Z",
    "expiresAt": "2026-11-09T20:12:03Z",
    "decidedBy": null,
    "decidedAt": null
  }
]
```

Build the approval screen from `requester`, `capabilityId`, `agentId`, and `arguments`. `summary` is a plain-text fallback.

### `POST /api/me/approvals/{id}/approve` and `POST /api/me/approvals/{id}/deny`

No request body. Only an active guardian listed on the request can decide it, and only once.

| Status | Meaning |
| --- | --- |
| 200 | Decided. The body is the updated request. |
| 404 | No such request, or this member can't decide it. |
| 409 | Already decided, or expired. |

### `GET /api/me/requests?limit=25`

The member's own requests that needed a decision, newest first, in the same shape as approvals. `limit` is 1 to 100 (default 25). Use `status` to show what happened:

| `status` | Meaning |
| --- | --- |
| `AwaitingDecision` | Waiting for a guardian. |
| `Approved` | Approved. Ask again to carry it out. |
| `Denied`, `Expired` | Not done. |
| `Executing`, `Succeeded`, `Failed` | Carried out after approval. |

### `GET /api/me/usage?limit=25`

The member's model usage and limits. Costs are estimates in US dollars, from token counts and the administrator's provider prices. Local models without prices count as free.

```json
{
  "today": { "calls": 14, "inputTokens": 42100, "outputTokens": 6900, "costUsd": 0.4123 },
  "month": { "calls": 212, "inputTokens": 611000, "outputTokens": 98000, "costUsd": 3.21 },
  "dailyBudgetUsd": 0.5,
  "monthlyBudgetUsd": 5,
  "recent": [
    {
      "id": "u0",
      "memberId": "k1",
      "timestamp": "2026-11-09T19:12:05Z",
      "providerId": "local-ollama",
      "model": "qwen3:8b",
      "inputTokens": 3010,
      "outputTokens": 420,
      "costUsd": 0
    }
  ]
}
```

A null budget means no limit. Days and months follow the server's time zone.

## What the apps can't do

These stay in the administration portal:

- Change a member's role, permissions, model controls, or guardians.
- Pair devices, link accounts, or remove credentials.
- Change single sign-on settings.

## Related

- [Conversation API](conversation-api.md): the full conversation pipeline and the event-stream format.
- [Dashboard README](../lucia-dashboard/README.md#family): the administrator's Family page.
