# Bishop — Shopping list fallback for todo-backed Home Assistant lists

## Context
Bug #42 showed that Lucia's shopping list endpoint assumed Home Assistant always exposed `GET /api/shopping_list`. That works for the native `shopping_list` integration, but CalDAV and other to-do providers expose list items through the todo platform instead.

## Decision
When `HomeAssistantClient.GetShoppingListItemsAsync()` receives `404 Not Found` from `/api/shopping_list`, Lucia should fall back to `todo.get_items` instead of surfacing the 404.

## Fallback selection
1. Prefer `todo.shopping_list` when present.
2. Otherwise prefer a todo entity whose entity id or `friendly_name` normalizes to `shopping list`.
3. If Home Assistant exposes exactly one todo entity, use that as the shopping list fallback.
4. If no reasonable todo-backed shopping list can be identified, rethrow the original 404.

## Rationale
This keeps native Home Assistant shopping list behavior unchanged while allowing CalDAV and other todo-backed shopping lists to load in Lucia without special user configuration. The heuristic stays conservative enough to avoid silently picking an arbitrary list when multiple unrelated todo entities exist.
