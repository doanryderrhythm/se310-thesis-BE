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
| `GET /api/products/{productId}` · `GET /api/products/slug/{slug}` | Public | None | `200` with product (`slug`, `material` text, `materialGroup {id,name,slug}`, `occasions[]`, `careInstructions`), images (see "Product images"), active variants (`sizeId`, `colorId`, `quantityOnHand`, `quantityReserved`, `availableQuantity`, `inStock`), `totalAvailableQuantity`, and `sizeChart` (the product's own chart, else the default chart of its category; rows have height/weight ranges). `404` if missing or not `active`. |
| `GET /api/cart` | Customer | None | `200` with `items`, `totalQuantity`, `subtotal`, `selectedQuantity`, `selectedSubtotal`. Each item has current price, `availableQuantity`, `isPurchasable`, `selected`, `priceAtAdd` and `priceChanged`; `subtotal` counts purchasable lines, `selectedSubtotal` only the ticked ones (what checkout should use). |
| `POST /api/cart/items` | Customer | `{"variantId":1,"quantity":2}` (quantity 1–99) | `200` with the updated cart; quantities add up on one line per variant. `400` validation, `404` unknown/inactive variant, `409` if the total would exceed available stock (`availableQuantity` in body). Stock is not reserved. |
| `PATCH /api/cart/items/{variantId}` | Customer | `{"quantity"?:3,"selected"?:false}` (at least one) | `200` with the cart. `400` for quantity outside 1–99 or an empty body, `404` if the line is not in the cart, `409` above available stock (`availableQuantity`). |
| `POST /api/cart/merge` | Customer | `{"items":[{"variantId":1,"quantity":2}]}` (≤ 50 lines) | Call once after login to merge the cart a visitor built locally. Quantities add to the account cart, capped at stock. `200 {cart, results:[{variantId, requested, added, quantity, status}]}`; `status` ∈ `added`, `capped`, `out_of_stock`, `unavailable`, `conflict`. |
| `DELETE /api/cart/items/{variantId}` | Customer | None | `200` with the updated cart, `404` if the line is not in the cart. |
| `GET /api/products/{productId}/reviews?page=1&pageSize=10` | Public | pageSize 1–50 | `200` with published reviews, `totalCount`, `averageRating`. |
| `GET /api/customers/me/reviewable-items` | Customer | None | Products the customer bought in a **delivered** order and has not reviewed yet (one per product, from the latest such order): `{orderCode, orderItemId, productId, productName, imageUrl, sku, size, color, deliveredAt}`. |
| `POST /api/products/{productId}/reviews` | Customer | `{"rating":5,"comment":"10-500 characters"}` | `201` (published immediately, linked to the order item). `409 {code:"REVIEW_NOT_PURCHASED"}` if the customer has no delivered order with the product, `409` if already reviewed. |
| `GET /api/customers/me/reviews` | Customer | None | `200` with the customer's own reviews. |
| `DELETE /api/customers/me/reviews/{reviewId}` | Customer | None | `204`; `404` if not the customer's review. |
| `DELETE /api/admin/reviews/{reviewId}` | Admin | None | `204`; `404` if missing. |

A review needs a delivered purchase of the product; `order_item_id` is stored.

## Storefront catalog APIs (public)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/categories` | Active categories `{categoryId, name, slug, description, displayOrder, productCount}` (active products only) |
| `GET /api/materials` · `GET /api/occasions` | Active materials (chất liệu: cotton, denim, …) and occasions (công dụng: đi làm, đi chơi, đi học, …) `{id, name, slug, description, displayOrder, productCount}` for the filter checkboxes |
| `GET /api/products?q=&category=&size=&material=&occasion=&minPrice=&maxPrice=&inStock=&sort=&page=&pageSize=` | Product listing, **active products of active categories only**. `category` = id or slug. `size`, `material`, `occasion` may repeat (`size=M&size=L`) or be comma separated; materials/occasions take slug or id; values within one group are OR, different groups are AND. Size, price range and `inStock=true` must all hold on the **same active variant**. `q` ignores case and accents and matches name + description (every word must match). `sort` = `newest` (default), `price_asc`, `price_desc`, `name_asc`. `pageSize` default 12, max 48. Returns `{items, page, pageSize, totalCount}`; an item is `{productId, slug, name, categoryId, categoryName, priceMin, priceMax, image, hoverImage, colors[], sizes[], inStock, material, materialGroup, occasions[], createdAt}` |
| `GET /api/products/{id}` · `GET /api/products/slug/{slug}` | Detail (see above) |
| `GET /api/size-charts/{id}` | A size chart with its rows |
| `GET /api/products/{id}/size-suggestion?heightCm=&weightKg=` | Suggests a size from the chart's height/weight ranges: `{suggestedSize, matchType: exact\|closest\|none, inStock, candidates[]}`; height 50–250, weight 10–250, at least one of them |
| `GET /api/storefront/policies` | Shipping fee (30.000đ, free from 500.000đ after discount), return window (7 days), delivery attempts, payment methods, bank-transfer account (from `Store__Bank*`), store name/address/phone, image standard |
| `GET /api/media-standard` | The product image standard (below) |
| `GET /api/products/search?name=&categoryId=` | Older name search (max 50, no paging); now returns **active products only**. Prefer `GET /api/products?q=` |

Validation keys: `q`, `minPrice`, `maxPrice`, `sort`, `page`, `heightCm`, `weightKg`.

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

Customer checkout is available through `POST /api/orders` (also available as
`POST /api/orders/purchase`). It uses `CouponService.QuoteAsync` /
`RedeemAsync` to price and atomically consume a coupon, and
`InventoryService.ReserveAsync` to reserve stock before creating the order
documents in `Features/Orders`. The admin side is already wired: cancelling an
order calls `CouponService.ReleaseAsync` and releases the reservation, and
moving an order to `delivered` calls
`VoucherIssuer.IssueSpendVouchersAsync`, so `spend` campaigns (e.g.
`THANTHIET`) fire automatically. The **my orders** and customer **cancel
order** endpoints are not implemented yet.

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
product slugs, locations, …), then `02-seed-products.js` (refreshes the catalog and adds category `display_order`, the materials and
occasions lists, product material/occasion/care/search text, image order and size-chart height/weight
ranges; legacy `inactive` statuses become `hidden`), `02a-seed-locations.js` (the province /
district / ward catalog; required for addresses), `03-seed-campaigns.js` and optionally
`04-seed-demo-data.js` (see "Demo data" below), using the same `mongosh`
command as above. Rebuild the API image afterwards, and start Mailpit with
`docker compose up -d`. Set `Store__*` and `RateLimit__AuthPerMinute` through `.env` (see
`.env.example`). For a real (non-demo) admin, promote a registered
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
| `GET /api/admin/dashboard?from=&to=&groupBy=` | One call for the home screen (default: last 30 days, `groupBy` default `day`): `kpis`, `counters {ordersNeedingAction, pendingOrders, pendingReturns, lowStockCount}`, `series`, `recentOrders` (5), `lowStock` (5 lowest-stock SKUs), `definitions` |
| `GET /api/admin/reports?from=&to=&groupBy=day\|week\|month\|quarter\|year` | KPIs + `series` + `comparison` (the previous period of the same length with `changePercent`) + `topProducts` (10 by units) + `lowStock` (20) + `definitions`. Longest range: `day` 366 days, `week` 731 days, `month`/`quarter`/`year` 1827 days (5 years) |
| `GET /api/admin/reports/export?from=&to=&groupBy=` | The same series as a CSV download (UTF-8 with BOM for Excel): `period,label,start,end,orders,delivered_orders,cancelled_orders,gross_revenue,merchandise_revenue,shipping_revenue,discount_amount,refund_amount,net_revenue,units_sold,average_order_value` plus a `TOTAL` row |

