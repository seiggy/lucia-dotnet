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

postgres = [
    "postgres.enabled=true", "mongodb.enabled=false",
    "postgres.host=postgres-rw.example.svc", "postgres.existingSecret=lucia-db",
]
result = render(*postgres, "postgres.connectionStringKeys.luciaconfig=config-dsn")
assert result.returncode == 0, result.stderr
documents = result.stdout.split("---")
config = next(document for document in documents if "\nkind: ConfigMap\n" in document)
deployment = next(document for document in documents if "\nkind: Deployment\n" in document
                  and "wait-for-postgres" in document)
assert 'DataProvider__Store: "PostgreSQL"' in config
assert "ConnectionStrings__lucia" not in config
assert "wait-for-mongodb" not in deployment
assert 'nc -z "$POSTGRES_HOST" "$POSTGRES_PORT"' in deployment
assert 'key: "config-dsn"' in deployment
for database in ("luciaconfig", "luciatraces", "luciatasks"):
    fragment = deployment.split(f"- name: ConnectionStrings__{database}\n", 1)[1].split("- name:", 1)[0]
    assert "secretKeyRef:" in fragment and 'name: "lucia-db"' in fragment
assert result.stdout.count('secretKeyRef:') == 3
assert "kind: StatefulSet" not in "".join(
    document for document in documents if "mongodb" in document
)

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
