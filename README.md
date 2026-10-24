# se310-thesis-BE

## Local MongoDB

The repository currently contains no backend application code, so this Compose
setup provides a local MongoDB instance. Its initialization script creates the
collections, validation rules, and indexes from the supplied database schema.

1. Copy `.env.example` to `.env` and set local development passwords:

   ```powershell
   Copy-Item .env.example .env
   ```

2. Start MongoDB with `docker compose up -d`.
3. Check readiness with `docker compose ps`.
4. Stop it with `docker compose down`. Database files remain in the
   `mongodb_data` Docker volume.

MongoDB and Mongo Express listen only on localhost. Open Mongo Express at
`http://localhost:8081` and sign in with `MONGO_EXPRESS_USERNAME` and
`MONGO_EXPRESS_PASSWORD` from `.env`. Change both example passwords before
starting the services.

The default MongoDB connection string is:

```text
mongodb://root:change-me-for-local-development@localhost:27017/fashion-store-data?authSource=admin
```

Replace the username, password, port, or database in the connection string if
you changed the corresponding values in `.env`. Percent-encode special
characters in credentials when putting them in a URI. MongoDB applies
initialization scripts only when creating a new data volume; for an existing
volume, run the script manually:

```powershell
docker compose exec mongodb mongosh --username root --authenticationDatabase admin fashion-store-data /docker-entrypoint-initdb.d/01-schema.js
```

The command prompts for the root password. Replace `fashion-store-data` if you
changed `MONGO_DATABASE`. MongoDB does not enforce foreign keys; the schema
preserves relationship fields and adds indexes for them. Numeric primary keys
are uniquely indexed but are not auto-incremented; the application must assign
their values. These services are for local development only; do not expose
them publicly or reuse their credentials in production.