A series point is `{period, label, start, end, orderCount, deliveredCount, cancelledCount, grossRevenue, merchandiseRevenue, shippingRevenue, discountAmount, refundAmount, netRevenue, unitsSold, averageOrderValue}`.
`period` is the key (`2026-11-23`, ISO week `2026-W47`, `2026-11`, `2026-Q4`, `2026`), `label` is
ready-to-show Vietnamese (`Tuần 47/2026 (17/11 – 23/11)`, `Tháng 11/2026`, `Quý 4/2026`,
`Năm 2026`); `start`/`end` are the full calendar span of the bucket (the first and last bucket of
a range may be only partly inside it; only orders inside the range are counted). Every bucket of the range
appears, empty ones as zeros.

KPI definitions (also returned in `definitions` so the UI can show them):
`orderCount` = orders **placed** in the range (any status); `deliveredCount`,
`grossRevenue` (sum of `total_amount`, discount applied, shipping included) and
`unitsSold` = orders **delivered** in the range (by `delivered_at`);
`merchandiseRevenue + shippingRevenue = grossRevenue` (goods after discount vs. shipping fee), `averageOrderValue = grossRevenue ÷ deliveredCount`,
`cancelledCount` = orders placed in the range that are now cancelled;
`refundAmount` = return refunds paid in the range (by refund date; refunds of
cancelled or returned-to-shop orders are not subtracted because those orders
never counted as revenue); `netRevenue = grossRevenue − refundAmount`.
`lowStock` = active variants with `available ≤ lowStockThreshold`.

