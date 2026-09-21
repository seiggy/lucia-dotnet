import os
import subprocess
from pathlib import Path

CHART = Path(__file__).resolve().parents[1]


def render(*values):
    command = [os.environ.get("HELM", "helm"), "template", "lucia", str(CHART)]
    for value in values:
        command.extend(["--set", value])
    return subprocess.run(command, capture_output=True, text=True, check=False)


default = render()
assert default.returncode == 0, default.stderr
assert 'DataProvider__Store: "MongoDB"' in default.stdout
assert 'mongodb://' in default.stdout
assert 'wait-for-postgres' not in default.stdout
assert 'secretKeyRef:' not in default.stdout

postgres = [
    "postgres.enabled=true", "mongodb.enabled=false",
    "postgres.host=postgres-rw.example.svc", "postgres.existingSecret=lucia-db",
]
keys = {
    "luciaconfig": "config-dsn",
    "luciatraces": "traces-dsn",
    "luciatasks": "tasks-dsn",
}
result = render(*postgres, *(f"postgres.connectionStringKeys.{name}={key}" for name, key in keys.items()))
assert result.returncode == 0, result.stderr
documents = result.stdout.split("---")
config = next(document for document in documents if "\nkind: ConfigMap\n" in document)
deployments = [document for document in documents if "\nkind: Deployment\n" in document
               and "configMapRef:" in document]
assert len(deployments) == 2, "Expected AgentHost and default-enabled Timer Agent deployments"
deployment = next(document for document in deployments if "wait-for-postgres" in document)
assert 'DataProvider__Store: "PostgreSQL"' in config
assert "ConnectionStrings__lucia" not in config
assert "wait-for-mongodb" not in deployment
assert 'nc -z "$POSTGRES_HOST" "$POSTGRES_PORT"' in deployment
for consumer in deployments:
    for database, key in keys.items():
        marker = f"- name: ConnectionStrings__{database}\n"
        assert marker in consumer, f"{database} missing from a database consumer deployment"
        fragment = consumer.split(marker, 1)[1].split("- name:", 1)[0]
        assert "secretKeyRef:" in fragment and 'name: "lucia-db"' in fragment
        assert f'key: "{key}"' in fragment
assert result.stdout.count('secretKeyRef:') == 6
assert "kind: StatefulSet" not in "".join(
    document for document in documents if "mongodb" in document
)

without_timer = render(*postgres, "a2aAgents.timerAgent.enabled=false")
assert without_timer.returncode == 0, without_timer.stderr
assert without_timer.stdout.count('secretKeyRef:') == 3
assert "name: lucia-timer-agent" not in without_timer.stdout

for override, message in (
    ("mongodb.enabled=true", "cannot both be true"),
    ("postgres.existingSecret=", "postgres.existingSecret is required"),
    ("postgres.host=", "postgres.host is required"),
    ("postgres.port=0", "postgres.port must be"),
    ("postgres.port=65536", "postgres.port must be"),
    ("postgres.port=5432.5", "postgres.port must be"),
    ("postgres.connectionStringKeys.luciaconfig=", "postgres.connectionStringKeys.luciaconfig is required"),
):
    invalid = render(*postgres, override)
    assert invalid.returncode != 0 and message in invalid.stderr, invalid.stdout + invalid.stderr

print("Helm render regressions passed: defaults, external secrets, and invalid values.")
