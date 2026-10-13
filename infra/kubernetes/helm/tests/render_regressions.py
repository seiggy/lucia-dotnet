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
assert 'DataProvider__Store: "PostgreSQL"' in default.stdout
assert 'mongo' not in default.stdout.lower()
assert 'name: lucia-postgres' in default.stdout and 'kind: StatefulSet' in default.stdout
assert 'nc -z "$POSTGRES_HOST" "$POSTGRES_PORT"' in default.stdout
assert 'POSTGRES_HOST' in default.stdout and 'value: "lucia-postgres"' in default.stdout
assert 'ConnectionStrings__luciaconfig' in default.stdout
assert 'Password=' in default.stdout.split("kind: Secret", 1)[1].split("---", 1)[0]

custom_port = render("bundledPostgres.service.port=5433")
assert custom_port.returncode == 0, custom_port.stderr
secret = custom_port.stdout.split("kind: Secret", 1)[1].split("---", 1)[0]
assert "Port=5433;" in secret and "Port=5432;" not in secret
assert 'value: "5433"' in custom_port.stdout

postgres = [
    "postgres.enabled=true", "bundledPostgres.enabled=false",
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
assert len(deployments) == 1, "Expected only the AgentHost deployment"
deployment = deployments[0]
assert 'DataProvider__Store: "PostgreSQL"' in config
assert "ConnectionStrings__lucia" not in config

assert 'nc -z "$POSTGRES_HOST" "$POSTGRES_PORT"' in deployment
for consumer in deployments:
    for database, key in keys.items():
        marker = f"- name: ConnectionStrings__{database}\n"
        assert marker in consumer, f"{database} missing from a database consumer deployment"
        fragment = consumer.split(marker, 1)[1].split("- name:", 1)[0]
        assert "secretKeyRef:" in fragment and 'name: "lucia-db"' in fragment
        assert f'key: "{key}"' in fragment
assert result.stdout.count('secretKeyRef:') == 3
assert not any("name: lucia-postgres" in document and "kind: StatefulSet" in document for document in documents), "external mode must not render the bundled server"

for override, message in (
    ("bundledPostgres.enabled=true", "cannot both be true"),
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
