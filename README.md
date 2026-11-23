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

   - `POST /api/auth/register` — `{"fullName":"Jane Doe","phone":"0123456789","password":"example-password","email":"jane@example.com"}`
     (`email` is optional; see "Email" below)
   - `POST /api/auth/login` — `{"phone":"0123456789","password":"example-password"}`
   - `GET /api/products/search?name=áo%20thun` — find products whose names
     contain the search text (case-insensitive). The response includes product
     details, images, and variants with size, color, price, and inventory.
     Add `categoryId` to filter by category as well, for example
     `/api/products/search?name=áo&categoryId=1`. The `name` parameter is
     required; `categoryId` is optional.
   - `GET /api/customers/me` — fetch the authenticated customer's profile
     (`userId`, `fullName`, `phone`, `email`, `role`, `createdAt`, `updatedAt`).
   - `PATCH /api/customers/me` — update `fullName` and/or `email`. Omitted
     fields are unchanged; send `"email": ""` to remove the email. At least
     one field is required.
   - `GET /api/customers/me/addresses` — fetch delivery addresses belonging
     to the authenticated customer. Send the login access token as a bearer
     token in the `Authorization` header.
   - `POST /api/customers/me/addresses` — add a delivery address for the
     authenticated customer with `recipientName`, `recipientPhone`,
     `addressLine` and the location as codes from `/api/locations/*`
     (`provinceCode`, `districtCode`, `wardCode`); see "Data formats" below.
     Optionally set `isDefault` to `true`.
   - `PUT /api/customers/me/addresses/{addressId}` — replace an address
     belonging to the authenticated customer. Send the same fields as when
     adding an address; the endpoint returns `404` if the address does not
     belong to that customer. Setting `isDefault` to `true` clears that flag
     from the customer's other addresses.
   - `DELETE /api/customers/me/addresses/{addressId}` — delete an address
     belonging to the authenticated customer. Returns `204` when deleted or
     `404` if the address does not belong to that customer.

   Successful registration and login responses include a one-hour JWT access
   token and a password-free user object. Phone numbers are Vietnamese mobile
   numbers (see "Data formats"). Passwords must be at least 8 characters and no
   more than 72 UTF-8 bytes. Registration rejects duplicate phone numbers. The
   API stores BCrypt password hashes, not plaintext passwords.

5. Stop it with `docker compose down`. Database files remain in the
   `mongodb_data` Docker volume.

### Data formats

Input is normalized on the server, so the UI may be lenient but must always show
the values the API returns.

- **Phone numbers** (registration, login, admin accounts, recipient phones):
  Vietnamese mobile numbers. Accepted as typed — `0912 345 678`, `0912.345.678`,
  `+84 912 345 678`, `84912345678` — and always stored and returned as 10 digits
  `0912345678`. The number must start with `03`, `05`, `07`, `08` or `09`
  (`400` with key `phone` / `recipientPhone` otherwise). The same number written
  differently is still a duplicate (`409` on register). Login also accepts
  accounts created before the prefix rule (any 10 digits).
- **Names and text**: surrounding and repeated spaces are removed
  (`"  Nguyễn   Văn A "` → `"Nguyễn Văn A"`). `fullName` and `recipientName`:
  2–100 characters; `addressLine`: 5–255 characters; email: lower-cased.
- **Addresses** use the administrative hierarchy **province/city → district →
  ward/commune** (3 levels, the structure before the July 2025 reorganisation:
  63 provinces, 696 districts, 10,051 wards; data from provinces.open-api.vn).
  The UI fills three dependent dropdowns from the public endpoints below and
  sends the **codes**; the server checks that the ward belongs to the district
  and the district to the province and stores the catalog's canonical names
  next to the codes. Error keys: `province`, `district`, `ward`,
  `recipientName`, `recipientPhone`, `addressLine`. For older clients the names
  (`province`, `district`, `ward`) are still accepted and matched to the
  catalog (accents, case and prefixes such as "Quận"/"Phường" are ignored); an
  unmatched or ambiguous name is rejected.

| Endpoint | Response |
| --- | --- |
| `GET /api/locations/provinces` | `[{code, name}]` (63) |
| `GET /api/locations/provinces/{provinceCode}/districts` | `[{code, name}]`, `404` for an unknown province |
| `GET /api/locations/districts/{districtCode}/wards` | `[{code, name}]`, `404` for an unknown district |

Address responses now also carry `provinceCode`, `districtCode`, `wardCode`.
Only one address per customer can be the default: saving an address with
`isDefault: true` clears the flag on the others (also on `POST`).

### Email

`email` is optional on register and profile update. It is trimmed and stored
lower-case; it is **not** unique and **not** used to log in (login stays
phone + password). Customers with an email also receive voucher emails.
Responses that describe a user (`user` in auth responses, the profile, admin
customer lists) include `email`, `null` when none is set.