### Products and catalog (A02–A05)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/admin/products?q=&categoryId=&materialId=&occasionId=&status=&sort=newest\|name\|price&page=&pageSize=` | Table: `{productId, name, slug, categoryId, categoryName, materialId, materialName, status, thumbnailUrl, variantCount, priceMin, priceMax, totalAvailable, createdAt, updatedAt}`. `q` matches name or SKU; `status` = `draft`, `active`, `hidden` |
| `POST /api/admin/products` | Create everything in one request (below). `201` with the detail. `400` field errors |
| `GET /api/admin/products/{id}` | Edit form: info (`materialId`, `materialName`, `materialDetail`, `occasions[{id,name}]`, `careInstructions`), `images[]`, `variants[]` with `onHand`, `reserved`, `available`, `lowStockThreshold`, `hasOrders`, and `warnings[]` (fewer than 3 photos, photo not 3:4) |
| `PATCH /api/admin/products/{id}` | Partial update of `name`, `slug`, `categoryId`, `description`, `materialDetail` (alias `material`), `materialId` (`0` clears), `occasionIds` (replaces the list), `careInstructions`, `basePrice`, `sizeChartId` (`0` detaches the chart). Does not touch status, images or variants |
| `PATCH /api/admin/products/{id}/status` | `{"status":"draft\|active\|hidden"}`. Activating needs a description ≥ 20 chars, a material, ≥ 1 occasion, ≥ 1 image and ≥ 1 active variant, otherwise `409 {message}` says what is missing. Only `active` products show on the storefront |
| `PUT /api/admin/products/{id}/images` | Replace the whole gallery `{"images":[{mediaId \| url, altText?, colorId?, sortOrder?, isPrimary?, focalX?, focalY?}]}`; the image flagged `isPrimary` (else the lowest `sortOrder`) comes first and is the thumbnail; an active product keeps ≥ 1 image. Response has `warnings` |
| `POST /api/admin/uploads/images` | `multipart/form-data` field `file` → `201 {mediaId, url, width, height, aspectRatio, srcset{w400,w800,w1200}, warnings[]}`. See "Product images" |
| `GET /api/media/{file}` | Public, serves uploaded images (long cache) |
| `GET/POST /api/admin/materials` · `GET/PATCH/DELETE /api/admin/materials/{id}` · same for `/api/admin/occasions` | Managed lists `{id, name, slug, description, displayOrder, status(active\|hidden), productCount}`; names unique (accent/case-insensitive); `DELETE` is `409` while a product uses it (hide it instead) |
| `POST /api/admin/products/{id}/variants` · `PATCH /api/admin/variants/{variantId}` · `DELETE /api/admin/variants/{variantId}` | Add (`sku, sizeId, colorId, price?, status?, initialOnHand?, lowStockThreshold?`), edit (`sku, price, status`), delete a variant. Each returns the refreshed product detail. `409` for a duplicate size+colour or SKU, for deleting a variant that is in an order (hide it instead) and for removing the last active variant of an active product |
| `GET/POST /api/admin/categories` · `GET/PATCH/DELETE /api/admin/categories/{id}` | Categories `{categoryId, name, slug, description, displayOrder, status(active\|hidden), productCount}`; list takes `q`, `status`; `DELETE` is `409` while products or size charts use it |
| `GET/POST /api/admin/sizes` · `PATCH/DELETE /api/admin/sizes/{id}` · `GET/POST /api/admin/colors` · `PATCH/DELETE /api/admin/colors/{id}` | Dropdown data and maintenance (`name`, `displayOrder` for sizes, `hexCode` like `#1A2B3C`, `status`); `DELETE` is `409` while variants (or images, for colors) use it |
| `GET /api/admin/size-charts?categoryId=` · `GET/PUT/DELETE /api/admin/size-charts/{id}` · `POST /api/admin/size-charts` · `POST /api/admin/size-charts/{id}/duplicate` | Size charts `{name, description, categoryId, isDefault, items:[{sizeLabel, chestCm, waistCm, hipCm, lengthCm, notes, heightMinCm, heightMaxCm, weightMinKg, weightMaxKg, displayOrder}]}`. `PUT` keeps the id of rows whose label stays; `isDefault` (needs a category; one per category) is the chart shown for products without their own; `duplicate` copies a chart (`{"name"?}`); `DELETE` is `409` while a product uses the chart |

