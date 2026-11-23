# [TODO] Tích hợp FE — nhánh `vantri/feat-voucher-admin-dashboard`

> **Dành cho người làm FE.** File này liệt kê **mọi fetch** mà BE đã làm xong trên nhánh này (PR #9) nhưng FE **chưa nối**, kèm: fetch gì, API nào, mô tả API, response thật, lỗi, luồng chạy và cách làm trong code FE hiện tại.
> Response mẫu lấy từ API đang chạy với dữ liệu demo (đã rút gọn: mảng chỉ giữ 1–2 phần tử). Tài liệu đầy đủ từng endpoint ở BE nằm trong `README.md` (mục "Admin dashboard APIs").
> Khi nối xong một mục: đổi `☐` thành `☑` và ghi tên người làm.

- Snapshot FE đã đối chiếu: `se310-thesis-FE@70a473e` (đã nối: đăng ký, đăng nhập, hồ sơ — chỉ `fullName`, danh sách/thêm địa chỉ, tìm kiếm sản phẩm).
- Chưa có API (không nằm trong tài liệu này): đặt đơn (báo giá + tạo đơn) — xem **Phần C**. Đơn của tôi / hủy đơn / danh sách sản phẩm công khai đã có ở nhánh sau.
- ⚠️ **Đã đổi ở nhánh `vantri/feat-catalog-purchasing-orders-reports`** (xem [`feat-catalog-purchasing-orders-reports_TODO.md`](feat-catalog-purchasing-orders-reports_TODO.md), mục 1): bỏ `initialOnHand` và tăng tồn thủ công (dùng phiếu nhập hàng), upload ảnh theo chuẩn mới (`mediaId`, 3:4, ≥ 900×1200), kích hoạt SP cần chất liệu + công dụng, báo cáo `groupBy` thêm month/quarter/year và `period` tuần là `2026-W47`, review cần đơn đã giao. Nếu mục bên dưới mâu thuẫn, **file mới thắng**.

---

## 0. Chạy thử local

1. Cập nhật BE: chạy lại các seed `01-schema.js`, `02-seed-products.js`, `02a-seed-locations.js`, `03-seed-campaigns.js`, `04-seed-demo-data.js` (lệnh `mongosh` xem README BE), build lại image API, `docker compose up -d`.
2. FE: `.env.local` có `VITE_DATA_SOURCE=api` và `VITE_API_BASE_URL=/api`; Vite đã proxy `/api → http://localhost:8080` (cả ảnh upload `/api/media/...`).
3. Tài khoản demo (mật khẩu **`Demo@12345`**):

| Vai trò | SĐT | Ghi chú |
|---|---|---|
| Admin | `0900000001` | Vào được mọi `/api/admin/*` |
| Khách | `0900000011` … `0900000017` | Có địa chỉ, đơn, trả hàng demo |
| Khách bị khóa | `0900000018` | Đăng nhập báo 401 |

4. Hộp thư thử email voucher: http://localhost:8025 (Mailpit). Mã khuyến mãi mẫu: `WELCOME10` (tự cấp khi đăng ký), `BLACKFRIDAY`, `1111` (nhập tay), `FREESHIP`, `2412`, `0101`, `THANTHIET` (admin phát / tự cấp khi chi tiêu ≥ 500.000đ).

---

## 1. Quy ước chung (đọc trước)

| Mục | Quy ước |
|---|---|
| Base URL | `/api` (cùng origin qua proxy, không có CORS). Trong `src/api.ts`: `baseUrl`. |
| Xác thực | Header `Authorization: Bearer <accessToken>`. Token sống **1 giờ**, không refresh. FE đã có `setToken`, `request(path, options, authenticated)`. |
| Phân quyền | `401` = chưa đăng nhập / token hết hạn / tài khoản bị khóa. `403` = đã đăng nhập nhưng sai vai trò (khách gọi `/api/admin/*`). |
| JSON | Tên field **camelCase**. Id là **số** → FE đổi sang `string` như đang làm (`String(dto.productId)`). |
| Tiền | VND, số nguyên (kiểu `number`). Hiển thị `vi-VN` (có sẵn `Price.tsx`). |
| Thời gian | Trả về UTC ISO-8601 có `Z`. Hiển thị theo `Asia/Ho_Chi_Minh` (`dd/MM/yyyy HH:mm`). Tham số ngày trên query là `yyyy-MM-dd` tính theo **ngày giờ Việt Nam**. |
| Phân trang | Query `?page=1&pageSize=20` (pageSize 1–100). Response `{ items, page, pageSize, totalCount }`. Số trang = `ceil(totalCount / pageSize)`. |
| Chuỗi rỗng | Field tùy chọn: bỏ hẳn hoặc `null`. Riêng `PATCH /customers/me` gửi `"email": ""` để **xóa** email. |
| `status`/enum | Mã tiếng Anh (bảng nhãn tiếng Việt ở Phụ lục B). |

### 1.1 Các loại lỗi

| Mã | Khi nào | Body | FE xử lý |
|---|---|---|---|
| `400` | Dữ liệu sai | `{ "errors": { "<field>": ["thông báo tiếng Anh"] } }` (ProblemDetails) | Tra `fieldMessages[<field>]` ra câu tiếng Việt, gắn vào ô nhập đúng field. **Không** hiện nguyên câu tiếng Anh. |
| `401` | Token thiếu/hết hạn/bị khóa; đăng nhập sai | rỗng | `request()` đã tự `logout()`; đăng nhập sai hiện "Số điện thoại hoặc mật khẩu không đúng". |
| `403` | Sai vai trò | rỗng | "Tài khoản không có quyền truy cập." (đã có) |
| `404` | Không có / không phải của bạn | rỗng hoặc `{message}` | Trang "không tìm thấy". |
| `409` | Xung đột nghiệp vụ (trùng, sai trạng thái, hết lượt…) | `{ "message": "…" }` (đôi khi kèm field phụ như `allowed[]`, `lockedFields[]`) | Hiện toast; xem từng mục. |

Mẫu `400`:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "fullName": [
      "Full name is required (2-100 characters)."
    ],
    "phone": [
      "Phone must be a Vietnamese mobile number: 10 digits starting with 03, 05, 07, 08 or 09."
    ],
    "password": [
      "Password must be at least 8 characters and no more than 72 UTF-8 bytes."
    ]
  }
}
```
Mẫu `409`:
```json
{
  "message": "SKU P07-S-0 already exists."
}
```

`request()` hiện đã gom `errors[field]` thành câu tiếng Việt từ `fieldMessages`, nên chỉ cần **bổ sung key mới vào `fieldMessages`** (Phụ lục A) và sửa key đã đổi nghĩa (`phone`, `recipientPhone`, `ward`, `district`, `province`).

### 1.2 Việc làm một lần ở lõi FE (làm trước khi nối từng màn)

- ☐ **Đưa `request()` ra dùng chung**: tách `request`, `json`, `ServiceError`, `fieldMessages` sang `src/http.ts` (hoặc `export` từ `api.ts`) để `src/adminApi.ts` và `src/customerApi.ts` dùng chung.
- ☐ **Session có `role` và `email`**: `CustomerDto` / `toUser()` hiện chỉ có `userId, fullName, phone, createdAt`. Thêm `role` ("customer"|"admin") và `email` (string|null) vì login/profile đã trả hai field này.
- ☐ **Bổ sung `fieldMessages`** theo Phụ lục A; sửa thông báo `phone`/`recipientPhone` (không còn là "đúng 10 chữ số" mà là "SĐT di động Việt Nam, đầu số 03/05/07/08/09").
- ☐ **`AdminOnly` route guard** (giống `CustomerOnly`): chưa đăng nhập → `/login`; `role !== 'admin'` → trang 403. Sau đăng nhập admin chuyển về `/admin`.
- ☐ **`src/adminApi.ts`** chứa toàn bộ lời gọi `/admin/*` (mỗi nhóm một object: `adminApi.products.list(...)`…) để UI không tự `fetch`.
- ☐ **Hàm tải file có token** (`downloadCsv`): `fetch` + `blob()` + `URL.createObjectURL` (thẻ `<a href>` không gắn được header `Authorization`).
- ☐ **Hàm upload ảnh**: `FormData` với field `file`, **không** tự set `Content-Type` (để trình duyệt tự thêm boundary). Lưu ý `request()` hiện luôn set `Content-Type: application/json` khi có `body` → cần nhánh riêng cho `FormData`.
- ☐ **Hook `useLocations`** cho 3 dropdown địa chỉ (cache trong bộ nhớ, BE đã gửi `Cache-Control` 1 ngày).
- ☐ Dựng khung layout admin: sidebar theo `FE_UI_Planning.md` §9 (A01–A14), route gốc `/admin`.

---

## 2. Checklist tổng

### Phần A — Khách hàng
| ☐ | Mã | Việc | Màn FE (file hiện có) |
|---|---|---|---|
| ☐ | A1 | Đăng ký (thêm email, SĐT chuẩn hóa) | `features/auth/Register.tsx` |
| ☐ | A2 | Đăng nhập (chấp nhận nhiều dạng SĐT) | `features/auth/Login.tsx` |
| ☐ | A3 | Lấy hồ sơ (thêm `email`, `role`) | `restoreSession()` trong `api.ts` |
| ☐ | A4 | Sửa hồ sơ (`fullName`, `email`) | `features/account/Profile.tsx` |
| ☐ | A5 | Danh sách địa chỉ (thêm mã tỉnh/quận/phường) | `features/account/Addresses.tsx` |
| ☐ | A6 | Dropdown địa giới 3 cấp | `components/AddressFields.tsx` |
| ☐ | A7 | Thêm địa chỉ (gửi mã địa giới) | `Addresses.tsx` |
| ☐ | A8 | Sửa/xóa địa chỉ, đặt mặc định | `Addresses.tsx` |
| ☐ | A9 | Thông báo (chuông) | `layouts/StoreLayout.tsx` (mới) |
| ☐ | A10 | Ví voucher | route mới `/account/vouchers` |
| ☐ | A11 | Banner mã công khai | `ApiCatalog.tsx` / trang chủ |
| ☐ | A12 | Danh sách mã chọn được ở checkout | `features/checkout/Checkout.tsx` |
| ☐ | A13 | Áp mã, tính lại tiền | `Checkout.tsx` |
| ☐ | A14 | Sản phẩm: chi tiết | `features/catalog/ProductDetail.tsx` |
| ☐ | A15 | Giỏ hàng | `features/checkout/CartPage.tsx` |
| ☐ | A16 | Đánh giá sản phẩm | `ProductDetail.tsx`, `features/reviews/Reviews.tsx` |
| ☐ | A17 | Trả hàng: kiểm tra đơn có trả được không | `features/returns/NewReturn.tsx` |
| ☐ | A18 | Trả hàng: tạo yêu cầu | `NewReturn.tsx` |
| ☐ | A19 | Trả hàng: danh sách + chi tiết | `Returns.tsx`, `ReturnDetail.tsx` |

### Phần B — Admin (chưa có màn nào)
| ☐ | Mã | Màn (theo `FE_UI_Planning.md`) | Route đề xuất |
|---|---|---|---|
| ☐ | B1 | A01 Dashboard | `/admin` |
| ☐ | B2 | A14 Báo cáo + CSV | `/admin/reports` |
| ☐ | B3 | A02 Danh sách sản phẩm | `/admin/products` |
| ☐ | B4 | A03 Tạo/sửa sản phẩm (ảnh, biến thể) | `/admin/products/new`, `/admin/products/:id/edit` |
| ☐ | B5 | A04 Danh mục | `/admin/categories` |
| ☐ | B6 | Size & màu | dùng trong B4 + `/admin/attributes` |
| ☐ | B7 | A05 Bảng size | `/admin/size-charts` |
| ☐ | B8 | A06 Tồn kho | `/admin/inventory` |
| ☐ | B9 | A07 Danh sách đơn | `/admin/orders` |
| ☐ | B10 | A08 Chi tiết đơn + xử lý | `/admin/orders/:orderCode` |
| ☐ | B11 | A09 Danh sách trả hàng | `/admin/returns` |
| ☐ | B12 | A10 Chi tiết trả hàng + xử lý | `/admin/returns/:returnCode` |
| ☐ | B13 | A12 Khách hàng (danh sách, chi tiết, khóa/mở) | `/admin/customers` |
| ☐ | B14 | A13 Đánh giá | `/admin/reviews` |
| ☐ | B15 | A11 Khuyến mãi / chiến dịch | `/admin/campaigns` |

### Phần C — BE còn thiếu (FE chưa nối được, xem cuối file)

---

# PHẦN A — KHÁCH HÀNG

## A1. Đăng ký tài khoản

- **Fetch gì:** Tạo tài khoản mới, đăng nhập luôn, nhận token.
- **API:** `POST /api/auth/register` — công khai.
- **Mô tả API:** Body `{ fullName, phone, password, email? }`.
  - `phone`: SĐT **di động Việt Nam**; nhập kiểu `0912 345 678`, `0912.345.678`, `+84912345678`, `84912345678` đều được; BE lưu và trả về `0912345678`. Đầu số phải là 03/05/07/08/09.
  - `fullName` 2–100 ký tự (BE gộp khoảng trắng thừa). `password` ≥ 8 ký tự, ≤ 72 byte UTF-8. `email` tùy chọn, tự viết thường; để trống = không có email.
  - Cùng một số viết khác dạng vẫn tính **trùng** (409).
  - Đăng ký xong BE **tự cấp voucher chào mừng** (`WELCOME10`) vào ví + tạo thông báo (+ gửi email nếu có email).
- **Response 200:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-11-23T05:20:04.3850155Z",
  "user": {
    "userId": 28,
    "fullName": "Nguyễn Minh Tiến",
    "phone": "0900000011",
    "role": "customer",
    "email": "tien.demo@example.com"
  }
}
```
- **Lỗi:** `400` (`fullName`, `phone`, `password`, `email`), `409` số điện thoại đã đăng ký.
- **Luồng:** (1) Người dùng điền form → (2) FE gửi nguyên chuỗi SĐT đã `trim` → (3) nhận token → `setToken` → (4) `restoreSession()` gọi A3 → (5) chuyển về trang trước đó. Có thể hiện toast "Bạn nhận được voucher chào mừng" (chuông ở A9 sẽ có thông báo).
- **Cách làm:**
  - `Register.tsx`: thêm ô `email` (không bắt buộc, `type="email"`); `httpApi.register(input)` thêm `email`.
  - `api.ts`: `authenticate()` gửi `{ ...input, phone: input.phone.trim() }` → giữ nguyên, thêm `email` nếu có.
  - **Sau khi nhận response, dùng `user.phone` BE trả về** (đã chuẩn hóa) thay cho chuỗi người dùng gõ.
  - `fieldMessages`: sửa `phone`, thêm `email`.

## A2. Đăng nhập

- **Fetch gì:** Xác thực, nhận token.
- **API:** `POST /api/auth/login` — công khai.
- **Mô tả API:** Body `{ phone, password }`. `phone` nhận mọi dạng như A1. Tài khoản cũ (đầu số lạ) vẫn đăng nhập được.
- **Response 200:** giống A1 (`accessToken`, `expiresAt`, `user{userId, fullName, phone, role, email}`).
- **Lỗi:** `400` thiếu/sai định dạng; `401` sai SĐT/mật khẩu **hoặc tài khoản bị khóa** (không phân biệt, FE luôn báo "Số điện thoại hoặc mật khẩu không đúng").
- **Luồng:** như A1. Nếu `user.role === 'admin'` → chuyển `/admin`, ngược lại về trang khách.
- **Cách làm:** `Login.tsx` không đổi gì ngoài việc đọc `role`. Hết hạn token (`expiresAt`): `scheduleExpiry` đã tự đăng xuất.

## A3. Lấy hồ sơ cá nhân (sau đăng nhập)

- **Fetch gì:** Thông tin người dùng hiện tại; dùng để khôi phục phiên khi tải lại trang.
- **API:** `GET /api/customers/me` — khách. (Admin dùng `/api/auth/login` response để biết `role`; endpoint này **chỉ cho khách**, admin gọi sẽ `403`.)
- **Response 200:**
```json
{
  "userId": 28,
  "fullName": "Nguyễn Minh Tiến",
  "phone": "0900000011",
  "email": "tien.demo@example.com",
  "role": "customer",
  "createdAt": "2026-09-12T04:09:17.921Z",
  "updatedAt": "2026-09-12T04:09:17.921Z"
}
```
- **Lỗi:** `401`, `403` (admin).
- **Luồng:** Mở app → có token trong `sessionStorage` → `restoreSession()` → nếu `401` thì `logout()`.
- **Cách làm:** `restoreSession()` đã làm; chỉ thêm `email` (và `role` lấy từ login response vì `/customers/me` trả `role` = `"customer"`). Với admin, **đừng** gọi `restoreSession()` bằng endpoint này — lưu `user` từ login response vào `sessionStorage` (hoặc thêm bước dò role từ JWT: claim `role`).

> ⚠️ Gợi ý: để tránh `403` khi admin tải lại trang, lưu `user` (gồm `role`) cùng token khi đăng nhập, hoặc giải mã payload JWT (claim `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`).

## A4. Sửa hồ sơ

- **Fetch gì:** Đổi họ tên và/hoặc email.
- **API:** `PATCH /api/customers/me` — khách.
- **Mô tả API:** Body `{ fullName?, email? }`, field nào **bỏ** thì giữ nguyên. Gửi `"email": ""` để **xóa** email. Phải có ít nhất 1 field. FE cũ chỉ gửi `{ fullName }` vẫn chạy.
- **Response 200:** như A3 (đã cập nhật).
- **Lỗi:** `400` (`fullName`, `email`).
- **Luồng:** Profile → sửa → Lưu → gọi PATCH → cập nhật `session.user`.
- **Cách làm:** `httpApi.updateProfile(fullName)` → đổi thành `updateProfile({ fullName?, email? })`; `Profile.tsx` thêm ô email. SĐT **không đổi được** (BE không hỗ trợ).

## A5. Danh sách địa chỉ giao hàng

- **Fetch gì:** Các địa chỉ đã lưu của khách (mặc định xếp trước).
- **API:** `GET /api/customers/me/addresses` — khách.
- **Response 200:** (rút gọn)
```json
[
  {
    "addressId": 9,
    "recipientName": "Nguyễn Minh Tiến",
    "recipientPhone": "0900000011",
    "addressLine": "12 Nguyễn Huệ",
    "ward": "Phường Tân Định",
    "district": "Quận 1",
    "province": "Thành phố Hồ Chí Minh",
    "isDefault": true,
    "createdAt": "2026-11-23T04:10:00Z",
    "provinceCode": 79,
    "districtCode": 760,
    "wardCode": 26734
  }
]
```
> Địa chỉ **tạo trước bản này** có `provinceCode/districtCode/wardCode = null` (chỉ có tên). Khi người dùng bấm Sửa, bắt chọn lại 3 dropdown.
- **Lỗi:** `401`.
- **Cách làm:** `AddressDto`/`toAddress()` thêm 3 mã; `Address` (domain.ts) thêm `provinceCode?: number`, … BE **không có `postalCode`** → bỏ ô này hoặc để ẩn.

## A6. Dropdown địa giới 3 cấp (tỉnh → quận/huyện → phường/xã)

- **Fetch gì:** Danh mục địa giới để người dùng chọn.
- **API:** (công khai, cache được)
  - `GET /api/locations/provinces`
  - `GET /api/locations/provinces/{provinceCode}/districts`
  - `GET /api/locations/districts/{districtCode}/wards`
- **Response:** mảng `[{ code, name }]`; `404` nếu mã không tồn tại.
```json
[
  {
    "code": 1,
    "name": "Thành phố Hà Nội"
  },
  {
    "code": 2,
    "name": "Tỉnh Hà Giang"
  }
]
```
- **Luồng:** Mở form → tải tỉnh (1 lần) → chọn tỉnh → tải quận → chọn quận → tải phường. Đổi tỉnh ⇒ **xóa** quận & phường đã chọn; đổi quận ⇒ xóa phường.
- **Cách làm:**
  - `AddressFields.tsx`: thay 3 ô text bằng 3 `<select>`; giá trị lưu là **mã** (số).
  - Cache theo mã trong `Map` ở module (danh sách tỉnh gọi 1 lần cho cả phiên).
  - Khi sửa địa chỉ cũ: tải quận theo `provinceCode`, phường theo `districtCode` rồi đặt giá trị đang chọn.
  - Đây là danh mục **3 cấp (trước 7/2025)**; FE chỉ hiển thị `name` BE trả về.

## A7. Thêm địa chỉ

- **Fetch gì:** Lưu địa chỉ giao hàng mới.
- **API:** `POST /api/customers/me/addresses` — khách.
- **Mô tả API:** Body:
```json
{
  "recipientName": "Nguyễn Minh Tiến",
  "recipientPhone": "0912 345 678",
  "addressLine": "12 Nguyễn Huệ",
  "provinceCode": 79,
  "districtCode": 760,
  "wardCode": 26734,
  "isDefault": true
}
```
  - `recipientPhone` nhận mọi dạng SĐT di động VN, trả về dạng `0912345678`. `recipientName` 2–100, `addressLine` 5–255 ký tự.
  - BE kiểm phường thuộc quận, quận thuộc tỉnh và **lưu tên chuẩn theo danh mục** (FE không cần gửi tên).
  - `isDefault: true` → các địa chỉ khác tự bỏ mặc định (chỉ có 1 mặc định).
  - FE cũ gửi tên (`province/district/ward`) vẫn được nhưng **không nên dùng nữa**.
- **Response 201:** địa chỉ như A5.
- **Lỗi:** `400` với key `recipientName`, `recipientPhone`, `addressLine`, `province`, `district`, `ward`:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "district": [
      "Choose a valid district that belongs to the selected province."
    ]
  }
}
```
- **Luồng:** Điền form → Lưu → `POST` → làm mới danh sách (A5).
- **Cách làm:** `httpApi.createAddress(input)` đổi payload sang mã; `fieldMessages` cho `province/district/ward` đổi nghĩa thành "Vui lòng chọn …".

## A8. Sửa / xóa địa chỉ

- **Fetch gì:** Cập nhật hoặc xóa một địa chỉ.
- **API:** `PUT /api/customers/me/addresses/{addressId}` (cùng body A7, thay toàn bộ) · `DELETE /api/customers/me/addresses/{addressId}` (→ `204`).
- **Lỗi:** `400` như A7; `404` địa chỉ không thuộc khách.
- **Cách làm:** FE hiện chưa có hàm `updateAddress/deleteAddress` trong `httpApi` → thêm. Mặc định: gửi `isDefault: true` ở PUT để đặt làm mặc định.

## A9. Thông báo (chuông)

- **Fetch gì:** Thông báo trong tài khoản: voucher mới, trạng thái đơn, trả hàng.
- **API:**

| API | Mô tả |
|---|---|
| `GET /api/customers/me/notifications/unread-count` | Số chưa đọc → `{ "unreadCount": 3 }` (dùng cho badge) |
| `GET /api/customers/me/notifications?unreadOnly=&page=&pageSize=` | Danh sách, mới nhất trước |
| `POST /api/customers/me/notifications/{id}/read` | Đánh dấu 1 thông báo đã đọc → `204` (gọi lại vẫn `204`) |
| `POST /api/customers/me/notifications/read-all` | Đọc hết → `204` |

- **Response danh sách:** (ví dụ)
```json
{
  "items": [
    {
      "notificationId": 5,
      "type": "voucher_issued",
      "title": "Bạn nhận được voucher WELCOME10",
      "body": "Chào mừng thành viên mới: Giảm 10%, tối đa 50.000đ cho đơn từ 300.000đ. Hạn dùng đến 23/12/2026. Voucher đã có sẵn trong ví, chọn khi thanh toán.",
      "data": { "userCouponId": 12, "campaignId": 1 },
      "isRead": false,
      "readAt": null,
      "createdAt": "2026-11-23T04:00:00Z"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "unreadCount": 1
}
```
  `type`: `voucher_issued` (data `userCouponId`, `campaignId`), `order_status` / `order_payment` (data `orderId`), `return_status` (data `returnId`). `title/body` đã là **tiếng Việt**, hiển thị nguyên văn.
- **Luồng:** Đăng nhập → gọi `unread-count` → hiện badge. Bấm chuông → tải danh sách → bấm 1 mục: gọi `read` rồi điều hướng theo `type` (voucher → `/account/vouchers`, đơn → `/account/orders`, trả hàng → `/account/returns`).
- **Cách làm:**
  - Component `NotificationBell` trong `StoreLayout.tsx` (chỉ khi `user.role === 'customer'`).
  - Poll `unread-count` mỗi 60 giây **chỉ khi tab đang hiển thị** (`document.visibilityState`), dừng khi đăng xuất.
  - Mở dropdown mới tải danh sách; "Đọc hết" gọi `read-all` rồi đặt badge = 0.
  - Voucher/ email do BE tự phát — FE không cần nút "Nhận mã".

## A10. Ví voucher

- **Fetch gì:** Các voucher đã được cấp cho khách (đã nằm sẵn trong ví, **không cần bấm nhận**).
- **API:** `GET /api/customers/me/vouchers?status=available|used|expired|all` — khách. Mặc định `available`.
- **Response 200:** (ví dụ)
```json
[
  {
    "userCouponId": 12,
    "status": "available",
    "issuedAt": "2026-11-23T04:00:00Z",
    "expiresAt": "2026-12-23T04:00:00Z",
    "usedAt": null,
    "coupon": {
      "couponId": 1, "code": "WELCOME10", "name": "Chào mừng thành viên mới",
      "description": "Tặng voucher giảm 10% cho thành viên vừa đăng ký.",
      "discountType": "percent", "discountValue": 10, "maxDiscountAmount": 50000,
      "minimumOrderAmount": 300000, "appliesTo": "all", "categoryIds": [],
      "summary": "Giảm 10%, tối đa 50.000đ cho đơn từ 300.000đ"
    }
  }
]
```
  `status` trả về: `available`, `used`, `expired`. `coupon.summary` là câu mô tả sẵn bằng tiếng Việt.
- **Luồng:** Trang `/account/vouchers` có 3 tab Còn dùng / Đã dùng / Hết hạn → mỗi tab gọi 1 lần với `status` tương ứng.
- **Cách làm:** Thêm route `/account/vouchers` trong `App.tsx` (bọc `CustomerOnly` + `AccountFrame`) và mục menu "Voucher". Card hiển thị `code`, `summary`, hạn (`expiresAt`), nút "Dùng ngay" → `/cart`. Hết hạn còn ≤ 3 ngày thì gắn nhãn cảnh báo.

## A11. Banner mã công khai

- **Fetch gì:** Các mã mọi người đều nhập được (ví dụ `1111`, `BLACKFRIDAY`).
- **API:** `GET /api/coupons` — **công khai**.
- **Response 200:**
```json
[
  {
    "coupon": {
      "couponId": 4,
      "code": "1111",
      "name": "Siêu sale 11.11",
      "description": "Nhập mã 1111: giảm 11% (tối đa 111.000đ) cho đơn từ 300.000đ. 1.111 lượt đầu tiên.",
      "discountType": "percent",
      "discountValue": 11,
      "maxDiscountAmount": 111000,
      "minimumOrderAmount": 300000,
      "appliesTo": "all",
      "categoryIds": [],
      "summary": "Giảm 11%, tối đa 111.000đ cho đơn từ 300.000đ"
    },
    "expiresAt": "2026-11-30T16:59:59Z",
    "remainingUses": 1106
  },
  {
    "coupon": {
      "couponId": 2,
      "code": "BLACKFRIDAY",
      "name": "Black Friday 2026",
      "description": "Giảm 15% (tối đa 150.000đ) cho đơn từ 500.000đ. Số lượng có hạn.",
      "discountType": "percent",
      "discountValue": 15,
      "maxDiscountAmount": 150000,
      "minimumOrderAmount": 500000,
      "appliesTo": "all",
      "categoryIds": [],
      "summary": "Giảm 15%, tối đa 150.000đ cho đơn từ 500.000đ"
    },
    "expiresAt": "2026-12-01T16:59:59Z",
    "remainingUses": 500
  }
]
```
  `remainingUses = null` nghĩa là không giới hạn.
- **Luồng:** Trang chủ/danh sách sản phẩm tải 1 lần → hiện banner "Nhập mã `1111` giảm 11%…". Bấm sao chép mã.
- **Cách làm:** Component `PromoStrip`; ẩn nếu mảng rỗng. Không cần đăng nhập.

## A12. Danh sách mã chọn được ở trang thanh toán

- **Fetch gì:** Tất cả mã khách **có thể chọn** cho giỏ hiện tại: voucher trong ví + mã công khai, đã tính sẵn mã nào áp được và giảm bao nhiêu.
- **API:** `POST /api/coupons/applicable` — khách.
- **Mô tả API:** Body `{ "items": [{ "variantId": 1, "quantity": 2 }] }` (1–50 dòng, số lượng 1–99; **giá lấy từ DB**, đừng gửi giá).
- **Response 200:** (xếp mã dùng được lên đầu, giảm nhiều nhất trước)
```json
[
  {
    "source": "public",
    "userCouponId": null,
    "coupon": {
      "couponId": 2,
      "code": "BLACKFRIDAY",
      "name": "Black Friday 2026",
      "description": "Giảm 15% (tối đa 150.000đ) cho đơn từ 500.000đ. Số lượng có hạn.",
      "discountType": "percent",
      "discountValue": 15,
      "maxDiscountAmount": 150000,
      "minimumOrderAmount": 500000,
      "appliesTo": "all",
      "categoryIds": [],
      "summary": "Giảm 15%, tối đa 150.000đ cho đơn từ 500.000đ"
    },
    "expiresAt": "2026-12-01T16:59:59Z",
    "eligible": true,
    "reason": null,
    "discountAmount": 150000,
    "shippingDiscount": 0
  },
  {
    "source": "public",
    "userCouponId": null,
    "coupon": {
      "couponId": 4,
      "code": "1111",
      "name": "Siêu sale 11.11",
      "description": "Nhập mã 1111: giảm 11% (tối đa 111.000đ) cho đơn từ 300.000đ. 1.111 lượt đầu tiên.",
      "discountType": "percent",
      "discountValue": 11,
      "maxDiscountAmount": 111000,
      "minimumOrderAmount": 300000,
      "appliesTo": "all",
      "categoryIds": [],
      "summary": "Giảm 11%, tối đa 111.000đ cho đơn từ 300.000đ"
    },
    "expiresAt": "2026-11-30T16:59:59Z",
    "eligible": false,
    "reason": "COUPON_USED",
    "discountAmount": 0,
    "shippingDiscount": 0
  }
]
```
  - `source`: `wallet` (voucher trong ví → áp bằng `userCouponId`) hoặc `public` (mã công khai → áp bằng `coupon.code`).
  - `eligible=false` thì `reason` là mã lỗi (`COUPON_MIN`, `COUPON_NOT_ELIGIBLE`, `COUPON_USED`, …) → **vẫn hiện nhưng làm mờ**, kèm lý do (Phụ lục A).
- **Lỗi:** `400` (`items`: giỏ rỗng/dòng sai, hoặc biến thể không còn bán).
- **Luồng:** Vào Checkout → gọi 1 lần với các dòng đã chọn → hiện danh sách dạng radio → người dùng chọn 1 mã (hoặc "không dùng") → A13.
- **Cách làm:** `Checkout.tsx` hiện gọi `svc.couponDiscount` (mock) → thay bằng `customerApi.applicableCoupons(items)`. Gọi lại khi giỏ đổi số lượng. Không có bước "nhận mã"/"claim".

## A13. Áp mã và tính lại tiền

- **Fetch gì:** Kiểm tra một mã với giỏ hàng và nhận số tiền giảm, phí ship, tổng thanh toán.
- **API:** `POST /api/coupons/validate` — khách.
- **Mô tả API:** Body `{ "code": "BLACKFRIDAY", "items": [...] }` **hoặc** `{ "userCouponId": 12, "items": [...] }` — chỉ gửi **một** trong hai (gửi cả hai/không gửi cái nào → `400 couponCode`).
- **Response 200:**
```json
{
  "coupon": {
    "couponId": 2,
    "code": "BLACKFRIDAY",
    "name": "Black Friday 2026",
    "description": "Giảm 15% (tối đa 150.000đ) cho đơn từ 500.000đ. Số lượng có hạn.",
    "discountType": "percent",
    "discountValue": 15,
    "maxDiscountAmount": 150000,
    "minimumOrderAmount": 500000,
    "appliesTo": "all",
    "categoryIds": [],
    "summary": "Giảm 15%, tối đa 150.000đ cho đơn từ 500.000đ"
  },
  "userCouponId": null,
  "subtotal": 1990000,
  "eligibleSubtotal": 1990000,
  "discountAmount": 150000,
  "shippingFee": 0,
  "shippingDiscount": 0,
  "total": 1840000
}
```
  Công thức BE: giảm giá tính trên `eligibleSubtotal`; phí ship 30.000đ, **miễn phí khi `subtotal − discount ≥ 500.000đ`**; mã `free_shipping` miễn phí ship (`shippingDiscount`). `total = subtotal − discountAmount + shippingFee − shippingDiscount`.
- **Lỗi:** `400` với `errors.couponCode = ["COUPON_*"]` hoặc `errors.items`. Mã lỗi `COUPON_INVALID`, `COUPON_NOT_STARTED`, `COUPON_EXPIRED`, `COUPON_LIMIT`, `COUPON_USED`, `COUPON_MIN`, `COUPON_NOT_ELIGIBLE` → câu tiếng Việt ở Phụ lục A.
- **Luồng:** Chọn mã ở A12 (hoặc gõ tay) → `validate` → cập nhật khối "Tóm tắt đơn hàng". Khi đặt đơn (API đặt đơn chưa có — Phần C) sẽ gửi cùng `code` hoặc `userCouponId`.
- **Cách làm:** Chỉ coi kết quả `validate` là để **hiển thị**; số tiền cuối cùng do BE tính lại khi đặt đơn. Với ô gõ tay: viết hoa mã, `trim`; mã `issued` chỉ dùng được nếu khách có trong ví.

## A14. Chi tiết sản phẩm *(đã có ở BE từ trước)*

- **Fetch gì:** Thông tin sản phẩm, ảnh, biến thể (size/màu/giá/tồn khả dụng), bảng size.
- **API:** `GET /api/products/{productId}` — công khai. `404` nếu sản phẩm ẩn/nháp.
- **Response 200:** (rút gọn)
```json
{
  "productId": 1,
  "categoryId": 1,
  "categoryName": "Áo thun",
  "name": "Áo thun Body Cotton",
  "description": "Áo thun Body Cotton với phom dáng dễ phối cùng trang phục thường ngày.",
  "material": "Cotton pha",
  "basePrice": 199000,
  "status": "active",
  "images": [
    {
      "imageUrl": "/images/products/P01-1.jpg",
      "altText": "Áo thun Body Cotton - ảnh 1"
    }
  ],
  "variants": [
    {
      "variantId": 1,
      "sku": "P01-S-0",
      "size": "S",
      "color": "Đen",
      "hexCode": "#22252b",
      "price": 199000,
      "status": "active",
      "quantityOnHand": 10,
      "quantityReserved": 0,
      "availableQuantity": 10,
      "inStock": true
    }
  ],
  "totalAvailableQuantity": 53,
  "sizeChart": {
    "sizeChartId": 1,
    "name": "Bảng chọn size · Áo thun Body Cotton",
    "description": "Bảng chọn size cho Áo thun Body Cotton",
    "items": [
      {
        "sizeLabel": "S",
        "chestCm": 88,
        "waistCm": 72,
        "hipCm": 90,
        "lengthCm": 65,
        "notes": "Số đo cơ thể tham khảo"
      }
    ]
  }
}
```
  `availableQuantity = quantityOnHand − quantityReserved`; chỉ biến thể `active` được trả.
- **Cách làm:** `App.tsx` đang chặn route này ở chế độ API ("Chi tiết sản phẩm chưa có API") → bỏ chặn, viết `httpApi.product(id)` và map sang `Product`/`Variant`.

## A15. Giỏ hàng *(đã có ở BE từ trước)*

- **API:** `GET /api/cart` · `POST /api/cart/items` `{ variantId, quantity }` (1–99, **cộng dồn** vào dòng đã có) · `DELETE /api/cart/items/{variantId}` — khách.
- **Response:** `{ cartId, items:[…], totalQuantity, subtotal }` (mỗi dòng có `price`, `availableQuantity`, `isPurchasable`).
```json
{
  "cartId": null,
  "items": [],
  "totalQuantity": 0,
  "subtotal": 0
}
```
- **Lỗi:** `409 { message, availableQuantity }` khi vượt tồn; `404` biến thể không bán.
- **Lưu ý:** BE **chưa có** `PUT` đổi số lượng và **chưa có** gộp giỏ khách vãng lai khi đăng nhập (Phần C). Hiện đổi số lượng = `POST` thêm/`DELETE` rồi `POST` lại.

## A16. Đánh giá sản phẩm *(đã có ở BE từ trước)*

| API | Mô tả |
|---|---|
| `GET /api/products/{id}/reviews?page=&pageSize=` | Công khai; `{ items:[{reviewId, productId, reviewerName, rating, comment, createdAt}], page, pageSize, totalCount, averageRating }` (pageSize ≤ 50). Chỉ đánh giá `published`. |
| `POST /api/products/{id}/reviews` | Khách; `{ rating: 1–5, comment: 10–500 ký tự }` → `201`; `409` nếu đã đánh giá sản phẩm này. |
| `GET /api/customers/me/reviews` | Đánh giá của tôi (kèm `status`: `published` hoặc `hidden`). |
| `DELETE /api/customers/me/reviews/{id}` | Xóa đánh giá của tôi → `204`. |

> Lưu ý nghiệp vụ: BE cho đánh giá **ngay** (chưa ràng buộc "đã mua"), khác với mô tả mock (cần đơn đã giao). Admin ẩn được ở B14; đánh giá bị ẩn **không hiện công khai** nhưng khách vẫn thấy trong "của tôi" với `status: "hidden"`.

## A17. Trả hàng — kiểm tra đơn có trả được không

- **Fetch gì:** Với một đơn, cho biết có được trả không và từng dòng còn trả được bao nhiêu.
- **API:** `GET /api/customers/me/orders/{orderCode}/returnable-items` — khách; `404` nếu đơn không phải của khách.
- **Response 200:**
```json
{
  "orderCode": "THM-1033",
  "deliveredAt": "2026-11-07T21:08:49.208Z",
  "returnDeadline": "2026-11-14T21:08:49.208Z",
  "eligible": false,
  "ineligibleReason": "The 7-day return window has ended.",
  "items": [
    {
      "orderItemId": 65,
      "productName": "Áo thun Cotton Premium",
      "sku": "P17-M-0",
      "size": "M",
      "color": "Trắng",
      "purchasedQuantity": 1,
      "returnedQuantity": 1,
      "remainingQuantity": 0,
      "unitPrice": 299000,
      "refundPerUnit": 299000
    }
  ]
}
```
  `eligible=false` + `ineligibleReason` (tiếng Anh, **đừng hiện nguyên văn**): đơn chưa giao hoặc quá 7 ngày kể từ lúc giao → FE tự viết câu tiếng Việt theo `returnDeadline`. `refundPerUnit` = tiền hoàn ước tính cho 1 sản phẩm (đã trừ giảm giá, không hoàn phí ship).
- **Luồng:** Chi tiết đơn → nút "Trả hàng" (chỉ khi đơn `delivered`) → màn tạo yêu cầu → gọi API này để vẽ danh sách dòng + số lượng tối đa.
- **Cách làm:** ⚠️ Cần **API "đơn của tôi"** (Phần C) để có `orderCode`. Trong lúc chờ, test bằng cách lấy mã đơn của khách demo từ màn admin (B10).

## A18. Trả hàng — tạo yêu cầu

- **Fetch gì:** Gửi yêu cầu trả hàng.
- **API:** `POST /api/customers/me/returns` — khách.
- **Mô tả API:** Body:
```json
{
  "orderCode": "THM-1033",
  "reason": "wrong_size",
  "description": "Áo bị chật hơn so với bảng size",
  "items": [{ "orderItemId": 65, "quantity": 1 }]
}
```
  - `reason` ∈ `wrong_size`, `wrong_item`, `defective`, `changed_mind`, `other`; `description` 10–1000 ký tự; chọn ≥ 1 dòng, `quantity` ≤ số còn trả được.
- **Response 201:** chi tiết yêu cầu (xem A19) với `status: "requested"`, `returnCode: "RTN-<n>"`.
- **Lỗi:** `400` (`orderCode`, `reason`, `description`, `items`), `409 { message }` (đơn chưa giao / quá 7 ngày).
- **Luồng:** Chọn dòng + số lượng + lý do + mô tả → Gửi → chuyển tới `/account/returns/{returnCode}`.
- **Cách làm:** Route hiện dùng `:returnId` → đổi sang `:returnCode` (chuỗi `RTN-1`). Ảnh bằng chứng: BE **chưa nhận** — bỏ khỏi form hoặc chỉ preview.

## A19. Trả hàng — danh sách và chi tiết của tôi

- **API:** `GET /api/customers/me/returns` (mới nhất trước) · `GET /api/customers/me/returns/{returnCode}`.
- **Response danh sách:** (rút gọn)
```json
[
  {
    "returnId": 2,
    "returnCode": "RTN-2",
    "orderCode": "THM-1033",
    "status": "requested",
    "itemCount": 1,
    "estimatedRefund": 299000,
    "requestedAt": "2026-11-08T21:08:49.208Z",
    "waitingDays": 14,
    "customer": {
      "userId": 28,
      "fullName": "Nguyễn Minh Tiến",
      "phone": "0900000011"
    }
  }
]
```
- **Response chi tiết:** `{ returnId, returnCode, orderCode, status, reason, description, items[{orderItemId, productName, sku, size, color, quantity, unitPrice, allocatedDiscount, estimatedRefund}], estimatedRefund, refundAmount, refundReference, rejectReason, requestedAt, updatedAt, refundedAt, events[{status, note, actor, at}], customer: null, allowedActions: [] }`. Cấu trúc đầy đủ giống B12 (phía admin) nhưng `customer=null`.
- **Trạng thái:** `requested` → `awaiting_return` → `received` → `refund_pending` → `refunded`; hoặc `rejected` (kèm `rejectReason`).
- **Cách làm:** `events` vẽ timeline; `status` đổi nhãn theo Phụ lục B. Thông báo `return_status` ở A9 trỏ về đây.

---

# PHẦN B — ADMIN

> Tất cả endpoint dưới đây cần token **admin** (`401` chưa đăng nhập, `403` khách). Đăng nhập admin bằng `POST /api/auth/login` (A2), đọc `user.role`. Quy tắc chung: **UI chỉ hiện nút/hành động mà BE cho phép** (`allowedTransitions`, `allowedActions`, `canConfirmPaid`, `hasOrders`…), lỗi `409 {message}` hiện thành toast.
> Spec giao diện từng màn: `FE_UI_Planning.md` §10.2 (A01–A14, đặt tên "A.." trong file đó khác với mã "A1–A19/B1–B15" của tài liệu này).

## B1. Dashboard (A01)

- **Fetch gì:** Toàn bộ số liệu màn tổng quan trong **một** lần gọi.
- **API:** `GET /api/admin/dashboard?from=yyyy-MM-dd&to=yyyy-MM-dd` — admin. Bỏ trống = 30 ngày gần nhất; tối đa 366 ngày.
- **Mô tả API:**
  - `kpis`: `orderCount` (đơn đặt trong kỳ, mọi trạng thái), `deliveredCount`, `grossRevenue` (tổng tiền các đơn **đã giao** trong kỳ), `refundAmount` (tiền đã hoàn do trả hàng), `netRevenue = gross − refund`, `unitsSold`.
  - `counters`: `ordersNeedingAction` (chờ xác nhận + đã xác nhận + đang đóng gói), `pendingOrders`, `pendingReturns` (yêu cầu trả chờ duyệt), `lowStockCount`.
  - `series`: mỗi ngày một điểm (kể cả ngày 0) → vẽ biểu đồ cột (số đơn + doanh thu).
  - `recentOrders` (5 đơn mới), `lowStock` (5 SKU tồn thấp nhất), `definitions` (cách tính từng KPI, tiếng Việt → hiện ở chú thích dưới bảng).
- **Response 200:** (rút gọn)
```json
{
  "range": {
    "from": "2026-10-25",
    "to": "2026-11-23",
    "days": 30
  },
  "kpis": {
    "orderCount": 22,
    "deliveredCount": 10,
    "grossRevenue": 10160060,
    "refundAmount": 349000,
    "netRevenue": 9811060,
    "unitsSold": 26
  },
  "counters": {
    "ordersNeedingAction": 6,
    "pendingOrders": 2,
    "pendingReturns": 2,
    "lowStockCount": 5
  },
  "series": [
    {
      "period": "2026-10-25",
      "orderCount": 1,
      "deliveredCount": 0,
      "grossRevenue": 0,
      "refundAmount": 0,
      "netRevenue": 0,
      "unitsSold": 0
    }
  ],
  "recentOrders": [
    {
      "orderCode": "THM-1045",
      "customerName": "Võ Quốc Bảo",
      "placedAt": "2026-11-22T12:41:55.674Z",
      "totalAmount": 1374000,
      "status": "pending"
    }
  ],
  "lowStock": [
    {
      "variantId": 9,
      "sku": "P02-M-0",
      "productName": "Áo thun Boxy Cotton",
      "size": "M",
      "color": "Xanh dương",
      "available": 0,
      "threshold": 5
    }
  ],
  "definitions": {
    "orderCount": "Số đơn đặt trong khoảng thời gian (mọi trạng thái, theo ngày đặt).",
    "deliveredCount": "Số đơn đã giao thành công trong khoảng (theo ngày giao).",
    "grossRevenue": "Tổng tiền (đã trừ giảm giá, gồm phí vận chuyển) của các đơn đã giao trong khoảng, theo ...",
    "refundAmount": "Tổng tiền đã hoàn cho các yêu cầu trả hàng có ngày hoàn nằm trong khoảng. Hoàn tiền đơn...",
    "netRevenue": "Doanh thu thuần = doanh thu gộp − tiền hoàn.",
    "unitsSold": "Tổng số sản phẩm trong các đơn đã giao trong khoảng.",
    "lowStock": "Biến thể đang bán có số khả dụng (tồn − đang giữ) nhỏ hơn hoặc bằng ngưỡng cảnh báo.",
    "timezone": "Ngày được tính theo giờ Việt Nam (UTC+7); tuần bắt đầu từ thứ Hai."
  }
}
```
- **Lỗi:** `400` key `range` (ngày sai, `from > to`, quá 366 ngày).
- **Luồng:** Vào `/admin` → gọi mặc định → hiện 5 thẻ KPI, biểu đồ, bảng đơn mới (bấm → B10), bảng tồn thấp (bấm → B8), thẻ "yêu cầu trả chờ duyệt" (bấm → B11). Đổi khoảng ngày → "Áp dụng" gọi lại.
- **Cách làm:** `adminApi.dashboard({ from, to })`. Biểu đồ: tự vẽ bằng SVG/CSS (dự án chưa có thư viện chart); trục X dùng `period`, trục Y `grossRevenue` và `orderCount`.

## B2. Báo cáo + xuất CSV (A14)

- **Fetch gì:** KPI + chuỗi theo ngày/tuần, top sản phẩm, tồn thấp; xuất file CSV.
- **API:**
  - `GET /api/admin/reports?from=&to=&groupBy=day|week` — tuần bắt đầu **thứ Hai**, `period` = ngày đầu tuần.
  - `GET /api/admin/reports/export?from=&to=&groupBy=` — tải CSV (`period,orders,delivered_orders,gross_revenue,refund_amount,net_revenue,units_sold` + dòng `TOTAL`), UTF-8 có BOM nên Excel mở đúng tiếng Việt.
- **Response (`reports`):** như dashboard nhưng thêm `topProducts[{ productId, productName, sku, units, revenue }]` (10 dòng, theo số lượng bán) và `lowStock` 20 dòng; không có `recentOrders`/`counters`.
```json
{
  "range": {
    "from": "2026-10-25",
    "to": "2026-11-23",
    "days": 30
  },
  "groupBy": "week",
  "kpis": {
    "orderCount": 22,
    "deliveredCount": 10,
    "grossRevenue": 10160060,
    "refundAmount": 349000,
    "netRevenue": 9811060,
    "unitsSold": 26
  },
  "series": [
    {
      "period": "2026-10-19",
      "orderCount": 1,
      "deliveredCount": 0,
      "grossRevenue": 0,
      "refundAmount": 0,
      "netRevenue": 0,
      "unitsSold": 0
    }
  ],
  "topProducts": [
    {
      "productId": 11,
      "productName": "Quần jean Classic",
      "sku": "P11-S-0",
      "units": 3,
      "revenue": 1008610
    }
  ],
  "lowStock": [
    {
      "variantId": 9,
      "sku": "P02-M-0",
      "productName": "Áo thun Boxy Cotton",
      "size": "M",
      "color": "Xanh dương",
      "available": 0,
      "threshold": 5
    }
  ],
  "definitions": {
    "orderCount": "Số đơn đặt trong khoảng thời gian (mọi trạng thái, theo ngày đặt).",
    "deliveredCount": "Số đơn đã giao thành công trong khoảng (theo ngày giao).",
    "grossRevenue": "Tổng tiền (đã trừ giảm giá, gồm phí vận chuyển) của các đơn đã giao trong khoảng, theo ...",
    "refundAmount": "Tổng tiền đã hoàn cho các yêu cầu trả hàng có ngày hoàn nằm trong khoảng. Hoàn tiền đơn...",
    "netRevenue": "Doanh thu thuần = doanh thu gộp − tiền hoàn.",
    "unitsSold": "Tổng số sản phẩm trong các đơn đã giao trong khoảng.",
    "lowStock": "Biến thể đang bán có số khả dụng (tồn − đang giữ) nhỏ hơn hoặc bằng ngưỡng cảnh báo.",
    "timezone": "Ngày được tính theo giờ Việt Nam (UTC+7); tuần bắt đầu từ thứ Hai."
  }
}
```
- **Lỗi:** `400` `range`, `groupBy`.
- **Luồng:** Chọn từ/đến + nhóm theo ngày/tuần → Áp dụng → KPI, biểu đồ, bảng top, bảng tồn. "Xuất CSV" → tải file đúng khoảng đang xem.
- **Cách làm:** CSV phải tải bằng `fetch` có token rồi `blob` (xem 1.2). Tên file lấy từ header `Content-Disposition` (`report_<from>_<to>.csv`). Validate `from ≤ to` ở FE trước khi gọi.

## B3. Danh sách sản phẩm (A02)

- **Fetch gì:** Bảng sản phẩm để quản lý.
- **API:** `GET /api/admin/products?q=&categoryId=&status=draft|active|hidden&sort=newest|name|price&page=&pageSize=` — admin. `q` khớp tên **hoặc SKU**.
- **Response 200:**
```json
{
  "items": [
    {
      "productId": 1,
      "name": "Áo thun Body Cotton",
      "slug": "ao-thun-body-cotton",
      "categoryId": 1,
      "categoryName": "Áo thun",
      "status": "active",
      "thumbnailUrl": "/images/products/P01-1.jpg",
      "variantCount": 6,
      "priceMin": 199000,
      "priceMax": 199000,
      "totalAvailable": 53,
      "createdAt": "2026-10-18T03:00:00Z",
      "updatedAt": "2026-10-18T03:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 17
}
```
  `priceMin/priceMax` là khoảng giá các biến thể; `totalAvailable` = tổng khả dụng của biến thể đang bán.
- **Lỗi:** `400` `status`, `sort`, `page`.
- **Luồng:** Toolbar (tìm, lọc danh mục/trạng thái, sắp xếp) → bảng, phân trang 10/trang → bấm hàng → B4 (sửa). Nút "Ẩn/Hiện" gọi `PATCH …/status` (B4).
- **Cách làm:** Debounce ô tìm 300 ms, hủy request cũ bằng `AbortController` (giống `ApiCatalog.tsx`). Nhãn trạng thái: Nháp / Đang bán / Đã ẩn. Danh mục lấy từ B5.

## B4. Tạo / sửa sản phẩm, ảnh, biến thể (A03)

- **Fetch gì:** Toàn bộ vòng đời một sản phẩm: tạo, xem, sửa thông tin, đổi trạng thái, ảnh, biến thể.
- **API:**

| API | Mô tả |
|---|---|
| `GET /api/admin/products/{id}` | Dữ liệu form sửa (xem mẫu dưới) |
| `POST /api/admin/products` | Tạo mới **kèm ảnh + biến thể** trong 1 request → `201` + chi tiết |
| `PATCH /api/admin/products/{id}` | Sửa `name, slug, categoryId, description, material, basePrice, sizeChartId` (field nào bỏ thì giữ; `sizeChartId: 0` = bỏ bảng size) |
| `PATCH /api/admin/products/{id}/status` | `{ "status": "draft" \| "active" \| "hidden" }` |
| `PUT /api/admin/products/{id}/images` | Thay **toàn bộ** thư viện ảnh `{ images:[{ url, altText?, colorId?, sortOrder? }] }` |
| `POST /api/admin/uploads/images` | Upload 1 ảnh (`multipart/form-data`, field `file`; JPEG/PNG/WebP ≤ 5MB) → `201 { url:"/api/media/<id>.png", fileName, size }` |
| `POST /api/admin/products/{id}/variants` | Thêm biến thể `{ sku, sizeId, colorId, price?, status?, initialOnHand?, lowStockThreshold? }` → trả chi tiết sản phẩm |
| `PATCH /api/admin/variants/{variantId}` | Sửa `sku`, `price`, `status` (`active`/`hidden`) |
| `DELETE /api/admin/variants/{variantId}` | Xóa biến thể (chưa từng nằm trong đơn) |

- **Body tạo mới:**
```json
{
  "name": "Áo thun cotton basic", "slug": null, "categoryId": 1,
  "description": "Ít nhất 20 ký tự khi sản phẩm ở trạng thái đang bán.",
  "material": "Cotton 100%", "basePrice": 199000, "sizeChartId": null, "status": "draft",
  "images": [{ "url": "/api/media/ab12….jpg", "altText": "Mặt trước", "colorId": 1, "sortOrder": 0 }],
  "variants": [{ "sku": "TSHIRT-M-BLK", "sizeId": 2, "colorId": 1, "price": 199000,
                 "status": "active", "initialOnHand": 20, "lowStockThreshold": 5 }]
}
```
- **Response `GET /{id}`:** (rút gọn)
```json
{
  "productId": 7,
  "name": "Sơ mi Cotton tay dài",
  "slug": "so-mi-cotton-tay-dai",
  "categoryId": 2,
  "categoryName": "Áo sơ mi",
  "description": "Sơ mi Cotton tay dài với phom dáng dễ phối cùng trang phục thường ngày.",
  "material": "Cotton pha",
  "basePrice": 529000,
  "status": "active",
  "sizeChartId": 7,
  "images": [
    {
      "imageId": 71,
      "url": "/images/products/P07-1.jpg",
      "altText": "Sơ mi Cotton tay dài - ảnh 1",
      "colorId": null,
      "sortOrder": 0
    }
  ],
  "variants": [
    {
      "variantId": 32,
      "sku": "P07-S-0",
      "sizeId": 1,
      "size": "S",
      "colorId": 11,
      "color": "Đỏ",
      "hexCode": "#b73731",
      "price": 529000,
      "status": "active",
      "onHand": 2,
      "reserved": 0,
      "available": 2,
      "lowStockThreshold": 5,
      "hasOrders": false
    }
  ],
  "createdAt": "2026-10-12T03:00:00Z",
  "updatedAt": "2026-10-12T03:00:00Z"
}
```
  Mỗi biến thể có `onHand`, `reserved`, `available`, `lowStockThreshold`, `hasOrders` (đã nằm trong đơn → **không cho xóa**, chỉ ẩn).
- **Quy tắc BE:**
  - `slug` bỏ trống → tự sinh từ tên (bỏ dấu); phải duy nhất.
  - SKU **luôn lưu chữ HOA** và duy nhất toàn shop (không phân biệt hoa/thường); mỗi sản phẩm một tổ hợp size+màu chỉ một lần.
  - Chuyển sang `active` cần: mô tả ≥ 20 ký tự, ≥ 1 ảnh, ≥ 1 biến thể `active` → nếu thiếu: `409 { message }` nêu rõ thiếu gì. Chỉ sản phẩm `active` hiện ở cửa hàng; sản phẩm `draft/hidden` → `GET /api/products/{id}` trả `404`.
  - Không cho ẩn/xóa biến thể `active` cuối cùng của sản phẩm đang bán (`409`).
  - Ảnh có `sortOrder` nhỏ nhất là **ảnh đại diện**. `url` bắt đầu bằng `/` hoặc `http(s)://`.
- **Lỗi:** `400` các key `name, slug, categoryId, description, material, basePrice, sizeChartId, status, images, variants, sku, price` (hai key `images`/`variants` có **nhiều** thông báo, đã đánh số "Variant 2: …" — nối lại hiển thị); `409` trùng SKU/size-màu, xóa biến thể đã bán, kích hoạt thiếu dữ liệu; `404` không tìm thấy.
- **Luồng (tạo):** (1) Điền thông tin → (2) chọn ảnh → mỗi ảnh gọi upload lấy `url` → (3) thêm bảng biến thể → (4) "Lưu nháp" (`status: draft`) hoặc "Lưu và hiển thị" (`active`) → `POST` → (5) chuyển sang trang sửa.
  **(sửa):** `GET` → sửa thông tin `PATCH`; đổi ảnh `PUT images`; thêm/sửa/xóa biến thể gọi API biến thể rồi **dùng chi tiết trả về** để cập nhật bảng (mỗi API biến thể trả cả sản phẩm).
- **Cách làm:**
  - Danh mục/size/màu cho dropdown: B5, B6; bảng size: B7.
  - **Tồn kho của biến thể đã có không sửa ở đây** (chỉ nhập `initialOnHand` lúc tạo) → điều chỉnh ở B8 để có lịch sử.
  - Upload: `FormData` + `fetch` có `Authorization`; không set `Content-Type`. Hiển thị ảnh bằng `url` trả về (cùng origin qua proxy). Ảnh seed cũ nằm trong FE `public/images/products/…`.
  - Chống bấm nhiều lần: khóa nút khi đang gửi.

## B5. Danh mục (A04)

- **Fetch gì:** CRUD danh mục.
- **API:** `GET /api/admin/categories?q=&status=` (không phân trang, đã sắp theo `displayOrder`) · `POST /api/admin/categories` · `GET|PATCH|DELETE /api/admin/categories/{id}`.
- **Body:** `{ name (2–150), slug?, description?, displayOrder?, status: "active"|"hidden" }`. `DELETE` → `204`; `409` nếu còn sản phẩm/bảng size dùng (→ ẩn thay vì xóa).
```json
[
  {
    "categoryId": 1,
    "name": "Áo thun",
    "slug": "ao-thun",
    "description": null,
    "displayOrder": 1,
    "status": "active",
    "productCount": 5,
    "createdAt": "2026-10-02T03:00:00Z",
    "updatedAt": "2026-10-18T03:00:00Z"
  },
  {
    "categoryId": 2,
    "name": "Áo sơ mi",
    "slug": "ao-so-mi",
    "description": null,
    "displayOrder": 2,
    "status": "active",
    "productCount": 4,
    "createdAt": "2026-10-02T03:00:00Z",
    "updatedAt": "2026-10-18T03:00:00Z"
  }
]
```
- **Lỗi:** `400` `name, slug, description, displayOrder, status` (slug trùng cũng là `400 slug`).
- **Luồng:** Bảng + drawer thêm/sửa; nút Ẩn/Hiện đổi `status`; nút Xóa chỉ bật khi `productCount = 0`.

## B6. Size & màu

- **API:** `GET /api/admin/sizes` · `POST /api/admin/sizes` `{ name, status? }` · `PATCH /api/admin/sizes/{id}` · `GET /api/admin/colors` · `POST /api/admin/colors` `{ name, hexCode: "#1A2B3C", status? }` · `PATCH /api/admin/colors/{id}`. Không có xóa (chỉ ẩn).
```json
[
  {
    "colorId": 1,
    "name": "Đen",
    "hexCode": "#22252b",
    "status": "active",
    "variantCount": 19
  },
  {
    "colorId": 2,
    "name": "Trắng",
    "hexCode": "#f2f1eb",
    "status": "active",
    "variantCount": 9
  }
]
```
- **Lỗi:** `400` `name`, `hexCode`, `status` (trùng tên cũng `400 name`).
- **Cách làm:** Dùng chung cho dropdown ở B4 và form biến thể. Color picker xuất `#RRGGBB` (BE tự viết hoa).

## B7. Bảng size (A05)

- **API:** `GET /api/admin/size-charts?categoryId=` · `GET /api/admin/size-charts/{id}` · `POST /api/admin/size-charts` · `PUT /api/admin/size-charts/{id}` (thay toàn bộ dòng) · `DELETE /api/admin/size-charts/{id}`.
- **Body:** `{ name, description?, categoryId?, items:[{ sizeLabel, chestCm?, waistCm?, hipCm?, lengthCm?, notes? }] }` — ≥ 1 dòng, `sizeLabel` không trùng trong 1 bảng (không phân biệt hoa/thường).
```json
{
  "sizeChartId": 1,
  "name": "Bảng chọn size · Áo thun Body Cotton",
  "description": "Bảng chọn size cho Áo thun Body Cotton",
  "categoryId": 1,
  "categoryName": "Áo thun",
  "itemCount": 3,
  "productCount": 1,
  "createdAt": "2026-10-18T03:00:00Z",
  "items": [
    {
      "sizeChartItemId": 101,
      "sizeLabel": "S",
      "chestCm": 88,
      "waistCm": 72,
      "hipCm": 90,
      "lengthCm": 65,
      "notes": "Số đo cơ thể tham khảo"
    }
  ]
}
```
- **Lỗi:** `400` `name, description, categoryId, items`; `409` xóa khi sản phẩm đang dùng.
- **Cách làm:** Form dạng lưới thêm/xóa dòng. Gắn vào sản phẩm bằng `sizeChartId` ở B4.

## B8. Tồn kho (A06)

- **Fetch gì:** Bảng tồn theo biến thể, điều chỉnh có lý do, ngưỡng cảnh báo, lịch sử.
- **API:**

| API | Mô tả |
|---|---|
| `GET /api/admin/inventory?q=&lowStock=true&status=active\|hidden&page=&pageSize=` | Bảng; `q` khớp SKU/tên sản phẩm |
| `GET /api/admin/inventory/{variantId}` | Một dòng |
| `POST /api/admin/inventory/{variantId}/adjustments` | `{ type:"increase"\|"decrease", quantity (1–100000), reason (10–300 ký tự) }` → dòng sau cập nhật |
| `PATCH /api/admin/inventory/{variantId}` | `{ lowStockThreshold }` (≥ 0) |
| `GET /api/admin/inventory/{variantId}/movements?page=` | Lịch sử biến động |

- **Response bảng:**
```json
{
  "items": [
    {
      "variantId": 9,
      "sku": "P02-M-0",
      "productId": 2,
      "productName": "Áo thun Boxy Cotton",
      "size": "M",
      "color": "Xanh dương",
      "variantStatus": "active",
      "onHand": 0,
      "reserved": 0,
      "available": 0,
      "lowStockThreshold": 5,
      "isLow": true,
      "updatedAt": "2026-11-23T04:09:17.921Z"
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 5
}
```
  `available = onHand − reserved`; `isLow = available ≤ lowStockThreshold`.
- **Lịch sử (ví dụ):**
```json
{ "items": [
  { "movementId": 31, "type": "ship", "deltaOnHand": -2, "deltaReserved": -2, "onHandAfter": 10, "reservedAfter": 0,
    "reason": null, "refType": "order", "refId": 1001, "actor": "Quản trị viên Themis", "createdAt": "2026-11-23T03:00:00Z" } ],
  "page": 1, "pageSize": 20, "totalCount": 1 }
```
  `type`: `initial` (nhập đầu), `adjust_increase`, `adjust_decrease`, `reserve`, `release`, `ship`, `restock`, `compensation` (hoàn tác khi một thao tác lỗi giữa chừng).
- **Lỗi:** `400` `type`, `quantity`, `reason`, `lowStockThreshold`; `409` giảm quá số khả dụng ("không được thấp hơn số đang giữ cho đơn").
- **Luồng:** Bảng + bộ lọc "Sắp hết" → "Điều chỉnh" mở modal (loại, số lượng, lý do) → gọi API → cập nhật dòng; icon lịch sử mở drawer.
- **Cách làm:** Không có ô sửa trực tiếp `onHand` (luôn qua modal có lý do). Badge cảnh báo khi `isLow`.

## B9. Danh sách đơn hàng (A07)

- **API:** `GET /api/admin/orders?q=&status=&paymentStatus=&paymentMethod=&from=&to=&page=&pageSize=` · `GET /api/admin/orders/counts`.
  - `q`: mã đơn hoặc SĐT người nhận. `status` nhận **danh sách cách nhau dấu phẩy** (`confirmed,packing`).
- **Response danh sách:** (rút gọn)
```json
{
  "items": [
    {
      "orderId": 45,
      "orderCode": "THM-1045",
      "placedAt": "2026-11-22T12:41:55.674Z",
      "customer": {
        "userId": 32,
        "fullName": "Võ Quốc Bảo",
        "phone": "0900000015"
      },
      "recipientName": "Võ Quốc Bảo",
      "recipientPhone": "0900000015",
      "itemCount": 6,
      "totalAmount": 1374000,
      "paymentMethod": "cod",
      "paymentStatus": "unpaid",
      "status": "pending"
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 45
}
```
- **Response `counts`:** dùng cho badge của tab.
```json
{
  "byStatus": {
    "pending": 2,
    "confirmed": 2,
    "packing": 2,
    "shipping": 3,
    "delivered": 25,
    "delivery_failed": 1,
    "returning_to_seller": 1,
    "returned_to_seller": 5,
    "cancelled": 4
  },
  "tabs": {
    "new": 2,
    "toPack": 4,
    "shipping": 3,
    "failed": 1,
    "returning": 6
  }
}
```
  Tab → `status`: **Mới** = `pending`; **Cần đóng gói** = `confirmed,packing`; **Đang giao** = `shipping`; **Giao thất bại** = `delivery_failed`; **Hoàn về** = `returning_to_seller,returned_to_seller`.
- **Lỗi:** `400` `status`, `from`, `to`, `page`.
- **Luồng:** Vào trang → gọi `counts` + danh sách tab "Mới" → đổi tab = đổi `status` → bấm hàng → B10.
- **Cách làm:** Refresh `counts` sau mỗi lần chuyển trạng thái ở B10 (hoặc khi quay lại danh sách).

## B10. Chi tiết đơn + xử lý (A08)

- **Fetch gì:** Xem một đơn và thực hiện các bước xử lý.
- **API:**

| API | Mô tả |
|---|---|
| `GET /api/admin/orders/{orderCode}` | Chi tiết đầy đủ |
| `POST /api/admin/orders/{orderCode}/transitions` | Chuyển trạng thái (body bên dưới) → trả chi tiết mới |
| `POST /api/admin/orders/{orderCode}/payment` | `{ action:"confirm_paid"\|"confirm_refunded", reference? }` → trả chi tiết mới |

- **Response `GET`:** (rút gọn; đơn này đang `delivery_failed`)
```json
{
  "orderId": 37,
  "orderCode": "THM-1037",
  "status": "delivery_failed",
  "placedAt": "2026-11-11T12:31:46.877Z",
  "updatedAt": "2026-11-13T11:41:49.234Z",
  "deliveredAt": null,
  "customer": {
    "userId": 32,
    "fullName": "Võ Quốc Bảo",
    "phone": "0900000015"
  },
  "recipientName": "Võ Quốc Bảo",
  "recipientPhone": "0900000015",
  "shippingAddressLine": "103 Hai Bà Trưng",
  "shippingWard": "Phường 2",
  "shippingDistrict": "Quận Tân Bình",
  "shippingProvince": "Thành phố Hồ Chí Minh",
  "shippingPostalCode": null,
  "subtotal": 807000,
  "discountAmount": 0,
  "shippingFee": 0,
  "totalAmount": 807000,
  "paymentMethod": "cod",
  "paymentStatus": "unpaid",
  "paidAt": null,
  "refundedAt": null,
  "customerNote": null,
  "cancelReason": null,
  "carrier": "GHTK",
  "trackingCode": "VN700005069",
  "returnInspection": null,
  "couponId": null,
  "items": [
    {
      "orderItemId": 70,
      "variantId": 13,
      "productId": 3,
      "productName": "Áo thun Cotton Oversize",
      "sku": "P03-S-0",
      "size": "S",
      "color": "Xanh nhạt",
      "quantity": 1,
      "unitPrice": 289000,
      "discountAmount": 0,
      "lineTotal": 289000
    }
  ],
  "deliveryAttempts": [
    {
      "attemptNumber": 1,
      "attemptedAt": "2026-11-13T11:41:49.234Z",
      "reason": "unreachable",
      "note": "Không liên lạc được với người nhận sau 2 cuộc gọi."
    }
  ],
  "history": [
    {
      "oldStatus": null,
      "newStatus": "pending",
      "changedBy": "Võ Quốc Bảo",
      "note": null,
      "changedAt": "2026-11-11T12:31:46.877Z"
    }
  ],
  "allowedTransitions": [
    {
      "toStatus": "shipping",
      "requiresReason": false,
      "requiresFailureInfo": false,
      "requiresInspection": false
    }
  ],
  "canConfirmPaid": false,
  "canConfirmRefunded": false,
  "maxDeliveryAttempts": 2
}
```
  - `allowedTransitions[]` = **danh sách nút được phép**: `{ toStatus, requiresReason, requiresFailureInfo, requiresInspection }`. **Chỉ vẽ nút có trong mảng này**; cờ `requires*` cho biết cần mở modal nhập gì.
  - `canConfirmPaid` / `canConfirmRefunded` → nút "Xác nhận đã thanh toán" / "Xác nhận đã hoàn tiền".
  - `history` = timeline (ai, khi nào, ghi chú); `deliveryAttempts` = các lần giao thất bại (tối đa `maxDeliveryAttempts` = 2).
- **Body `transitions`:** `{ toStatus, note?, reason?, failureReason?, attemptedAt?, inspection?, carrier?, trackingCode? }`
  - `cancelled` → `reason` bắt buộc (10–300 ký tự).
  - `delivery_failed` → `failureReason` ∈ `unreachable | wrong_address | reschedule | customer_refused | other` và `note` (10–300 ký tự); `attemptedAt` ISO tùy chọn (mặc định = bây giờ).
  - `returned_to_seller` → `inspection` = `passed` (nhập lại kho) | `failed`; `note` tùy chọn.
  - `shipping` từ `packing` → `carrier`, `trackingCode` tùy chọn (≤ 100 ký tự).
- **Sơ đồ trạng thái:**
  `pending → confirmed → packing → shipping → delivered`
  `pending/confirmed/packing → cancelled`
  `shipping → delivery_failed → shipping (giao lại, tối đa 2 lần, không áp dụng nếu khách từ chối) | returning_to_seller → returned_to_seller`
  `packing → shipping` chỉ khi **COD** hoặc chuyển khoản đã `paid`. `delivered`, `cancelled`, `returned_to_seller` là trạng thái cuối.
- **Hiệu ứng phụ (BE tự làm, FE chỉ hiển thị):** bàn giao vận chuyển trừ tồn; hủy nhả hàng giữ chỗ + trả lượt mã giảm giá; giao thành công → COD thành `paid`, tự cấp voucher "chi tiêu ≥ 500k" nếu đủ; hoàn về đạt kiểm → nhập lại kho; đơn đã `paid` bị hủy/hoàn về → `refund_pending`. Khách nhận thông báo mỗi bước.
- **Lỗi:** `400` `reason`, `note`, `failureReason`, `inspection`, `carrier`, `trackingCode`, `action`, `reference`; `409 { message, allowed[] }` bước không hợp lệ hoặc **đơn vừa bị người khác đổi** ("Reload it and try again") hoặc không đủ hàng giữ chỗ để bàn giao; `404`.
- **Luồng:** Mở chi tiết → hiện nút theo `allowedTransitions` → bấm nút có `requires*` → modal nhập → gửi → nhận chi tiết mới → cập nhật timeline và nút.
- **Cách làm:** Không tự suy luận trạng thái ở FE; luôn lấy `allowedTransitions` từ response mới nhất. Bắt `409` → toast + `GET` lại chi tiết.

## B11. Danh sách trả hàng (A09)

- **API:** `GET /api/admin/returns?status=&q=&from=&to=&page=&pageSize=` · `GET /api/admin/returns/counts`.
  - `status` nhận danh sách phẩy; `q` khớp mã trả/mã đơn/tên/SĐT khách.
- **Response:**
```json
{
  "items": [
    {
      "returnId": 1,
      "returnCode": "RTN-1",
      "orderCode": "THM-1034",
      "status": "requested",
      "itemCount": 1,
      "estimatedRefund": 289000,
      "requestedAt": "2026-11-11T03:45:54.983Z",
      "waitingDays": 12,
      "customer": {
        "userId": 29,
        "fullName": "Trần Thị Bình",
        "phone": "0900000012"
      }
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 8
}
```
```json
{
  "byStatus": {
    "requested": 2,
    "awaiting_return": 1,
    "received": 1,
    "refund_pending": 1,
    "refunded": 1,
    "rejected": 2
  }
}
```
  Tab → `status`: Chờ duyệt = `requested`; Chờ nhận = `awaiting_return`; Chờ kiểm = `received`; Chờ hoàn = `refund_pending`; Hoàn tất = `refunded`; Từ chối = `rejected`. `waitingDays` = số ngày kể từ lúc yêu cầu.
- **Luồng:** giống B9; bấm hàng → B12.

## B12. Chi tiết trả hàng + xử lý (A10)

- **API:** `GET /api/admin/returns/{returnCode}` · `POST /api/admin/returns/{returnCode}/transitions`.
- **Body:** `{ action, reason?, note?, refundReference? }` với `action`:

| action | Từ trạng thái | Sang | Cần |
|---|---|---|---|
| `approve` | `requested` | `awaiting_return` | `note?` |
| `reject` | `requested` | `rejected` | `reason` 10–300 |
| `receive` | `awaiting_return` | `received` | `note?` |
| `inspect_pass` | `received` | `refund_pending` (**nhập lại kho**) | `note?` |
| `inspect_fail` | `received` | `rejected` | `reason` 10–300 |
| `refund` | `refund_pending` | `refunded` | `refundReference` (≤ 100) |

- **Response `GET`:** (rút gọn)
```json
{
  "returnId": 4,
  "returnCode": "RTN-4",
  "orderCode": "THM-1031",
  "status": "received",
  "reason": "wrong_item",
  "description": "Sản phẩm không đúng như mong đợi nên mình muốn trả lại.",
  "items": [
    {
      "orderItemId": 60,
      "productName": "Áo thun Body Cotton",
      "sku": "P01-S-0",
      "size": "S",
      "color": "Đen",
      "quantity": 1,
      "unitPrice": 199000,
      "allocatedDiscount": 0,
      "estimatedRefund": 199000
    }
  ],
  "estimatedRefund": 199000,
  "refundAmount": null,
  "refundReference": null,
  "rejectReason": null,
  "requestedAt": "2026-11-07T05:39:14.018Z",
  "updatedAt": "2026-11-07T21:39:14.018Z",
  "refundedAt": null,
  "events": [
    {
      "status": "requested",
      "note": "Sản phẩm không vừa, mình muốn trả lại shop.",
      "actor": null,
      "at": "2026-11-07T05:39:14.018Z"
    }
  ],
  "customer": {
    "userId": 34,
    "fullName": "Bùi Thanh Tùng",
    "phone": "0900000017"
  },
  "allowedActions": [
    {
      "action": "inspect_pass",
      "requiresReason": false,
      "requiresRefundReference": false
    }
  ]
}
```
  `allowedActions[]` = nút được phép (`{ action, requiresReason, requiresRefundReference }`). `estimatedRefund` = `Σ(line_total × qty ÷ số đã mua)` (đã trừ giảm giá, **không hoàn phí ship**); `refundAmount` chỉ có sau khi `refunded`.
- **Lỗi:** `400` `reason`, `note`, `refundReference`; `409 { message, allowed[] }`; `404`.
- **Luồng:** Duyệt → (khách gửi hàng) → xác nhận đã nhận → kiểm hàng đạt/không đạt → xác nhận đã hoàn. Mỗi bước khách nhận thông báo `return_status`.
- **Cách làm:** Vẽ nút từ `allowedActions`; modal có ô lý do khi `requiresReason`, ô mã giao dịch hoàn khi `requiresRefundReference`.

## B13. Khách hàng (A12): danh sách, chi tiết, khóa/mở

- **API:**

| API | Mô tả |
|---|---|
| `GET /api/admin/customers?q=&status=active\|locked&page=&pageSize=` | `q` khớp tên/SĐT/email |
| `GET /api/admin/customers/{userId}` | Drawer chi tiết |
| `POST /api/admin/customers/{userId}/lock` | `{ reason }` 10–300 ký tự → trả chi tiết |
| `POST /api/admin/customers/{userId}/unlock` | Không body |

- **Response danh sách:**
```json
{
  "items": [
    {
      "userId": 35,
      "fullName": "Hoàng Gia Hân",
      "phone": "0900000018",
      "email": null,
      "status": "locked",
      "createdAt": "2026-10-03T04:09:17.921Z",
      "orderCount": 5,
      "totalSpent": 2022030
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 8
}
```
  `orderCount` = số đơn không bị hủy; `totalSpent` = tổng tiền các đơn **đã giao**.
- **Response chi tiết:** (rút gọn)
```json
{
  "userId": 28,
  "fullName": "Nguyễn Minh Tiến",
  "phone": "0900000011",
  "email": "tien.demo@example.com",
  "status": "active",
  "statusReason": null,
  "statusChangedAt": null,
  "createdAt": "2026-09-12T04:09:17.921Z",
  "updatedAt": "2026-09-12T04:09:17.921Z",
  "addresses": [
    {
      "addressId": 1,
      "recipientName": "Nguyễn Minh Tiến",
      "recipientPhone": "0900000011",
      "addressLine": "12 Nguyễn Huệ",
      "ward": "Phường Tân Định",
      "district": "Quận 1",
      "province": "Thành phố Hồ Chí Minh",
      "isDefault": true,
      "createdAt": "2026-11-23T04:09:17.921Z",
      "provinceCode": 79,
      "districtCode": 760,
      "wardCode": 26734
    }
  ],
  "stats": {
    "orderCount": 4,
    "deliveredCount": 3,
    "totalSpent": 3684000,
    "lastOrderAt": "2026-11-17T00:20:41.854Z"
  },
  "recentOrders": [
    {
      "orderCode": "THM-1041",
      "placedAt": "2026-11-17T00:20:41.854Z",
      "totalAmount": 1454000,
      "status": "packing"
    }
  ],
  "vouchers": {
    "available": 0,
    "used": 0,
    "expired": 0
  },
  "reviewCount": 1
}
```
- **Hành vi khóa:** khách bị khóa **không đăng nhập được** và token đang dùng bị `401` ở lần gọi kế tiếp; lịch sử đơn vẫn giữ. Chỉ khóa được tài khoản khách (admin → `404`).
- **Lỗi:** `400` `reason`; `409` đã khóa/chưa khóa; `404`.
- **Luồng:** Bảng + tìm kiếm → bấm hàng → drawer (hồ sơ, địa chỉ, thống kê, 5 đơn gần nhất → B10, số voucher) → nút Khóa/Mở có hộp xác nhận + ô lý do.
- **Cách làm:** Không hiển thị `passwordHash` (BE không trả). Danh sách này **không** gồm tài khoản admin.

## B14. Đánh giá (A13)

- **API:** `GET /api/admin/reviews?q=&status=published|hidden&rating=1..5&productId=&page=&pageSize=` · `PATCH /api/admin/reviews/{reviewId}/status` `{ status, reason? }` · `DELETE /api/admin/reviews/{reviewId}` (xóa hẳn — đã có từ trước).
```json
{
  "items": [
    {
      "reviewId": 9,
      "productId": 3,
      "productName": "Áo thun Cotton Oversize",
      "userId": 29,
      "customerName": "Trần Thị Bình",
      "customerPhone": "0900000012",
      "orderItemId": 66,
      "rating": 5,
      "comment": "Chất vải mát, mặc rất thoải mái, đúng size.",
      "status": "published",
      "moderationReason": null,
      "createdAt": "2026-11-12T03:45:54.983Z"
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 6
}
```
- **Hành vi:** Ẩn → bài biến mất khỏi trang sản phẩm (`GET /products/{id}/reviews`) nhưng khách vẫn thấy ở "đánh giá của tôi" với `status: hidden`. Hiện lại bằng `status: "published"`. **Không** sửa nội dung/số sao của khách.
- **Lỗi:** `400` `status`, `reason`, `rating`; `404`.
- **Cách làm:** Mặc định chưa có trạng thái "chờ duyệt" — mọi đánh giá khách gửi hiện ngay.

## B15. Khuyến mãi / chiến dịch (A11)

- **Khái niệm:** Một **chiến dịch** (campaign) có đúng **một mã** (coupon).
  - Giảm giá: `percent` (1–100, tùy chọn trần `maxDiscountAmount`), `fixed` (trừ tiền), `free_shipping` (miễn phí ship).
  - Phân phối: `public_code` (ai nhập mã cũng dùng được, giới hạn `usageLimit`/`perUserLimit`) hoặc `issued` (phát thẳng vào **ví** khách; khách chọn ở checkout, **không cần bấm nhận**).
  - Cách phát (`trigger`, chỉ cho `issued`): `signup` (tự cấp khi đăng ký — chỉ **một** chiến dịch `signup` chạy cùng lúc), `spend` (tự cấp khi tổng chi tiêu các đơn đã giao ≥ `triggerMinSpent`), `manual` (admin bấm phát).
  - Trạng thái hiển thị: `draft`, `scheduled` (chưa tới giờ), `active`, `paused`, `ended`.
- **API:**

| API | Mô tả |
|---|---|
| `GET /api/admin/campaigns?status=&trigger=&q=&page=&pageSize=` | Danh sách + thống kê mỗi chiến dịch |
| `POST /api/admin/campaigns` | Tạo (body bên dưới) → `201` |
| `GET /api/admin/campaigns/{id}` | Chi tiết |
| `PATCH /api/admin/campaigns/{id}` | Sửa (bỏ field = giữ nguyên); đã phát/dùng thì **khóa** quy tắc |
| `POST /api/admin/campaigns/{id}/pause` · `/resume` · `/end` | Tạm dừng / chạy lại (cũng kích hoạt bản nháp) / kết thúc (không hoàn tác) |
| `POST /api/admin/campaigns/{id}/duplicate` | `{ code, startsAt, endsAt, name? }` → bản **nháp** |
| `POST /api/admin/campaigns/{id}/issue` | Phát tay (xem dưới) |
| `GET /api/admin/campaigns/{id}/stats` | Thống kê |
| `GET /api/admin/campaigns/{id}/usages?page=` | Lịch sử dùng mã |
| `GET /api/admin/campaigns/{id}/recipients?status=&page=` | Danh sách người được phát |

- **Body tạo:**
```json
{
  "name": "Chào mừng thành viên mới",
  "description": "Giảm 10% đơn đầu, tối đa 50.000đ",
  "distribution": "issued", "trigger": "signup", "triggerMinSpent": null,
  "startsAt": "2026-12-01T00:00:00Z", "endsAt": "2027-03-01T00:00:00Z",
  "draft": false,
  "coupon": {
    "code": "WELCOME10", "discountType": "percent", "discountValue": 10, "maxDiscountAmount": 50000,
    "minimumOrderAmount": 300000, "appliesTo": "all", "categoryIds": [],
    "usageLimit": null, "perUserLimit": 1, "validDaysAfterIssue": 30
  }
}
```
  `trigger` mặc định `none` cho `public_code`, `manual` cho `issued`; `spend` bắt buộc `triggerMinSpent`. `usageLimit` rỗng/`0` = không giới hạn. `appliesTo` = `all` hoặc `categories` (+ `categoryIds`). Mã 4–30 ký tự `A–Z 0–9 _ -`, lưu chữ HOA.
- **Response chi tiết:**
```json
{
  "campaignId": 7,
  "name": "Khách hàng thân thiết",
  "description": "Tự động tặng voucher khi tổng chi tiêu (đơn đã giao) đạt 500.000đ.",
  "trigger": "spend",
  "triggerMinSpent": 500000,
  "distribution": "issued",
  "status": "active",
  "startsAt": "2026-11-14T17:00:00Z",
  "endsAt": "2027-06-30T16:59:59Z",
  "createdAt": "2026-11-23T04:09:17.01Z",
  "updatedAt": "2026-11-23T04:09:17.01Z",
  "coupon": {
    "couponId": 7,
    "code": "THANTHIET",
    "discountType": "percent",
    "discountValue": 15,
    "maxDiscountAmount": 120000,
    "minimumOrderAmount": 500000,
    "appliesTo": "all",
    "categoryIds": [],
    "usageLimit": null,
    "usageCount": 0,
    "perUserLimit": 1,
    "validDaysAfterIssue": 60,
    "status": "active"
  },
  "stats": {
    "issued": 0,
    "available": 0,
    "used": 0,
    "expired": 0,
    "revoked": 0,
    "usageCount": 0,
    "usageLimit": null,
    "totalDiscount": 0,
    "redemptionRate": null
  }
}
```
- **Phát tay:** `POST /campaigns/{id}/issue` — chỉ chiến dịch `issued` có `trigger` = `manual` hoặc `spend`, đang chạy.
  Body có **đúng 2 lựa chọn**: `{ "segment": "all_customers" }` (tất cả khách) hoặc `{ "segment": "min_spent", "minSpent": 1000000 }` (khách đã chi tiêu ≥ số này, tính trên đơn đã giao). Response `{ matched, issued, skipped }` (`skipped` = đã có mã, chạy lại không trùng). Chiến dịch `spend` thì phát tay = **phát bù** cho khách đã đủ điều kiện từ trước.
- **Thống kê:**
```json
{
  "issued": 0,
  "available": 0,
  "used": 0,
  "expired": 0,
  "revoked": 0,
  "usageCount": 0,
  "usageLimit": null,
  "totalDiscount": 0,
  "redemptionRate": null
}
```
  `usageCount`/`totalDiscount` chỉ có số liệu khi có đơn dùng mã (dữ liệu demo đã có vài đơn).
- **Lỗi:** `400` `name, description, distribution, trigger, triggerMinSpent, startsAt, endsAt, code, discountType, discountValue, maxDiscountAmount, minimumOrderAmount, appliesTo, categoryIds, usageLimit, perUserLimit, validDaysAfterIssue, segment, minSpent, status`; `409` mã trùng; hai chiến dịch `signup` trùng thời gian (kèm `conflictingCampaignId`); sửa quy tắc sau khi đã phát (kèm `lockedFields[]` → khóa các ô đó); sai trạng thái.
- **Luồng:** Danh sách (lọc trạng thái/cách phát, tìm theo tên hoặc mã) → "Tạo chiến dịch" (4 bước: thông tin → quy tắc giảm giá → cách phát → lịch & giới hạn) → chi tiết có KPI (`stats`), nút Tạm dừng/Tiếp tục/Kết thúc/Nhân bản, nút **Phát mã** (hộp thoại 2 lựa chọn) cho chiến dịch phát tay, tab Lịch sử dùng + Người nhận.
- **Cách làm:** Khóa các ô có trong `lockedFields`; gợi ý "Nhân bản" khi muốn đổi quy tắc đã khóa. Việc cấp mã cho khách (ví + thông báo + email) do BE làm.

---

# PHẦN C — BE CÒN THIẾU (FE chưa nối được)

Các chức năng dưới đây **chưa có API**; màn hình FE hiện bị chặn ở chế độ API (xem `App.tsx`). Cần BE làm trước khi FE nối:

- ☐ **Đặt đơn / tính tiền** (`POST /api/checkout/quote`, `POST /api/orders` có `Idempotency-Key`): dùng lại `CouponService`, `InventoryService.ReserveAsync`, document ở `Features/Orders`. Khi có API, đơn gửi `couponCode` **hoặc** `userCouponId` (A13).
- ☐ **Đơn của tôi** (`GET /api/customers/me/orders`, `/{orderCode}`) và **hủy đơn** (`POST …/cancel`).
- ☐ **Giỏ hàng**: `PUT /api/cart/items/{variantId}` (đổi số lượng) + gộp giỏ khách vãng lai khi đăng nhập.
- ☐ **Danh mục & danh sách sản phẩm công khai** có lọc/phân trang (`GET /api/categories`, `GET /api/products`).
- ☐ Gợi ý sản phẩm (`/api/recommendations`, `/api/customer-events`) và chính sách (`/api/storefront/policies`).
- ☐ Đánh giá gắn với đơn đã giao (`reviewable-items`, `POST /api/reviews`).
- ☐ Đồng bộ trạng thái đơn vị vận chuyển (tạm bỏ, admin cập nhật tay).

---

# PHỤ LỤC A — Thông báo lỗi (thêm vào `fieldMessages` và bảng mã lỗi)

BE trả thông báo **tiếng Anh**; FE tra theo **key** để hiển thị tiếng Việt.

| Key | Câu tiếng Việt gợi ý |
|---|---|
| `fullName` | Họ và tên là bắt buộc (2–100 ký tự). |
| `phone` | Vui lòng nhập số điện thoại di động Việt Nam (đầu số 03, 05, 07, 08 hoặc 09). |
| `password` | Mật khẩu cần ít nhất 8 ký tự và tối đa 72 byte. |
| `email` | Email không hợp lệ. |
| `recipientName` | Tên người nhận là bắt buộc (2–100 ký tự). |
| `recipientPhone` | Số điện thoại người nhận phải là số di động Việt Nam. |
| `addressLine` | Địa chỉ cụ thể (số nhà, đường) là bắt buộc (5–255 ký tự). |
| `province` | Vui lòng chọn tỉnh/thành phố. |
| `district` | Vui lòng chọn quận/huyện thuộc tỉnh đã chọn. |
| `ward` | Vui lòng chọn phường/xã thuộc quận/huyện đã chọn. |
| `couponCode` | (xem bảng mã `COUPON_*` bên dưới) |
| `items` | Giỏ hàng không hợp lệ hoặc có sản phẩm không còn bán. |
| `name` | Tên là bắt buộc. |
| `slug` | Đường dẫn chỉ gồm chữ thường, số và dấu gạch ngang, hoặc đã tồn tại. |
| `categoryId` | Danh mục không tồn tại. |
| `description` | Mô tả không hợp lệ (cần ≥ 20 ký tự khi đăng bán). |
| `material` | Chất liệu tối đa 150 ký tự. |
| `basePrice`, `price` | Giá phải lớn hơn 0. |
| `sizeChartId` | Bảng size không tồn tại. |
| `images`, `variants` | (BE trả nhiều câu — hiển thị lần lượt) |
| `sku` | SKU gồm chữ, số, `.`, `_`, `-` và phải duy nhất. |
| `displayOrder` | Thứ tự hiển thị phải ≥ 0. |
| `hexCode` | Mã màu dạng `#1A2B3C`. |
| `status` | Trạng thái không hợp lệ. |
| `type`, `quantity` | Chọn loại điều chỉnh và nhập số lượng hợp lệ. |
| `reason` | Lý do phải từ 10 đến 300 ký tự. |
| `note` | Ghi chú tối đa 300 ký tự (khi giao thất bại: 10–300). |
| `failureReason` | Chọn lý do giao thất bại. |
| `inspection` | Chọn kết quả kiểm hàng (đạt / không đạt). |
| `refundReference` | Nhập mã giao dịch hoàn tiền. |
| `reference`, `carrier`, `trackingCode` | Tối đa 100 ký tự. |
| `lowStockThreshold` | Ngưỡng cảnh báo phải ≥ 0. |
| `action` | Hành động không hợp lệ. |
| `orderCode` | Không tìm thấy đơn hàng. |
| `startsAt`, `endsAt` | Thời gian bắt đầu/kết thúc không hợp lệ (kết thúc phải sau bắt đầu). |
| `code` | Mã gồm 4–30 ký tự chữ, số, `-`, `_`. |
| `discountType`, `discountValue`, `maxDiscountAmount`, `minimumOrderAmount` | Kiểm tra lại quy tắc giảm giá. |
| `appliesTo`, `categoryIds` | Chọn phạm vi áp dụng / danh mục hợp lệ. |
| `usageLimit`, `perUserLimit`, `validDaysAfterIssue` | Giới hạn lượt không hợp lệ. |
| `distribution`, `trigger`, `triggerMinSpent` | Cách phân phối/phát mã không hợp lệ (spend cần mức chi tiêu > 0). |
| `segment`, `minSpent` | Chọn đối tượng nhận mã và mức chi tiêu tối thiểu > 0. |
| `range`, `from`, `to`, `groupBy`, `page`, `sort`, `rating` | Bộ lọc không hợp lệ. |
| `userCouponId` | Voucher không hợp lệ. |

**Mã `COUPON_*`** (trong `errors.couponCode[0]` và `reason` của A12):

| Mã | Câu tiếng Việt |
|---|---|
| `COUPON_INVALID` | Mã không hợp lệ hoặc không áp dụng được cho tài khoản này. |
| `COUPON_NOT_STARTED` | Mã chưa đến thời gian sử dụng. |
| `COUPON_EXPIRED` | Mã đã hết hạn. |
| `COUPON_LIMIT` | Mã đã hết lượt sử dụng. |
| `COUPON_USED` | Bạn đã sử dụng mã này. |
| `COUPON_MIN` | Đơn hàng chưa đạt giá trị tối thiểu của mã. |
| `COUPON_NOT_ELIGIBLE` | Giỏ hàng không có sản phẩm thuộc phạm vi áp dụng của mã. |

# PHỤ LỤC B — Nhãn tiếng Việt cho các mã trạng thái

| Nhóm | Mã → nhãn |
|---|---|
| **Đơn hàng** | `pending` Chờ xác nhận · `confirmed` Đã xác nhận · `packing` Đang đóng gói · `shipping` Đang giao · `delivered` Đã giao · `delivery_failed` Giao thất bại · `returning_to_seller` Đang hoàn về shop · `returned_to_seller` Đã hoàn về shop · `cancelled` Đã hủy |
| **Thanh toán** | `unpaid` Chưa thanh toán · `pending` Chờ xác nhận chuyển khoản · `paid` Đã thanh toán · `refund_pending` Chờ hoàn tiền · `refunded` Đã hoàn tiền |
| **Phương thức** | `cod` Thanh toán khi nhận hàng · `bank_transfer` Chuyển khoản ngân hàng |
| **Trả hàng** | `requested` Chờ duyệt · `awaiting_return` Chờ nhận hàng · `received` Chờ kiểm hàng · `refund_pending` Chờ hoàn tiền · `refunded` Đã hoàn tiền · `rejected` Từ chối |
| **Lý do trả hàng** | `wrong_size` Sai size · `wrong_item` Giao sai hàng · `defective` Sản phẩm lỗi · `changed_mind` Đổi ý · `other` Khác |
| **Lý do giao thất bại** | `unreachable` Không liên lạc được · `wrong_address` Sai địa chỉ · `reschedule` Khách hẹn lại · `customer_refused` Khách từ chối nhận · `other` Khác |
| **Sản phẩm** | `draft` Nháp · `active` Đang bán · `hidden` Đã ẩn |
| **Chiến dịch** | `draft` Nháp · `scheduled` Chờ chạy · `active` Đang chạy · `paused` Tạm dừng · `ended` Đã kết thúc |
| **Loại giảm giá** | `percent` Giảm theo % · `fixed` Giảm số tiền · `free_shipping` Miễn phí vận chuyển |
| **Cách phát mã** | `none` Nhập mã · `signup` Khi đăng ký · `spend` Khi chi tiêu đủ · `manual` Admin phát tay |
| **Voucher của khách** | `available` Còn dùng · `used` Đã dùng · `expired` Hết hạn |
| **Đánh giá** | `published` Đang hiển thị · `hidden` Đã ẩn |
| **Khách hàng** | `active` Hoạt động · `locked` Đã khóa |

# PHỤ LỤC C — Kiểu dữ liệu TypeScript dùng chung (gợi ý)

```ts
type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number }

type AuthUser = { userId: number; fullName: string; phone: string; role: 'customer' | 'admin'; email: string | null }
type AuthResult = { accessToken: string; expiresAt: string; user: AuthUser }

type LocationOption = { code: number; name: string }
type AddressDto = {
  addressId: number; recipientName: string | null; recipientPhone: string | null; addressLine: string | null
  ward: string | null; district: string | null; province: string | null; isDefault: boolean | null; createdAt: string | null
  provinceCode: number | null; districtCode: number | null; wardCode: number | null
}

type CouponSummary = {
  couponId: number; code: string; name: string; description: string | null
  discountType: 'percent' | 'fixed' | 'free_shipping'; discountValue: number; maxDiscountAmount: number | null
  minimumOrderAmount: number; appliesTo: 'all' | 'categories'; categoryIds: number[]; summary: string
}
type Voucher = { userCouponId: number; status: 'available' | 'used' | 'expired'; issuedAt: string; expiresAt: string; usedAt: string | null; coupon: CouponSummary }
type CouponOption = {
  source: 'wallet' | 'public'; userCouponId: number | null; coupon: CouponSummary; expiresAt: string
  eligible: boolean; reason: string | null; discountAmount: number; shippingDiscount: number
}
type CouponQuote = {
  coupon: CouponSummary; userCouponId: number | null; subtotal: number; eligibleSubtotal: number
  discountAmount: number; shippingFee: number; shippingDiscount: number; total: number
}

type NotificationItem = {
  notificationId: number; type: 'voucher_issued' | 'order_status' | 'order_payment' | 'return_status'
  title: string; body: string; data: Record<string, number> | null; isRead: boolean; readAt: string | null; createdAt: string
}

type ReturnStatus = 'requested' | 'awaiting_return' | 'received' | 'refund_pending' | 'refunded' | 'rejected'
type OrderStatus = 'pending' | 'confirmed' | 'packing' | 'shipping' | 'delivered' | 'delivery_failed' | 'returning_to_seller' | 'returned_to_seller' | 'cancelled'

type OrderAction = { toStatus: OrderStatus; requiresReason: boolean; requiresFailureInfo: boolean; requiresInspection: boolean }
type ReturnAction = { action: 'approve' | 'reject' | 'receive' | 'inspect_pass' | 'inspect_fail' | 'refund'; requiresReason: boolean; requiresRefundReference: boolean }
```