`docker compose` starts [Mailpit](https://mailpit.axllent.org/) as a local SMTP
sink; the API sends through it (`Smtp__Host=mailpit`, `Smtp__Port=1025`). Open
`http://localhost:8025` to read every email the API sends — nothing leaves the
machine. Emails are queued in memory and sent in the background, so a slow or
failing mail server never slows down or fails a request. Without `Smtp__Host`
the API only logs "email skipped". Other settings: `Smtp__From`,
`Smtp__FromName`, `Smtp__User`, `Smtp__Password`, `Smtp__EnableSsl`.

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

## Promotions: campaigns, vouchers, notifications

Terminology: a **campaign** is the admin-facing promotion (name, schedule,
how it is handed out); it owns exactly one **coupon** (code + discount rule).

- **Discount types** (`discountType`): `percent` (1–100, optional
  `maxDiscountAmount` cap), `fixed` (VND off), `free_shipping` (waives the
  shipping fee, which is 30.000đ unless `subtotal − discount ≥ 500.000đ`).
- **Distribution**: `public_code` — anyone can type the code (limited by
  `usageLimit` total and `perUserLimit`); `issued` — the coupon is put in
  chosen customers' **voucher wallet** (one per customer, valid
  `validDaysAfterIssue` days, never past the campaign end).
- **Trigger** (issued only): `signup` — issued automatically the moment a
  customer registers; `spend` — issued automatically once the customer's
  lifetime spend reaches `triggerMinSpent` VND (e.g. 500.000); `manual` — an
  admin issues it to a customer segment. Public codes use trigger `none`.
- **Lifetime spend** = sum of `total_amount` over the customer's `delivered`
  orders (cancelled, pending and returned orders do not count); the threshold
  is inclusive (`>=`). Any number of `spend` campaigns may run at once (e.g. a
  500.000 tier and a 1.000.000 tier); each issues at most once per customer.
- **One welcome voucher**: a new customer receives exactly one `signup`
  voucher. Two `signup` campaigns cannot be active over overlapping periods
  (`409 {message, conflictingCampaignId}` when creating, resuming or
  re-dating one; drafts are exempt until resumed).
- **No "claim" step.** Issuing puts the voucher straight into the customer's
  wallet, ready to apply. The in-app **notification** (and **email**, if the
  customer has one) is only an announcement; the customer picks the voucher at
  checkout (`POST /api/coupons/applicable`).
- One coupon per order. The discount base is the subtotal before shipping,
  limited to the campaign's categories when `appliesTo` is `categories`.
  `minimumOrderAmount` is checked against that base. Amounts are whole VND.
- Campaign status shown to admins: `draft`, `scheduled` (active but not yet
  started), `active`, `paused`, `ended` (manually ended or past its end date).
  Pausing blocks everything, including vouchers already in wallets. Ending
  stops new issuing and public-code use, but vouchers already issued stay
  usable until their own expiry.

### Admin endpoints (role `admin`)

