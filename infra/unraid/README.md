# Unraid Community Apps

Unraid requires one XML template per container. Lucia therefore ships three linked templates:

- [`lucia-postgres.xml`](../../templates/lucia-postgres.xml)
- [`lucia-redis.xml`](../../templates/lucia-redis.xml)
- [`lucia.xml`](../../templates/lucia.xml)

## Install

Create the user-defined bridge network before installing any template. Each template selects it through `<Network>lucia</Network>`.

Run this once in the Unraid terminal:

```bash
docker network create lucia
mkdir -p /mnt/cache/appdata/lucia/postgres-init /mnt/cache/appdata/lucia/models
curl -fsSL https://raw.githubusercontent.com/seiggy/lucia-dotnet/master/infra/unraid/init.sql \
  -o /mnt/cache/appdata/lucia/postgres-init/init.sql
chown -R 1100:1100 /mnt/cache/appdata/lucia/models
```

If your cache pool is not named `cache`, change the template paths before installing.

Install the templates in this order:

1. **Lucia-PostgreSQL**. Set a long database password containing only letters and numbers.
2. **Lucia-Redis**.
3. **Lucia**. Replace `CHANGE_ME` in all three PostgreSQL connection strings with the same database password.

Open `http://<unraid-ip>:7233` and complete the setup wizard.

PostgreSQL reads `init.sql` only when its data directory is empty. To repair an existing installation that started without it, create the missing databases before starting Lucia:

```bash
docker exec Lucia-PostgreSQL createdb -U lucia luciaconfig
docker exec Lucia-PostgreSQL createdb -U lucia luciatraces
docker exec Lucia-PostgreSQL createdb -U lucia luciatasks
```
