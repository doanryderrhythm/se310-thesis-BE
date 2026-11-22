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
   - `GET /api/products/search?name=áo%20thun` — find products whose names
     contain the search text (case-insensitive). The response includes product
     details, images, and variants with size, color, price, and inventory.
     Add `categoryId` to filter by category as well, for example
     `/api/products/search?name=áo&categoryId=1`. The `name` parameter is
     required; `categoryId` is optional.
   - `GET /api/customers/me/addresses` — fetch delivery addresses belonging
     to the authenticated customer. Send the login access token as a bearer
     token in the `Authorization` header.
   - `POST /api/customers/me/addresses` — add a delivery address for the
     authenticated customer with `recipientName`, `recipientPhone`,
     `addressLine`, `ward`, `district`, and `province`. Optionally set
     `isDefault` to `true`. Recipient phone numbers must contain exactly
     10 digits.
   - `PUT /api/customers/me/addresses/{addressId}` — replace an address
     belonging to the authenticated customer. Send the same fields as when
     adding an address; the endpoint returns `404` if the address does not
     belong to that customer. Setting `isDefault` to `true` clears that flag
     from the customer's other addresses.
   - `DELETE /api/customers/me/addresses/{addressId}` — delete an address
     belonging to the authenticated customer. Returns `204` when deleted or
     `404` if the address does not belong to that customer.

   Successful registration and login responses include a one-hour JWT access
   token and a password-free user object. Phone numbers must contain exactly
   10 digits. Passwords must be at least 8 characters and no more than 72
   UTF-8 bytes. Registration rejects duplicate phone numbers. The API stores
   BCrypt password hashes, not plaintext passwords.

5. Stop it with `docker compose down`. Database files remain in the
   `mongodb_data` Docker volume.

## Product detail, cart, and review APIs

| Endpoint | Auth | Request | Success / errors |
| --- | --- | --- | --- |
| `GET /api/products/{productId}` | Public | None | `200` with product, images, active variants (`quantityOnHand`, `quantityReserved`, `availableQuantity`, `inStock`), `totalAvailableQuantity`, and `sizeChart`. `404` if missing or inactive. |
| `GET /api/cart` | Customer | None | `200` with `items`, `totalQuantity`, `subtotal`. Each item has current price, `availableQuantity`, and `isPurchasable`; `subtotal` counts purchasable lines only. |
| `POST /api/cart/items` | Customer | `{"variantId":1,"quantity":2}` (quantity 1–99) | `200` with the updated cart; quantities add up on one line per variant. `400` validation, `404` unknown/inactive variant, `409` if the total would exceed available stock (`availableQuantity` in body). Stock is not reserved. |
| `DELETE /api/cart/items/{variantId}` | Customer | None | `200` with the updated cart, `404` if the line is not in the cart. |
| `GET /api/products/{productId}/reviews?page=1&pageSize=10` | Public | pageSize 1–50 | `200` with published reviews, `totalCount`, `averageRating`. |
| `POST /api/products/{productId}/reviews` | Customer | `{"rating":5,"comment":"10-500 characters"}` | `201` (published immediately). `409` if the customer already reviewed the product. |
| `GET /api/customers/me/reviews` | Customer | None | `200` with the customer's own reviews. |
| `DELETE /api/customers/me/reviews/{reviewId}` | Customer | None | `204`; `404` if not the customer's review. |
| `DELETE /api/admin/reviews/{reviewId}` | Admin | None | `204`; `404` if missing. |

Reviews are not yet tied to delivered orders (`order_item_id` stays null).

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