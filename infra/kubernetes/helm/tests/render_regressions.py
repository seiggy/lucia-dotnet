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

# Plugin persistence: with plugins enabled (default) the deployment must run a
# seed-plugins init container using the marker-based seed script baked into the
# image, and the chart must render a ReadWriteOnce PVC for /app/plugins. The
# old inline no-clobber copy could never update or repair an already-seeded
# bundled plugin, so it must be gone from the rendered output.
default_documents = default.stdout.split("---")
default_deployment = next(document for document in default_documents if "\nkind: Deployment\n" in document)
assert "- name: seed-plugins" in default_deployment
assert "/usr/local/bin/seed-plugins.sh" in default_deployment
assert "cp -rn" not in default.stdout
default_pvc = next(document for document in default_documents if "name: lucia-plugins" in document and "\nkind: PersistentVolumeClaim\n" in document)
assert "ReadWriteOnce" in default_pvc and 'storage: "1Gi"' in default_pvc

# A supplied existing claim must be mounted without the chart creating a PVC.
existing_claim = render("plugins.existingClaim=external-claim")
assert existing_claim.returncode == 0, existing_claim.stderr
claim_documents = existing_claim.stdout.split("---")
assert not any("\nkind: PersistentVolumeClaim\n" in document for document in claim_documents), "existingClaim must suppress the chart's PVC"
claim_deployment = next(document for document in claim_documents if "\nkind: Deployment\n" in document)
assert "claimName: external-claim" in claim_deployment

# Disabling plugins removes the init container, the mount, and the PVC.
plugins_disabled = render("plugins.enabled=false")
assert plugins_disabled.returncode == 0, plugins_disabled.stderr
disabled_documents = plugins_disabled.stdout.split("---")
disabled_deployment = next(document for document in disabled_documents if "\nkind: Deployment\n" in document)
assert "- name: seed-plugins" not in disabled_deployment
assert "mountPath: /app/plugins" not in disabled_deployment
assert not any("\nkind: PersistentVolumeClaim\n" in document for document in disabled_documents)

# Rollout strategy vs the ReadWriteOnce plugins PVC: a single-replica pod can
# never attach the volume while the previous pod still holds it, so
# maxUnavailable: 0 deadlocks every upgrade (new pod waits for detach, old pod
# is held until the new one is ready). Single replica must allow the old pod to
# terminate first; multi-replica (RWX) can keep zero downtime.
assert "maxUnavailable: 1" in default_deployment
multi = render("lucia.replicaCount=2")
assert multi.returncode == 0, multi.stderr
multi_deployment = next(document for document in multi.stdout.split("---") if "\nkind: Deployment\n" in document)
assert "maxUnavailable: 0" in multi_deployment

print("Helm render regressions passed: defaults, external secrets, invalid values, and plugin persistence.")
