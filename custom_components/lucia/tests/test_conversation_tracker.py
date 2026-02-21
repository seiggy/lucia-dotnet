"""Tests for ConversationTracker and A2A Task/Message parsing."""
import time
from unittest.mock import patch

from custom_components.lucia.conversation_tracker import ConversationTracker


def test_store_and_retrieve():
    """Store an entry and retrieve it."""
    tracker = ConversationTracker(ttl_seconds=60.0)
    tracker.store("conv-1", context_id="ctx-abc", task_id="task-xyz")

    entry = tracker.get("conv-1")
    assert entry is not None
    assert entry.context_id == "ctx-abc"
    assert entry.task_id == "task-xyz"


def test_missing_key_returns_none():
    """Retrieving a non-existent key returns None."""
    tracker = ConversationTracker()
    assert tracker.get("does-not-exist") is None


def test_ttl_expiration():
    """Entries expire after TTL."""
    tracker = ConversationTracker(ttl_seconds=0.1)
    tracker.store("conv-1", context_id="ctx-1")
    time.sleep(0.15)
    assert tracker.get("conv-1") is None


def test_store_resets_ttl():
    """Storing again resets the TTL."""
    tracker = ConversationTracker(ttl_seconds=0.2)
    tracker.store("conv-1", context_id="ctx-1")
    time.sleep(0.1)
    # Re-store to reset TTL
    tracker.store("conv-1", context_id="ctx-1-updated")
    time.sleep(0.15)
    # Should still be alive because we reset the TTL
    entry = tracker.get("conv-1")
    assert entry is not None
    assert entry.context_id == "ctx-1-updated"


def test_remove():
    """Remove deletes an entry."""
    tracker = ConversationTracker()
    tracker.store("conv-1", context_id="ctx-1")
    tracker.remove("conv-1")
    assert tracker.get("conv-1") is None


def test_remove_nonexistent_key_no_error():
    """Removing a key that doesn't exist should not raise."""
    tracker = ConversationTracker()
    tracker.remove("nope")  # Should not raise


def test_store_without_task_id():
    """Storing without task_id defaults to None."""
    tracker = ConversationTracker()
    tracker.store("conv-1", context_id="ctx-1")
    entry = tracker.get("conv-1")
    assert entry is not None
    assert entry.task_id is None


def test_prune_only_removes_expired():
    """Prune removes expired entries but keeps live ones."""
    tracker = ConversationTracker(ttl_seconds=0.1)
    tracker.store("expire-soon", context_id="ctx-1")
    time.sleep(0.15)
    tracker.store("still-alive", context_id="ctx-2")

    # Accessing any key triggers prune
    assert tracker.get("expire-soon") is None
    assert tracker.get("still-alive") is not None