`POST /api/admin/products` body (`status` defaults to `draft`; `slug` is
generated from the name when omitted; SKUs are unique across the shop):

```json
{
  "name": "Áo thun cotton basic", "slug": null, "categoryId": 1,
  "description": "At least 20 characters when the product is active.",
  "materialId": 1, "materialDetail": "Cotton 95%, spandex 5%", "occasionIds": [1, 3],
  "careInstructions": "Giặt nước mát, không dùng chất tẩy.",
  "basePrice": 199000, "sizeChartId": null, "status": "draft",
  "images": [{"mediaId": 12, "altText": "Mặt trước", "colorId": 1, "isPrimary": true, "focalX": 0.5, "focalY": 0.3}],
  "variants": [{"sku": "TSHIRT-M-BLK", "sizeId": 2, "colorId": 1, "price": 199000, "status": "active",
                "lowStockThreshold": 5}]
}
```

New variants start with **zero stock** (`initialOnHand` above 0 is rejected): stock comes in
through goods receipts. Validation keys to translate: `name`, `slug`, `categoryId`, `description`,
`materialId`, `materialDetail`, `occasionIds`, `careInstructions`, `basePrice`, `sizeChartId`, `status`, `images` (list of messages),
`variants` (list of messages), `sku`, `price`, `displayOrder`, `hexCode`,
`items`. Product `status` values: `draft`, `active`, `hidden` (older rows with
`inactive` are returned as `hidden`).

### Inventory (A06)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/admin/inventory?q=&lowStock=true&status=active\|hidden&page=&pageSize=` | Rows `{variantId, sku, productId, productName, size, color, variantStatus, onHand, reserved, available, lowStockThreshold, isLow, updatedAt, averageCost, stockValue}`; `available = onHand − reserved`; `isLow = available ≤ threshold` |
| `POST /api/admin/inventory/{variantId}/adjustments` | **Decrease only** (damaged, lost, stocktake): `{"reasonCode":"damaged\|lost\|stocktake\|other","quantity":5,"reason":"10–300 chars"}`. `type:"increase"` is `400`: stock goes up through a goods receipt. `409` if more than the available stock |
| `PATCH /api/admin/inventory/{variantId}` | `{"lowStockThreshold": 5}` |
| `GET /api/admin/inventory/{variantId}/movements?page=&pageSize=` | History `{type, deltaOnHand, deltaReserved, onHandAfter, reservedAfter, reason, refType, refId, actor, createdAt}`; types `initial`, `receipt` (goods receipt), `adjust_decrease` (older rows may have `adjust_increase`), `reserve`, `release`, `ship`, `restock`, `compensation` |

