# se310-thesis-BE

## Local MongoDB

This repository includes a local MongoDB instance and an ASP.NET Core API for
phone/password authentication. MongoDB initialization creates the collections,
validation rules, and indexes from the supplied database schema, then seeds the
product catalog and variants from `mongo-init/mock-data`.

1. Copy `.env.example` to `.env` and set local development passwords and a
   random JWT signing key of at least 32 characters:

   ```powershell
   Copy-Item .env.example .env
   ```

2. Start MongoDB and the API with `docker compose up --build -d`.
3. Check readiness with `docker compose ps`.
4. The API listens on `http://localhost:8080` by default. Register and log in
   with the following JSON endpoints:

   - `POST /api/auth/register` — `{"fullName":"Jane Doe","phone":"0123456789","password":"example-password"}`
   - `POST /api/auth/login` — `{"phone":"0123456789","password":"example-password"}`

   Both successful responses include a one-hour JWT access token and a
   password-free user object. Phone numbers must contain exactly 10 digits.
   Passwords must be at least 8 characters and no more than 72 UTF-8 bytes.
   Registration rejects duplicate phone numbers. The API stores BCrypt
   password hashes, not plaintext passwords.

5. Stop it with `docker compose down`. Database files remain in the
   `mongodb_data` Docker volume.

MongoDB, Mongo Express, and the API listen only on localhost. Open Mongo
Express at `http://localhost:8081` and sign in with `MONGO_EXPRESS_USERNAME` and
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

For an existing volume, apply the schema first if needed, then seed or refresh
the catalog with:

```powershell
docker compose exec mongodb mongosh --username root --authenticationDatabase admin fashion-store-data /docker-entrypoint-initdb.d/02-seed-products.js
```

Both commands prompt for the root password. Replace `fashion-store-data` if you
changed `MONGO_DATABASE`. The seed uses numeric IDs derived from the fixture IDs
and upserts documents, so it can be rerun without duplicating catalog records.
MongoDB does not enforce foreign keys; the schema preserves relationship fields
and adds indexes for them. Numeric primary keys are uniquely indexed but are
not auto-incremented; the application must assign their values. These services
are for local development only; do not expose them publicly or reuse their
credentials in production.