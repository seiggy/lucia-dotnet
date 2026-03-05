"""Track A2A conversation context/task IDs mapped to HA conversation IDs."""
from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import Optional


@dataclass
class TrackedConversation:
    """A tracked A2A conversation mapping."""

    context_id: str
    task_id: Optional[str] = None
    expires_at: float = field(default_factory=lambda: time.monotonic() + 300.0)


class ConversationTracker:
    """Maps HA conversation_id → A2A contextId/taskId with TTL expiration.

    Entries auto-expire after `ttl_seconds` (default 5 minutes) to prevent
    unbounded growth from voice conversations.
    """

    def __init__(self, ttl_seconds: float = 300.0) -> None:
        """Initialize with configurable TTL."""
        self._ttl = ttl_seconds
        self._entries: dict[str, TrackedConversation] = {}

    def get(self, conversation_id: str) -> Optional[TrackedConversation]:
        """Get tracked conversation, returning None if expired or missing."""
        self._prune_expired()
        return self._entries.get(conversation_id)

    def store(
        self,
        conversation_id: str,
        context_id: str,
        task_id: Optional[str] = None,
    ) -> None:
        """Store or update a conversation mapping, resetting the TTL."""
        self._entries[conversation_id] = TrackedConversation(
            context_id=context_id,
            task_id=task_id,
            expires_at=time.monotonic() + self._ttl,
        )

    def remove(self, conversation_id: str) -> None:
        """Remove a conversation mapping."""
        self._entries.pop(conversation_id, None)

    def _prune_expired(self) -> None:
        """Remove all expired entries."""
        now = time.monotonic()
        expired = [k for k, v in self._entries.items() if v.expires_at <= now]
        for k in expired:
            del self._entries[k]