Stock rules (shared with checkout): placing an order reserves stock;
cancelling before hand-over releases it; handing over to the carrier
(`packing → shipping`) takes it off the shelf; goods that pass inspection
(returned-to-shop orders, customer returns) go back on the shelf.

### Goods receipts: suppliers and stock in (nhập hàng)

Stock goes **up** only through a goods receipt (it goes down automatically when goods are handed over to the
carrier, and comes back when returned goods pass inspection). A receipt is a draft until completed.

| Endpoint | Purpose |
| --- | --- |
| `GET/POST /api/admin/suppliers` · `GET/PATCH/DELETE /api/admin/suppliers/{id}` | Suppliers `{name, phone, email, address, taxCode, note, status(active\|hidden), receiptCount}`; list takes `q`, `status`; name unique; `DELETE` is `409` once it has receipts (hide it) |
| `GET /api/admin/goods-receipts?q=&status=&supplierId=&from=&to=&page=` | Table `{receiptCode (PN-1001), supplierName, supplierInvoiceNo, receivedAt, itemCount, totalQuantity, totalAmount, status}`; `q` = code, supplier invoice or supplier name; `status` = `draft`, `completed`, `cancelled` |
| `POST /api/admin/goods-receipts` · `PUT /api/admin/goods-receipts/{code}` | Create / edit a **draft**: `{supplierId, supplierInvoiceNo?, receivedAt?, note?, items:[{variantId, quantity 1–100000, unitCost ≥ 0}]}` (1–200 lines, a variant once; the same supplier invoice number may be on one live receipt only). Stock is not touched yet |
| `GET /api/admin/goods-receipts/{code}` | Detail with supplier, lines (SKU, size, colour, `currentOnHand`), totals, `canEdit`/`canComplete`/`canCancel` |
| `POST /api/admin/goods-receipts/{code}/complete` | Adds every line to the stock exactly once (safe against double clicks and crashes), writes an `inventory_movements` row per line (`type:"receipt"`) and updates each variant's moving-average `averageCost`. `409` if already completed/cancelled or a line cannot be applied (everything is rolled back) |
| `POST /api/admin/goods-receipts/{code}/cancel` | `{"reason":"10–300 chars"}`, drafts only |
| `GET /api/admin/goods-receipts/{code}/print` | The receipt as a self-contained A4 HTML page (store, supplier, lines, totals, amount in Vietnamese words, signature boxes; has a Print button). Fetch it with the token and open it in a new window, then `window.print()` or "Save as PDF" |

Store name, address and phone on the printout come from `Store__Name`, `Store__Address`, `Store__Phone`.
Validation keys: `supplierId`, `supplierInvoiceNo`, `receivedAt`, `items` (list), `reason`, `name`, `phone`, `email`, `taxCode`.

### Product images

Standard (also at `GET /api/media-standard`): portrait **3:4** frame like most fashion shops, at least **900 × 1200 px**
(recommended 1200 × 1600), JPEG / PNG / WebP up to **20 MB**, at least 1 photo to publish and 3 recommended
(main view, another angle, a material/detail close-up). The upload really decodes the file (not the name or
content type): it is rotated by its EXIF orientation, converted to sRGB, stripped of metadata and stored as
WebP (master up to 2400 px on the long side, never enlarged) plus 400 / 800 / 1200 px wide versions
(`srcset.w400`, `w800`, `w1200`; only those smaller than the master exist). A photo that is not 3:4 is accepted
with a `warnings[]` entry: set `focalX`/`focalY` (0–1) on the image so the frame crops around the subject
(`object-position: {focalX*100}% {focalY*100}%`). Images from older data (`/images/products/…`) have no
size/srcset (`width`, `height`, `srcset` are `null`). Display sizes per place are listed in
`feat-catalog-purchasing-orders-reports_TODO.md`.

### Orders (A07, A08)

