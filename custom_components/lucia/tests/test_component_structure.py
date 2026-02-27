"""Tests for Home Assistant custom component structure - Task 2.1."""
import json
from pathlib import Path

BASE = Path(__file__).resolve().parent.parent

def test_required_files_exist():
    """Test that all required component files exist."""
    required = [
        "manifest.json",
        "__init__.py",
        "const.py",
        "strings.json",
        "config_flow.py",
    ]
    for name in required:
        path = BASE / name
        assert path.exists(), f"Missing required file: {name}"

def test_manifest_valid_json_and_keys():
    """Test manifest.json has valid JSON and required keys."""
    manifest_path = BASE / "manifest.json"
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    required_keys = {"domain", "name", "version", "requirements"}
    assert required_keys.issubset(set(data.keys())), "manifest.json missing required keys"
    assert data.get("domain") == "lucia", "manifest.json domain must be 'lucia'"

def test_manifest_has_a2a_requirement():
    """Test manifest.json includes a2a requirement."""
    manifest_path = BASE / "manifest.json"
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    reqs = data.get("requirements", [])
    assert any("a2a" in r for r in reqs), "manifest.json should include an a2a requirement"

def test_const_has_expected_constants():
    """Test const.py defines expected constants."""
    const_text = (BASE / "const.py").read_text(encoding="utf-8")
    assert "DOMAIN" in const_text, "const.py should define DOMAIN"
    assert "CONF_API_KEY" in const_text, "const.py should define CONF_API_KEY"
    assert "A2A_WELL_KNOWN_PATH" in const_text, "const.py should define A2A_WELL_KNOWN_PATH"

def test_init_contains_entrypoints():
    """Test __init__.py implements required entry points."""
    init_text = (BASE / "__init__.py").read_text(encoding="utf-8")
    assert "async_setup_entry" in init_text, "__init__.py should implement async_setup_entry"
    assert "async_setup" in init_text, "__init__.py should implement async_setup"


def test_init_allows_empty_agent_catalog():
    """Test that __init__.py does not treat an empty agents list as an error.

    When the Lucia backend returns an empty [] catalog, the integration must
    set up successfully (not raise ConfigEntryNotReady). The old code had:
        if not agents or not isinstance(agents, list):
    which rejected an empty list.  The corrected code must only check isinstance.
    """
    init_text = (BASE / "__init__.py").read_text(encoding="utf-8")
    # The bad pattern must NOT appear
    assert "if not agents or not isinstance(agents, list):" not in init_text, (
        "__init__.py must not reject an empty agents list as invalid"
    )
    # The good pattern MUST appear — only the type check
    assert "if not isinstance(agents, list):" in init_text, (
        "__init__.py must validate agents is a list (but allow empty)"
    )
    # No unconditional ConfigEntryNotReady for empty catalog
    assert 'raise ConfigEntryNotReady(f"No agents available at' not in init_text, (
        "__init__.py must not raise ConfigEntryNotReady for an empty catalog"
    )