| Endpoint | Request | Success / errors |
| --- | --- | --- |
| `GET /api/admin/campaigns?status=&trigger=&q=&page=&pageSize=` | `status`: draft/scheduled/active/paused/ended; `trigger`: none/signup/manual/spend; `q` matches name or code; pageSize 1–100 | `200 {items,page,pageSize,totalCount}`; each item = campaign + `coupon` + `stats` |
| `POST /api/admin/campaigns` | Body below | `201` with the campaign. `400` field errors, `409` code already exists |
| `GET /api/admin/campaigns/{id}` | — | `200` campaign, `404` |
| `PATCH /api/admin/campaigns/{id}` | Any of `name`, `description`, `startsAt`, `endsAt`, `triggerMinSpent`, `coupon{…}`; omitted fields are unchanged; `usageLimit`/`maxDiscountAmount` of `0` mean "none" | `200`. Once a voucher was issued or used the rules are frozen: only `name`, `description`, a **later** `endsAt` and a `usageLimit` ≥ uses can change, otherwise `409 {message, lockedFields[]}`. `409` on ended campaigns. `trigger`/`distribution` cannot be changed (duplicate instead) |
| `POST /api/admin/campaigns/{id}/pause` · `/resume` · `/end` | — | `200` campaign. `resume` also activates a `draft`. `409` for an invalid transition (e.g. ending an ended campaign, resuming after the end date). `end` is permanent |
| `POST /api/admin/campaigns/{id}/duplicate` | `{"code":"NEWCODE","startsAt":"…","endsAt":"…","name":"optional"}` | `201` new campaign in `draft` with the same rules |
| `POST /api/admin/campaigns/{id}/issue` | Allowed on `issued` campaigns with trigger `manual` or `spend` (for `spend` it backfills customers who already crossed the threshold before the campaign existed). Two choices: `{"segment":"all_customers"}` or `{"segment":"min_spent","minSpent":1000000}` (lifetime spend = sum of `total_amount` over the customer's `delivered` orders; cancelled/pending orders do not count) | `200 {matched,issued,skipped}` (skipped = already had it, so re-running is safe). Only `issued` + `manual` campaigns that are running; otherwise `409`. Admin accounts and inactive customers are never matched. `400` `segment`/`minSpent` |
| `GET /api/admin/campaigns/{id}/stats` | — | `200 {issued,available,used,expired,revoked,usageCount,usageLimit,totalDiscount,redemptionRate}` |
| `GET /api/admin/campaigns/{id}/usages?page=&pageSize=` | — | `200` page of `{couponUsageId,userId,fullName,orderId,discountAmount,usedAt,status}` (`applied`/`released`) |
| `GET /api/admin/campaigns/{id}/recipients?status=&page=&pageSize=` | `status`: available/used/expired/revoked | `200` page of `{userCouponId,userId,fullName,phone,status,issuedAt,expiresAt,usedAt}` |

`POST /api/admin/campaigns` body (`trigger` defaults to `none` for
`public_code` and `manual` for `issued`; `trigger: "spend"` also needs
`triggerMinSpent`, otherwise it is ignored; `draft: true` saves without
activating; dates are ISO-8601, UTC recommended):

```json
{
  "name": "Chào mừng thành viên mới",
  "description": "Giảm 10% đơn đầu, tối đa 50.000đ",
  "distribution": "issued",
  "trigger": "signup",
  "startsAt": "2026-12-01T00:00:00Z",
  "endsAt": "2027-03-01T00:00:00Z",
  "draft": false,
  "coupon": {
    "code": "WELCOME10",
    "discountType": "percent",
    "discountValue": 10,
    "maxDiscountAmount": 50000,
    "minimumOrderAmount": 300000,
    "appliesTo": "all",
    "categoryIds": [],
    "usageLimit": null,
    "perUserLimit": 1,
    "validDaysAfterIssue": 30
  }
}
```

`code` is 4–30 characters (letters, digits, `-`, `_`) and stored upper-case;
`appliesTo` is `all` or `categories` (then `categoryIds` must exist).
`usageLimit` empty or `0` = unlimited. `perUserLimit` applies to public codes
only (issued vouchers are always one per customer).

### Customer / public endpoints

| Endpoint | Auth | Request | Success / errors |
| --- | --- | --- | --- |
| `GET /api/coupons` | Public | — | `200` running public codes that still have uses left: `[{coupon{couponId,code,name,description,discountType,discountValue,maxDiscountAmount,minimumOrderAmount,appliesTo,categoryIds,summary},expiresAt,remainingUses}]`. `summary` is ready-made Vietnamese text |
| `GET /api/customers/me/vouchers?status=` | Customer | `status`: `available` (default), `used`, `expired`, `all` | `200` array of `{userCouponId,status,issuedAt,expiresAt,usedAt,coupon{…}}` |
| `POST /api/coupons/applicable` | Customer | `{"items":[{"variantId":1,"quantity":2}]}` (same item rules as validate) | `200` array — the checkout "choose a coupon" list: the customer's wallet vouchers (`source:"wallet"`, apply with `userCouponId`) and running public codes (`source:"public"`, apply with `coupon.code`), each judged against this cart: `{source,userCouponId,coupon{…},expiresAt,eligible,reason,discountAmount,shippingDiscount}`. Eligible first, best saving first; ineligible ones stay listed with `reason` = a `COUPON_*` code (e.g. `COUPON_MIN`) so the UI can grey them out |
| `POST /api/coupons/validate` | Customer | `{"code":"BLACKFRIDAY","items":[{"variantId":1,"quantity":2}]}` or `{"userCouponId":5,"items":[…]}` — exactly one of `code`/`userCouponId`; 1–50 items, quantity 1–99 | `200 {coupon,userCouponId,subtotal,eligibleSubtotal,discountAmount,shippingFee,shippingDiscount,total}`; prices come from the database, never from the client. `400` with `errors.couponCode` or `errors.items` |
| `GET /api/customers/me/notifications?unreadOnly=&page=&pageSize=` | Customer | pageSize 1–100 | `200 {items,page,pageSize,totalCount,unreadCount}`; item = `{notificationId,type,title,body,data,isRead,readAt,createdAt}`. Voucher notifications have `type` `voucher_issued` and `data {userCouponId,campaignId}` |
| `GET /api/customers/me/notifications/unread-count` | Customer | — | `200 {unreadCount}` (for the bell badge) |
| `POST /api/customers/me/notifications/{id}/read` | Customer | — | `204` (also if already read), `404` if not the customer's |
| `POST /api/customers/me/notifications/read-all` | Customer | — | `204` |

A typed `code` for an `issued` coupon only works for customers who hold that
voucher. Coupon failures come back as
`400 {"errors":{"couponCode":["<CODE>"]}}` with one of:
`COUPON_INVALID` (unknown, paused, draft, or not yours), `COUPON_NOT_STARTED`,
`COUPON_EXPIRED`, `COUPON_LIMIT` (no uses left), `COUPON_USED` (already used
by this customer), `COUPON_MIN` (below the minimum order amount),
`COUPON_NOT_ELIGIBLE` (no cart line matches the coupon's categories).

Orders: the **customer** "place order" / "my orders" / cancel endpoints do not exist
yet, so nothing creates orders or consumes a coupon at checkout. When they are
built they must reuse the shared services: `CouponService.QuoteAsync` /
`RedeemAsync` (price + atomically use up the coupon), `InventoryService.ReserveAsync`
(reserve stock) and the order documents in `Features/Orders`. The admin side is
already wired: cancelling an order calls `CouponService.ReleaseAsync` and
releases the reservation, and moving an order to `delivered` calls
`VoucherIssuer.IssueSpendVouchersAsync`, so `spend` campaigns (e.g. `THANTHIET`)
fire automatically. Until customer checkout exists, `usageCount` / `used` /
`totalDiscount` stay at 0 for real traffic (the demo seed fills them).

### Seed data and existing volumes

`mongo-init/03-seed-campaigns.js` seeds seven sample campaigns (all running at
the time of writing so they can be tested right away):

| Code | Campaign | Kind | Rule |
| --- | --- | --- | --- |
| `WELCOME10` | Chào mừng thành viên mới | issued on `signup` | 10% up to 50.000đ, orders from 300.000đ, 30 days |
| `BLACKFRIDAY` | Black Friday 2026 | public code, 500 uses, ends 2026-12-01 | 15% up to 150.000đ, orders from 500.000đ |
| `FREESHIP` | Tri ân khách hàng | issued `manual` | free shipping, any order, 30 days |
| `1111` | Siêu sale 11.11 | public code, 1.111 uses, until 2026-11-30 | 11% up to 111.000đ, orders from 300.000đ |
| `2412` | Quà Giáng sinh 24/12 | issued `manual`, until 2026-12-26 | 50.000đ off, orders from 400.000đ, 30 days |
| `0101` | Chào năm mới 1/1 | issued `manual`, until 2027-01-15 | 10% up to 100.000đ, orders from 300.000đ, 30 days |
| `THANTHIET` | Khách hàng thân thiết | issued automatically at lifetime spend ≥ 500.000đ (`spend`), until 2027-06-30 | 15% up to 120.000đ, orders from 500.000đ, 60 days |

On an existing volume run, in order, `01-schema.js` (adds the new collections
and fields: campaigns, vouchers, notifications, returns, inventory movements,
product slugs, locations, …), then `02-seed-products.js` (refreshes the catalog
and adds category `display_order`), `02a-seed-locations.js` (the province /
district / ward catalog; required for addresses), `03-seed-campaigns.js` and optionally
`04-seed-demo-data.js` (see "Demo data" below), using the same `mongosh`
command as above. Rebuild the API image afterwards, and start Mailpit with
`docker compose up -d`. For a real (non-demo) admin, promote a registered
account with `db.users.updateOne({phone:"…"},{$set:{role:"admin"}})` in Mongo
Express or `mongosh`, then log in again. The API creates its indexes and back-fills
product slugs and category order on startup.

## Admin dashboard APIs

Everything below needs an `admin` access token (`401` without a token, `403` for
a customer). Lists use `?page=1&pageSize=20` (pageSize 1–100) and return
`{items, page, pageSize, totalCount}`; validation errors are
`400 {"errors":{"field":["message"]}}`, conflicts `409 {message}`. Dates in
query strings are `yyyy-MM-dd` Vietnam days (UTC+7); stored/returned instants
are UTC ISO-8601.

### Dashboard and reports (A01, A14)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/admin/dashboard?from=&to=` | One call for the home screen (default: last 30 days, max 366): `kpis {orderCount, deliveredCount, grossRevenue, refundAmount, netRevenue, unitsSold}`, `counters {ordersNeedingAction, pendingOrders, pendingReturns, lowStockCount}`, `series` (one point per day `{period, orderCount, deliveredCount, grossRevenue, refundAmount, netRevenue, unitsSold}`), `recentOrders` (5), `lowStock` (5 lowest-stock SKUs), `definitions` |
| `GET /api/admin/reports?from=&to=&groupBy=day\|week` | Same KPIs + `series` grouped by day or week (weeks start Monday, `period` = first day), `topProducts` (10 by units: `{productId, productName, sku, units, revenue}`), `lowStock` (20), `definitions` |
| `GET /api/admin/reports/export?from=&to=&groupBy=` | The same series as a CSV download (UTF-8 with BOM for Excel): `period,orders,delivered_orders,gross_revenue,refund_amount,net_revenue,units_sold` plus a `TOTAL` row |

KPI definitions (also returned in `definitions` so the UI can show them):
`orderCount` = orders **placed** in the range (any status); `deliveredCount`,
`grossRevenue` (sum of `total_amount`, discount applied, shipping included) and
`unitsSold` = orders **delivered** in the range (by `delivered_at`);
`refundAmount` = return refunds paid in the range (by refund date; refunds of
cancelled or returned-to-shop orders are not subtracted because those orders
never counted as revenue); `netRevenue = grossRevenue − refundAmount`.
`lowStock` = active variants with `available ≤ lowStockThreshold`.

### Products and catalog (A02–A05)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/admin/products?q=&categoryId=&status=&sort=newest\|name\|price&page=&pageSize=` | Table: `{productId, name, slug, categoryId, categoryName, status, thumbnailUrl, variantCount, priceMin, priceMax, totalAvailable, createdAt, updatedAt}`. `q` matches name or SKU; `status` = `draft`, `active`, `hidden` |
| `POST /api/admin/products` | Create everything in one request (below). `201` with the detail. `400` field errors |
| `GET /api/admin/products/{id}` | Edit form: info, `images[]`, `variants[]` with `onHand`, `reserved`, `available`, `lowStockThreshold`, `hasOrders` |
| `PATCH /api/admin/products/{id}` | Partial update of `name`, `slug`, `categoryId`, `description`, `material`, `basePrice`, `sizeChartId` (`0` detaches the chart). Does not touch status, images or variants |
| `PATCH /api/admin/products/{id}/status` | `{"status":"draft\|active\|hidden"}`. Activating needs a description ≥ 20 chars, ≥ 1 image and ≥ 1 active variant, otherwise `409 {message}` says what is missing. Only `active` products show on the storefront |
| `PUT /api/admin/products/{id}/images` | Replace the whole gallery `{"images":[{url, altText?, colorId?, sortOrder?}]}`; the first image (lowest `sortOrder`) is the thumbnail; an active product keeps ≥ 1 image |
| `POST /api/admin/uploads/images` | `multipart/form-data` field `file` (JPEG/PNG/WebP, ≤ 5 MB, checked by content not by name) → `201 {url:"/api/media/<id>.png"}`; put that `url` into an image entry |
| `GET /api/media/{file}` | Public, serves uploaded images (long cache) |
| `POST /api/admin/products/{id}/variants` · `PATCH /api/admin/variants/{variantId}` · `DELETE /api/admin/variants/{variantId}` | Add (`sku, sizeId, colorId, price?, status?, initialOnHand?, lowStockThreshold?`), edit (`sku, price, status`), delete a variant. Each returns the refreshed product detail. `409` for a duplicate size+colour or SKU, for deleting a variant that is in an order (hide it instead) and for removing the last active variant of an active product |
| `GET/POST /api/admin/categories` · `GET/PATCH/DELETE /api/admin/categories/{id}` | Categories `{categoryId, name, slug, description, displayOrder, status(active\|hidden), productCount}`; list takes `q`, `status`; `DELETE` is `409` while products or size charts use it |
| `GET/POST /api/admin/sizes` · `PATCH /api/admin/sizes/{id}` · `GET/POST /api/admin/colors` · `PATCH /api/admin/colors/{id}` | Dropdown data and maintenance (`name`, `hexCode` like `#1A2B3C`, `status`); no delete, hide instead |
| `GET /api/admin/size-charts?categoryId=` · `GET/PUT/DELETE /api/admin/size-charts/{id}` · `POST /api/admin/size-charts` | Size charts `{name, description, categoryId, items:[{sizeLabel, chestCm, waistCm, hipCm, lengthCm, notes}]}`; `PUT` replaces the rows; `DELETE` is `409` while a product uses the chart |

`POST /api/admin/products` body (`status` defaults to `draft`; `slug` is
generated from the name when omitted; SKUs are unique across the shop):

```json
{
  "name": "Áo thun cotton basic", "slug": null, "categoryId": 1,
  "description": "At least 20 characters when the product is active.",
  "material": "Cotton 100%", "basePrice": 199000, "sizeChartId": null, "status": "draft",
  "images": [{"url": "/api/media/<uploaded>.jpg", "altText": "Mặt trước", "colorId": 1, "sortOrder": 0}],
  "variants": [{"sku": "TSHIRT-M-BLK", "sizeId": 2, "colorId": 1, "price": 199000, "status": "active",
                "initialOnHand": 20, "lowStockThreshold": 5}]
}
```

Validation keys to translate: `name`, `slug`, `categoryId`, `description`,
`material`, `basePrice`, `sizeChartId`, `status`, `images` (list of messages),
`variants` (list of messages), `sku`, `price`, `displayOrder`, `hexCode`,
`items`. Product `status` values: `draft`, `active`, `hidden` (older rows with
`inactive` are returned as `hidden`).

### Inventory (A06)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/admin/inventory?q=&lowStock=true&status=active\|hidden&page=&pageSize=` | Rows `{variantId, sku, productId, productName, size, color, variantStatus, onHand, reserved, available, lowStockThreshold, isLow, updatedAt}`; `available = onHand − reserved`; `isLow = available ≤ threshold` |
| `POST /api/admin/inventory/{variantId}/adjustments` | `{"type":"increase\|decrease","quantity":5,"reason":"10–300 chars"}`; `409` if on-hand would drop below what open orders reserved |
| `PATCH /api/admin/inventory/{variantId}` | `{"lowStockThreshold": 5}` |
| `GET /api/admin/inventory/{variantId}/movements?page=&pageSize=` | History `{type, deltaOnHand, deltaReserved, onHandAfter, reservedAfter, reason, refType, refId, actor, createdAt}`; types `initial`, `adjust_increase`, `adjust_decrease`, `reserve`, `release`, `ship`, `restock` |

Stock rules (shared with the future checkout): placing an order reserves stock;
cancelling before hand-over releases it; handing over to the carrier
(`packing → shipping`) takes it off the shelf; goods that pass inspection
(returned-to-shop orders, customer returns) go back on the shelf.

### Orders (A07, A08)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/admin/orders?q=&status=&paymentStatus=&paymentMethod=&from=&to=&page=&pageSize=` | Table; `q` = order code or recipient phone; `status` may be a comma list (e.g. `confirmed,packing`) |
| `GET /api/admin/orders/counts` | `{byStatus:{…}, tabs:{new, toPack, shipping, failed, returning}}` for the tab badges |
| `GET /api/admin/orders/{orderCode}` | Snapshot, customer, shipping address, items, payment, `deliveryAttempts`, `history`, **`allowedTransitions`** (render exactly these buttons), `canConfirmPaid`, `canConfirmRefunded`, `maxDeliveryAttempts` |
| `POST /api/admin/orders/{orderCode}/transitions` | `{"toStatus": "...", "note"?, "reason"?, "failureReason"?, "attemptedAt"?, "inspection"?, "carrier"?, "trackingCode"?}`; `409 {message, allowed[]}` for an invalid move |
| `POST /api/admin/orders/{orderCode}/payment` | `{"action":"confirm_paid\|confirm_refunded","reference"?}` |

Statuses: `pending → confirmed → packing → shipping → delivered`;
`pending/confirmed/packing → cancelled` (needs `reason`, 10–300 chars);
`packing → shipping` needs a COD order or a bank transfer already `paid`;
`shipping → delivery_failed` (needs `failureReason` ∈ `unreachable`,
`wrong_address`, `reschedule`, `customer_refused`, `other` and a 10–300 char
`note`); `delivery_failed → shipping` (redeliver, at most 2 attempts, never
after `customer_refused`) or `→ returning_to_seller → returned_to_seller`
(needs `inspection` = `passed|failed`; `passed` restocks). `delivered`,
`cancelled`, `returned_to_seller` are final. Payment statuses: `unpaid`
(COD), `pending` (transfer), `paid`, `refund_pending`, `refunded`; COD becomes
`paid` on delivery, cancelling or returning a paid order makes it
`refund_pending`. Every move is written to `history` and the customer gets an
in-app notification (`type: "order_status"`).

### Returns (A09, A10) and the customer side

| Endpoint | Who | Purpose |
| --- | --- | --- |
| `GET /api/customers/me/orders/{orderCode}/returnable-items` | Customer | `{eligible, ineligibleReason, returnDeadline, items:[{orderItemId, purchasedQuantity, returnedQuantity, remainingQuantity, unitPrice, refundPerUnit}]}` |
| `POST /api/customers/me/returns` | Customer | `{"orderCode","reason","description","items":[{"orderItemId","quantity"}]}` → `201` return (`RTN-<n>`, status `requested`). Only `delivered` orders within 7 days of delivery (`409` otherwise); `reason` ∈ `wrong_size`, `wrong_item`, `defective`, `changed_mind`, `other`; description 10–1000 chars; quantities may not exceed what is left (rejected returns free their quantity). Refund estimate = line total × qty ÷ purchased qty (shipping not refunded) |
| `GET /api/customers/me/returns` · `GET /api/customers/me/returns/{returnCode}` | Customer | Own returns and timeline |
| `GET /api/admin/returns?status=&q=&from=&to=&page=` · `GET /api/admin/returns/counts` | Admin | Table (`status` comma list; `q` = return/order code, customer name/phone) and tab badges |
| `GET /api/admin/returns/{returnCode}` | Admin | Detail with items, estimate, timeline and **`allowedActions`** |
| `POST /api/admin/returns/{returnCode}/transitions` | Admin | `{"action":"approve\|reject\|receive\|inspect_pass\|inspect_fail\|refund","reason"?,"note"?,"refundReference"?}` |

Return flow: `requested → (approve) awaiting_return → (receive) received →
(inspect_pass) refund_pending → (refund) refunded`; `reject` (from `requested`)
and `inspect_fail` (from `received`) end in `rejected` and need a 10–300 char
`reason`; `refund` needs `refundReference`. `inspect_pass` restocks the items.
Each step notifies the customer (`type: "return_status"`).

### Customers and reviews (A12, A13)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/admin/customers?q=&status=active\|locked&page=&pageSize=` | Table incl. `email`, `orderCount` (non-cancelled orders) and `totalSpent` (delivered orders); `q` matches name, phone or email |
| `GET /api/admin/customers/{userId}` | Drawer: profile, addresses, `stats`, 5 `recentOrders`, `vouchers {available, used, expired}`, `reviewCount`, `statusReason` |
| `POST /api/admin/customers/{userId}/lock` · `/unlock` | `lock` takes `{"reason":"10–300 chars"}`. A locked customer cannot log in and their current token is rejected on its next use (`401`); order history is kept. `409` if already in that state |
| `GET /api/admin/reviews?q=&status=published\|hidden&rating=&productId=&page=` | Table with product, customer, rating, comment, status |
| `PATCH /api/admin/reviews/{id}/status` | `{"status":"published\|hidden","reason"?}`; the customer's rating/comment are never edited. (Customers' own reviews are still published immediately.) `DELETE /api/admin/reviews/{id}` still exists |

### Demo data

`mongo-init/04-seed-demo-data.js` (run after 01–03 including 02a) creates one admin and eight
customers (one locked), 45 orders spread over the last 60 days in every status
(stock and coupon usage consistent with them), 8 returns, 6 reviews and a
default address per customer. Log in with password **`Demo@12345`**: admin
`0900000001`, customers `0900000011`–`0900000018`. The script replaces its own
previous data and resets every variant's stock to the base seed values, so rerun
it only on a development database. The script refuses to run (and changes
nothing) if one of its phone numbers already belongs to a non-demo account, so it
can never overwrite a real user. Uploaded images are stored in the
`api_uploads` Docker volume (`Uploads__Path`, default `./uploads` outside Docker).

## Frontend integration checklist

Features the backend now exposes that still need UI work and API wiring in the
frontend (`src/api.ts`, base `/api`, `Authorization: Bearer <accessToken>`).
Validation errors arrive as `{"errors":{"<field>":["message"]}}`; add the
Vietnamese text for every field listed below to `fieldMessages`.

**Already available — keep wired**

- **Register and log in** — `POST /api/auth/register`, `POST /api/auth/login`.
  Register form gets an optional **email** field (`email` error key). Both
  return `{accessToken, expiresAt, user:{userId, fullName, phone, role, email}}`;
  `409` on register = phone already used; `401` on login = wrong credentials.
- **Fetch the personal profile after login** — `GET /api/customers/me`
  (now includes `email`); edit form calls `PATCH /api/customers/me` with
  `fullName` and/or `email` (`""` removes the email).
- **Fetch and add delivery addresses** — `GET /api/customers/me/addresses`,
  `POST /api/customers/me/addresses` (`recipientName`, `recipientPhone`,
  `addressLine`, `provinceCode`, `districtCode`, `wardCode`, `isDefault`);
  update/delete via `PUT`/`DELETE /api/customers/me/addresses/{addressId}`.
  The location is three dependent dropdowns (province → district → ward) filled
  from `GET /api/locations/provinces`, `…/provinces/{code}/districts`,
  `…/districts/{code}/wards`; reset the lower dropdowns when a higher one
  changes. Phone inputs may accept spaces/dots/+84 — show the normalized value
  the API returns.

**New — customer UI**

- **Notification bell** — poll `GET /api/customers/me/notifications/unread-count`
  for the badge; dropdown/page lists `GET /api/customers/me/notifications`,
  marks items with `POST …/{id}/read` and `POST …/read-all`. A
  `voucher_issued` item should open the voucher wallet (`data.userCouponId`).
- **Voucher wallet page** — `GET /api/customers/me/vouchers?status=available|used|expired`
  as three tabs; show `coupon.code`, `coupon.summary`, `expiresAt`.
- **Checkout coupon box** — no "claim" step. Call `POST /api/coupons/applicable`
  with the cart lines to render the pick list (wallet vouchers + public codes,
  best saving first, ineligible ones greyed out with their `reason`), plus a
  text input for typing a code. When the customer picks one, call
  `POST /api/coupons/validate` with `userCouponId` (wallet option) or `code`
  (public option / typed) and display `discountAmount`, `shippingDiscount`,
  `total`. Map every `couponCode` error
  code (`COUPON_INVALID`, `COUPON_NOT_STARTED`, `COUPON_EXPIRED`,
  `COUPON_LIMIT`, `COUPON_USED`, `COUPON_MIN`, `COUPON_NOT_ELIGIBLE`) and the
  `items` error to Vietnamese text. When the orders API lands, the same
  `code`/`userCouponId` is sent with the order.
- **Home/promo banner (optional)** — `GET /api/coupons` (public) to advertise
  running public codes.

**New — admin UI (the frontend has no admin screens yet)**

- **Campaign list** — `GET /api/admin/campaigns` with status/trigger filters,
  search `q`, paging; show `status`, dates, `stats.issued/used`,
  `stats.totalDiscount`.
- **Create campaign form** — `POST /api/admin/campaigns` (steps: info → discount
  rule → how to hand out → schedule/limits). Field error keys to translate:
  `name`, `description`, `distribution`, `trigger`, `triggerMinSpent`, `startsAt`, `endsAt`, `code`,
  `discountType`, `discountValue`, `maxDiscountAmount`, `minimumOrderAmount`,
  `appliesTo`, `categoryIds`, `usageLimit`, `perUserLimit`, `validDaysAfterIssue`.
  `409 {message}` means the code already exists.
- **Campaign detail** — `GET /api/admin/campaigns/{id}`, `…/stats`,
  `…/usages`, `…/recipients`; edit with `PATCH` (handle `409 {lockedFields}` by
  disabling those inputs); buttons for `pause`, `resume`, `end`, `duplicate`.
- **Issue vouchers to customers** — `POST /api/admin/campaigns/{id}/issue`, a
  two-option picker: "everyone" (`segment: "all_customers"`) or "customers who
  spent at least … VND" (`segment: "min_spent"` + `minSpent`); show
  `matched` / `issued` / `skipped`. Only offered for `issued` + `manual`
  campaigns that are running. New customers are covered automatically by the
  `signup` campaign and loyal customers by a `spend` campaign. When creating a
  campaign offer the trigger choice "when a customer registers" (`signup`),
  "when a customer's spending reaches … VND" (`spend` + `triggerMinSpent`) or
  "I will issue it myself" (`manual`); show a warning for
  `409 conflictingCampaignId` when activating a second `signup` one.
- **Customer list** (existing) — `GET /api/admin/customers` now also returns
  `email`.

**New — admin dashboard (A01–A14, see "Admin dashboard APIs")**

- **Dashboard `/admin`** — `GET /api/admin/dashboard`: five KPI cards, a bar chart
  from `series`, tables for `recentOrders` and `lowStock`, a "returns waiting"
  card from `counters.pendingReturns`; date range toolbar sends `from`/`to`.
- **Reports `/admin/reports`** — `GET /api/admin/reports`, "Export CSV" links to
  `/api/admin/reports/export` with the same query (send the bearer token: use a
  fetch + blob download, a plain link cannot add the header). Show `definitions`
  under the table.
- **Products** — list (`GET /api/admin/products`), create/edit form: upload each
  image with `POST /api/admin/uploads/images`, then `POST` / `PATCH` the product;
  variants table with add/edit/delete; show `hasOrders` to disable delete; status
  switch via `PATCH …/status` (handle the `409` message as a toast).
- **Categories, sizes/colours, size charts** — the CRUD endpoints in the table
  above; disable delete when `productCount > 0`.
- **Inventory** — table with low-stock badge (`isLow`), an "Adjust" modal
  (type, quantity, reason ≥ 10 chars), threshold edit, and a movements drawer.
- **Orders** — list with quick tabs from `/counts`; detail screen renders its
  buttons from `allowedTransitions` and opens a modal when `requiresReason`,
  `requiresFailureInfo` or `requiresInspection` is true; payment buttons from
  `canConfirmPaid` / `canConfirmRefunded`.
- **Returns** — list with tabs from `/counts`; detail screen buttons from
  `allowedActions`. **Customer account** needs "Return this order" on
  `/account/orders/{code}` using `returnable-items`, and "My returns" using
  `/api/customers/me/returns`.
- **Customers and reviews** — customer table + drawer (`GET /api/admin/customers/{id}`)
  with a lock/unlock confirm dialog (reason required); review table with
  publish/hide actions.

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