| Endpoint | Purpose |
| --- | --- |
| `POST /api/orders` · `POST /api/orders/purchase` | Customer purchase. Optional header `Idempotency-Key` prevents duplicate orders for retries. Request: `{"addressId"?,"items"?,"code"?,"couponCode"?,"userCouponId"?,"paymentMethod":"cod\|bank_transfer","customerNote"?}`. If `items` is omitted, uses the customer's active cart; `addressId` is also optional when a saved address exists (the default address is preferred). Items use 1–50 distinct variants, quantity 1–99. Creates status `pending`, reserves stock, consumes one coupon, clears the cart, and returns `201` with the order snapshot. COD payment status is `unpaid`; bank transfer is `pending`. `400` for address, coupon or input errors; `409` for unavailable stock or a coupon changed/reached its limit |
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

### Customer: my orders

(Placing an order is built elsewhere; these read and cancel the orders that exist.)

| Endpoint | Purpose |
| --- | --- |
| `GET /api/customers/me/orders?status=&q=&from=&to=&page=&pageSize=` | Own orders, newest first (default 10, max 50). `status` = a tab (`all`, `active` = pending/confirmed/packing, `shipping`, `delivered`, `cancelled`, `delivery_failed` = failed + returning + returned) or a comma list of statuses. Item: `{orderCode, status, placedAt, totalAmount, paymentMethod, paymentStatus, itemCount, preview[3], canCancel}` |
| `GET /api/customers/me/orders/counts` | `{all, active, shipping, delivered, cancelled, deliveryFailed, byStatus}` for the tab badges |
| `GET /api/customers/me/orders/{orderCode}` | Items (with `imageUrl`, `reviewable`), totals, `couponCode`, payment, `timeline` (status changes), `deliveryAttempts`, `canCancel`, `canReturn`, `returnDeadline`, and `bankTransfer {bankName, accountNumber, accountName, transferContent, amount}` while a transfer is awaiting payment. Someone else's order is `404` |
| `POST /api/customers/me/orders/{orderCode}/cancel` | `{"reason":"10–300 chars"}`. Only `pending`/`confirmed` (`409` otherwise). Releases the reserved stock and the coupon use, a paid order becomes `refund_pending`, writes the history and notifies |

Order items written by the checkout should also set `image_url_snapshot` (the image shown when ordering).

### Account: change password, rate limit

`POST /api/customers/me/password` and `POST /api/admin/me/password` take
`{"currentPassword","newPassword"}` (8 characters .. 72 bytes, different from the current one) and return a fresh
auth response (`accessToken`, `user`); every token issued before the change stops working (`401`). Errors:
`400` with `currentPassword` / `newPassword`. `POST /api/auth/login`, `/api/auth/register` and the two password
endpoints allow `RateLimit__AuthPerMinute` calls per minute per client address (default 10, compose 20,
`0` = off), then answer `429 {message}` with `Retry-After`.

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
(stock and coupon usage consistent with them), 8 returns, 6 reviews, three suppliers with five goods receipts
(two completed, two drafts, one cancelled; the completed ones add stock and set average costs) and a
default address per customer. Log in with password **`Demo@12345`**: admin
`0900000001`, customers `0900000011`–`0900000018`. The script replaces its own
previous data and resets every variant's stock to the base seed values, so rerun
it only on a development database. The script refuses to run (and changes
nothing) if one of its phone numbers already belongs to a non-demo account, so it
can never overwrite a real user. Uploaded images are stored in the
`api_uploads` Docker volume (`Uploads__Path`, default `./uploads` outside Docker).

## Frontend integration checklist

> Chi tiết từng fetch (mô tả, API, response mẫu, lỗi, luồng, cách làm trong code FE) nằm trong
> [`feat-voucher-admin-dashboard_TODO.md`](feat-voucher-admin-dashboard_TODO.md). Phần bổ sung
> (catalog công khai, chất liệu/công dụng, bảng size, nhập hàng, báo cáo theo kỳ, đơn của khách, chuẩn ảnh)
> nằm trong [`feat-catalog-purchasing-orders-reports_TODO.md`](feat-catalog-purchasing-orders-reports_TODO.md).

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