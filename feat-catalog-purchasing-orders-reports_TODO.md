# [TODO] Tích hợp FE — catalog công khai, chất liệu/công dụng, nhập hàng, báo cáo theo kỳ, đơn của khách, chuẩn ảnh

> **Dành cho người làm FE hoặc agent được giao làm FE.** File này đủ để làm trọn phần FE của nhánh BE
> `vantri/feat-catalog-purchasing-orders-reports` mà không cần hỏi lại: mỗi việc có **API, luồng, file cần sửa, tiêu chí nghiệm thu**,
> kèm **hướng thiết kế UI** tham khảo các web thời trang lớn (Uniqlo, Zalando, Coolmate, H&M, Routine).
> - Chi tiết từng endpoint (query, body, mọi mã lỗi): [`README.md`](README.md). Quy ước chung (token, 401/403/409, phân trang): [`feat-voucher-admin-dashboard_TODO.md`](feat-voucher-admin-dashboard_TODO.md) mục 1 — **file này ghi đè** nếu có chỗ mâu thuẫn.
> - Việc **đặt hàng** (báo giá + tạo đơn) do người khác làm — xem Phần C.
> - Khi xong một mục: đổi `☐` thành `☑`, ghi tên người làm.

- Snapshot FE đã đối chiếu: `se310-thesis-FE@70a473e`. Stack: React 19 + TS + Vite + React Router 7, **không có UI lib**, icon `lucide-react`, CSS thuần ở `src/App.css` + token màu ở `src/index.css` (`--primary #c7462c`, `--accent-soft`, `--bg`, `--border`, `--ink`, `--muted`, `--success`, `--danger`). Giữ nguyên theme "coral cream" này.
- FE hiện **chưa có màn admin nào**, và ở `VITE_DATA_SOURCE=api` các trang chi tiết SP, giỏ hàng, thanh toán, đơn hàng, trả hàng, đánh giá đang hiện "chưa có API" (`src/App.tsx`).

---

## 0. Chạy thử

```powershell
# BE (đã có sẵn dữ liệu demo nếu chạy các script mongo-init)
docker compose up -d                       # API http://localhost:8080, Mailpit http://localhost:8025
# FE
cd ../se310-thesis-FE; $env:VITE_DATA_SOURCE='api'; npm run dev   # proxy /api -> :8080
```

Tài khoản demo (`mongo-init/04-seed-demo-data.js`, mật khẩu `Demo@12345`): admin `0900000001`, khách `0900000011`–`0900000018`
(`0900000018` bị khóa). Dữ liệu demo có 17 sản phẩm (16 đang bán), 6 chất liệu, 6 công dụng, 3 nhà cung cấp, 5 phiếu nhập, 45 đơn, 8 trả hàng.
Cấu hình hiển thị tài khoản chuyển khoản/tên cửa hàng: biến `STORE_*` trong `.env` (xem `.env.example`).

---

## 1. Thay đổi làm vỡ code FE hiện tại (sửa trước)

| Cũ | Mới | FE cần làm |
|---|---|---|
| Admin tạo biến thể gửi `initialOnHand: 20` | `initialOnHand > 0` → `400`. Biến thể mới **tồn 0**; tồn chỉ vào qua **phiếu nhập hàng** | Bỏ ô "tồn ban đầu"; có màn Nhập hàng (B7) |
| `POST /admin/inventory/{id}/adjustments` có `type: increase` | Chỉ **giảm**, bắt buộc `reasonCode` (`damaged\|lost\|stocktake\|other`); `increase` → `400` | Modal chỉ còn "Giảm tồn" + select lý do; "Tăng tồn" dẫn sang Nhập hàng |
| Kích hoạt SP cần mô tả + ảnh + biến thể | Cần thêm **chất liệu** + **≥ 1 công dụng** | Form SP có 2 trường mới (B3) |
| Upload ảnh ≤ 5 MB → `{url}` | ≤ 20 MB, **≥ 900×1200**, lưu **webp**, trả `{mediaId, url, width, height, srcset, warnings}` | Lưu ảnh SP bằng `mediaId`; hiện `warnings` |
| Ảnh SP `{imageUrl, altText}` | Thêm `isPrimary, colorId, width, height, focalX, focalY, srcset` (tên cũ giữ) | Thẻ SP/gallery dùng `srcset` + điểm lấy nét (Phụ lục 2) |
| Báo cáo `groupBy=week`: `period` = ngày thứ Hai | `period` là khóa kỳ (`2026-W47`, `2026-11`, `2026-Q4`, `2026`) + `label`, `start`, `end` | Trục biểu đồ dùng `label`; **không** parse `period` thành ngày |
| CSV báo cáo 7 cột | 15 cột | Chỉ ảnh hưởng nếu FE tự đọc CSV |
| Review: ai cũng gửi được | Cần **đơn đã giao** chứa SP, không thì `409 {code:"REVIEW_NOT_PURCHASED"}` | Chỉ hiện "Viết đánh giá" khi SP có trong `reviewable-items` |
| `/products/search` trả cả SP nháp/ẩn | Chỉ SP `active`; ảnh theo thứ tự admin đặt | Bỏ bước FE tự lọc `status` |
| Login/đăng ký không giới hạn | Quá `RateLimit__AuthPerMinute` lần/phút → `429 {message}` + `Retry-After` | Hiện "Thử lại sau N giây" |

---

## 2. Việc làm một lần ở lõi FE (làm trước Phần A, B)

☐ **L1. `src/api.ts` — chia sẻ `request`.** Hiện `request()` chưa export và chỉ trả JSON. Việc cần làm:
  1. `export` hàm `request`, `json`, `getToken` (hoặc tách `src/api/core.ts`), rồi tạo `src/api/store.ts` (API khách) và `src/api/admin.ts` (API admin) dùng chung. Giữ nguyên `httpApi` cũ.
  2. Thêm tham số kiểu trả về: `request<T>(path, options, authenticated, as: 'json'|'text'|'blob' = 'json')` — cần cho in phiếu nhập (HTML) và xuất CSV (blob).
  3. Xử lý `429`: lấy header `Retry-After`, message "Bạn thao tác quá nhanh, thử lại sau N giây." Xử lý `409` có `code` / `availableQuantity`: đưa vào `ServiceError` (thêm field `code?`, `data?`) thay vì chỉ message.
  4. Lỗi 400 nhiều dòng (key `items`, `variants`, `images` là **mảng** message) → nối bằng xuống dòng, không chỉ message đầu.
  5. Bổ sung `fieldMessages` (Phụ lục 3).
  6. Giữ `role` của user (BE trả `user.role`): `toUser` hiện bỏ mất — thêm `role` vào `User`/session để dựng guard admin.
☐ **L2. Guard admin + layout admin.** `src/layouts/AdminOnly.tsx` (giống `CustomerOnly`: chưa đăng nhập → `/login?returnTo=`; đăng nhập nhưng không phải admin → trang 403) và `src/layouts/AdminLayout.tsx` (sidebar trái + thanh trên, xem Phụ lục 2.6). Route `/admin/*` bọc `AdminOnly`. Khách đăng nhập bằng tài khoản admin vẫn dùng cùng form `/login`; sau login nếu `role==='admin'` chuyển `/admin`.
☐ **L3. Thành phần UI dùng chung** (tạo trong `src/components/`, không thêm thư viện): `Skeleton`, `Pagination` (số trang + "Xem thêm"), `StatusBadge`, `ConfirmDialog` (dựa `Modal.tsx` có sẵn), `Toast` (hoặc tái dùng `feedback.tsx`), `Drawer` (trượt phải cho chi tiết), `BarChart` (SVG thuần, mục B9), `MoneyDelta`.
☐ **L4. Format.** `money()` đã có ở `domain.ts`; thêm `formatDateTime` giờ Việt Nam (BE trả UTC ISO `…Z`; hiển thị `Intl.DateTimeFormat('vi-VN', {timeZone:'Asia/Ho_Chi_Minh'})`). Ngày gửi lên BE là `yyyy-MM-dd` (ngày VN).
☐ **L5. Danh mục động.** Thay `categories` hardcode (`seed.ts`) ở `StoreLayout.tsx` (menu), `ApiCatalog.tsx` bằng `GET /api/categories` (cache trong module, tải một lần). `id` danh mục là số (`categoryId`), URL dùng `category=<slug>` hoặc id.

---

## 3. Checklist tổng

**Phần A — Khách**
- ☐ A1 Menu danh mục động · ☐ A2 Danh sách SP + bộ lọc đầy đủ · ☐ A3 Chi tiết SP + gợi ý size · ☐ A4 Giỏ hàng (tick, số lượng, gộp giỏ)
- ☐ A5 Đơn của tôi (danh sách, chi tiết, hủy, chuyển khoản) · ☐ A5b Màn "Hủy / Trả hàng" (đơn có thể hủy hoặc trả) · ☐ A6 Đánh giá gắn đơn đã giao · ☐ A7 Đổi mật khẩu · ☐ A8 CSS ảnh theo chuẩn

**Phần B — Admin (mới hoàn toàn)**
- ☐ B1 Khung admin + đăng nhập · ☐ B2 Chất liệu & Công dụng · ☐ B3 Sản phẩm (danh sách + form) · ☐ B4 Ảnh sản phẩm (upload chuẩn)
- ☐ B5 Bảng size · ☐ B6 Size & màu · ☐ B7 Nhà cung cấp + Phiếu nhập hàng + In phiếu · ☐ B8 Tồn kho · ☐ B9 Báo cáo doanh thu · ☐ B10 Đổi mật khẩu admin

*(Các màn admin còn lại — dashboard, đơn hàng, trả hàng, khách hàng, đánh giá, mã giảm giá — đã có API từ nhánh trước, mô tả trong `feat-voucher-admin-dashboard_TODO.md` Phần B; làm chung khung B1.)*

---

# PHẦN A — KHÁCH HÀNG

## A1. Menu danh mục + chính sách ở thanh thông báo ☐

- **API**: `GET /api/categories` → `[{categoryId, name, slug, description, displayOrder, productCount}]`; `GET /api/storefront/policies` → `{shippingFee, freeShippingThreshold, returnWindowDays, …}`.
- **Làm**: `StoreLayout.tsx` — thay `categories` hardcode bằng dữ liệu API; link `/products?category=<slug>`. Thanh `announcement-bar` lấy số tiền freeship/ngày đổi trả từ policies (thay `demoPolicy`) khi `api`.
- **Nghiệm thu**: thêm/ẩn danh mục ở admin → menu đổi sau khi tải lại; danh mục hết SP vẫn hiện (productCount 0 — ẩn nếu 0 cho gọn).

## A2. Danh sách sản phẩm + bộ lọc ☐ (thay `ApiCatalog.tsx`)

- **API**: `GET /api/products?q=&category=&size=&material=&occasion=&minPrice=&maxPrice=&inStock=&sort=&page=&pageSize=`; bộ lọc từ `GET /api/materials`, `GET /api/occasions`, `GET /api/categories`.
- **Luật** (giống bản mock đã chốt): URL là nguồn sự thật (`?q=&category=&size=M&size=L&material=cotton&occasion=work&minPrice=&maxPrice=&inStock=1&sort=&page=`); cùng nhóm = OR, khác nhóm = AND; size + giá + còn hàng phải đúng trên **cùng một biến thể**; đổi bộ lọc → `page=1`; `sort` ∈ `newest|price_asc|price_desc|name_asc`; `pageSize` mặc định 12 (tối đa 48); `minPrice > maxPrice` → `400 maxPrice`. Slug chất liệu/công dụng giữ như mock: `cotton|denim|synthetic`, `daily|work|casual|outdoor` (+ `linen|kaki|wool`, `school|sport`) — **nhưng danh sách thật lấy từ API, đừng hardcode**.
- **Item trả về**: `{productId, slug, name, categoryId, categoryName, priceMin, priceMax, image, hoverImage, colors[], sizes[], inStock, material, materialGroup, occasions[], createdAt}`.
- **Luồng**: đọc `useSearchParams` → gọi API (AbortController, như file hiện tại) → hiện lưới; đổi bộ lọc = `setParams`. Tải danh sách bộ lọc một lần.
- **Giao diện** (xem Phụ lục 2.1): sidebar bộ lọc dính trên desktop, bottom-sheet trên mobile; hàng chip bộ lọc đang chọn + "Xóa tất cả"; select sắp xếp; số kết quả; skeleton; phân trang. Đã có CSS `.catalog-layout`, `.product-grid` để dùng lại.
- **Types** (thêm vào `src/api/store.ts`): xem Phụ lục 4.
- **Sửa `ProductCard.tsx`**: nhận `ProductListItem` (không phải `SearchProduct`); giá = `priceMin` (hoặc "từ …" nếu `priceMin < priceMax`); ảnh = `image` với `srcset`, hover đổi sang `hoverImage`; chấm màu từ `colors`; badge "Hết hàng" khi `!inStock`, "Mới" khi `createdAt` trong 14 ngày.
- **Nghiệm thu**: lọc từng nhóm và kết hợp cho kết quả đúng (so với trang admin); gõ "ao thun" (không dấu) tìm ra "Áo thun…"; F5 giữ nguyên bộ lọc; lỗi mạng có nút "Thử lại"; không có kết quả có gợi ý "Xóa bộ lọc".

## A3. Chi tiết sản phẩm + bảng size + gợi ý size ☐ (bỏ chặn "chưa có API" ở `App.tsx`, nối `ProductDetail.tsx`)

- **API**: `GET /api/products/{id}` hoặc `GET /api/products/slug/{slug}` (route nên đổi thành `/products/:slug`, vẫn nhận id số); `GET /api/products/{id}/size-suggestion?heightCm=&weightKg=`; `GET /api/products/{id}/reviews`.
- **Dữ liệu mới**: `slug`, `material` (text), `materialGroup {id,name,slug}`, `occasions[]`, `careInstructions`, `images[]` đã sắp theo thứ tự (ảnh đầu = chính; `colorId` để nhảy ảnh theo màu), mỗi biến thể có `sizeId`/`colorId`/`availableQuantity`, `sizeChart.items[]` có `heightMinCm/heightMaxCm/weightMinKg/weightMaxKg`.
- **Luồng**: chọn màu → ảnh có `colorId` đó (không có thì ảnh đầu); chọn size → biến thể = (size, màu) → hiện giá, "Còn N", nút "Thêm vào giỏ" (`POST /api/cart/items`); biến thể hết hàng → chip size gạch chéo, không chọn được. "Tìm size của bạn": nhập cao/nặng → `size-suggestion` → `suggestedSize` + `matchType` (`exact` "Phù hợp", `closest` "Gần nhất", `none` "Chưa đủ dữ liệu") → tự chọn size đó nếu còn hàng.
- **Giao diện**: xem Phụ lục 2.2 (thư viện ảnh, panel mua dính, accordion Mô tả / Chất liệu & bảo quản / Giao hàng & đổi trả lấy từ policies).
- **Nghiệm thu**: SP ẩn/nháp → trang 404 thân thiện; slug sai → 404; ảnh dùng `srcset` (kiểm tra trong DevTools chỉ tải bản `w800`/`w1200` đúng cỡ); đổi màu đổi ảnh.

## A4. Giỏ hàng ☐ (nối `CartPage.tsx`; badge số lượng ở `StoreLayout.tsx`)

- **API**: `GET /api/cart` · `POST /api/cart/items` `{variantId, quantity}` · `PATCH /api/cart/items/{variantId}` `{quantity?, selected?}` · `DELETE /api/cart/items/{variantId}` · `POST /api/cart/merge` `{items:[{variantId, quantity}]}`.
- **Item**: `{variantId, productId, productName, imageUrl, sku, size, color, hexCode, unitPrice, quantity, availableQuantity, lineTotal, isPurchasable, selected, priceAtAdd, priceChanged}`; giỏ có `subtotal` (mọi dòng mua được) và **`selectedSubtotal` / `selectedQuantity` (chỉ dòng được tick — dùng cho tạm tính/thanh toán)**.
- **Luồng**: khách chưa đăng nhập giữ giỏ trong localStorage (đã có ở mock); **ngay sau đăng nhập/đăng ký** gọi một lần `POST /cart/merge` với giỏ local rồi xóa giỏ local; hiển thị kết quả `results[].status` (`added`, `capped` "đã giảm theo tồn", `out_of_stock`, `unavailable`, `conflict`). Stepper số lượng gọi `PATCH` (debounce 400 ms, cập nhật lạc quan, hoàn lại nếu `409` kèm `availableQuantity` → "Chỉ còn N sản phẩm"). Checkbox gọi `PATCH {selected}`.
- **Giao diện**: Phụ lục 2.3 (thanh tiến độ freeship, tóm tắt dính, cảnh báo "Giá đã thay đổi từ X → Y", dòng hết hàng mờ + nút xóa).
- **Nghiệm thu**: đổi số lượng không nhảy layout; tick/bỏ tick đổi tổng; thêm quá tồn báo đúng; badge giỏ ở header = tổng `totalQuantity`.

## A5. Đơn của tôi ☐ (nối `Orders.tsx`, `OrderDetail.tsx`)

- **API**: `GET /api/customers/me/orders?status=&q=&from=&to=&page=&pageSize=` · `GET /api/customers/me/orders/counts` · `GET /api/customers/me/orders/{orderCode}` · `POST /api/customers/me/orders/{orderCode}/cancel` `{reason}`.
- **Tab → `status`**: Tất cả=`all`, Chờ xử lý=`active`, Đang giao=`shipping`, Đã giao=`delivered`, Đã hủy=`cancelled`, Giao thất bại=`delivery_failed` (gồm đang/đã hoàn về shop). Badge số lấy từ `counts` (`all, active, shipping, delivered, cancelled, deliveryFailed`).
- **Danh sách**: mỗi đơn là thẻ: mã, ngày, trạng thái (badge màu), tối đa 3 ảnh nhỏ từ `preview[]` + "+N sản phẩm", tổng tiền, nút "Xem chi tiết", "Hủy đơn" khi `canCancel`. Tìm theo mã (`q`), lọc ngày; 10 đơn/trang.
- **Chi tiết**: bảng sản phẩm (`imageUrl`, `productName`, size/màu, SL, đơn giá, giảm, thành tiền), tổng (`subtotal`, `discountAmount`, `shippingFee`, `totalAmount`, `couponCode`), địa chỉ giao, thanh toán, **timeline** dựng từ `timeline[]` (mỗi mốc có `status`, `at`; hủy kèm `note`), `deliveryAttempts[]`, `carrier`/`trackingCode`.
  - `bankTransfer` ≠ `null` (đơn chuyển khoản đang chờ): thẻ "Hướng dẫn chuyển khoản" — ngân hàng, STK, chủ TK, **nội dung = `transferContent` (mã đơn)**, số tiền; nút sao chép từng dòng. Chưa có nút "đã chuyển" — admin xác nhận thủ công.
  - Nút: "Hủy đơn" khi `canCancel` (modal lý do 10–300 ký tự); "Trả hàng" khi `canReturn` (hiện hạn `returnDeadline`) → luồng trả hàng cũ; "Viết đánh giá" cho item `reviewable`.
- **Lỗi**: hủy khi đơn đã sang đóng gói → `409 {message}` → hiện message và tải lại đơn. Đơn của người khác → `404`.
- **Nghiệm thu**: hủy xong tab "Đã hủy" tăng, "Chờ xử lý" giảm; số lượng trong kho admin được trả; thông báo trong chuông có "đơn … đã bị hủy".

## A5b. Màn "Hủy / Trả hàng" — đơn có thể hủy hoặc hoàn ☐ (thêm `src/features/orders/ReturnOrCancel.tsx`, route `/account/orders/actions`; chọn lọc ở `/account/returns/new` và `Returns.tsx`)

- **API**: `GET /api/customers/me/orders/actionable?action=all|cancel|return` (không phân trang, mới nhất trước).
  - `cancel` = đơn **chưa bàn giao vận chuyển** (`pending`, `confirmed`) → có `canCancel: true`.
  - `return` = đơn **đã giao** trong 7 ngày và còn sản phẩm chưa trả → `canReturn: true`, kèm `returnDeadline` và `returnableQuantity` (số SP còn trả được; yêu cầu trả bị từ chối thì SP được trả lại vào số này; trả hết thì đơn tự rời danh sách).
  - Đơn đang đóng gói / đang giao / giao thất bại / đã hủy / quá 7 ngày **không** có trong danh sách.
- **Response**: `{ items: [{ orderId, orderCode, status, placedAt, deliveredAt, totalAmount, paymentMethod, paymentStatus, itemCount, preview: [{productName, imageUrl, size, color, quantity}], canCancel, canReturn, returnDeadline, returnableQuantity }], cancellableCount, returnableCount, returnWindowDays }`.
- **Luồng / giao diện**: trang có hai tab "Có thể hủy (N)" và "Có thể trả hàng (N)" lấy `cancellableCount`/`returnableCount` (tải một lần với `action=all`, lọc theo `canCancel`/`canReturn` ở FE; hoặc gọi lại với `action=` tương ứng). Mỗi thẻ đơn: mã, ngày đặt (tab trả: ngày giao), tối đa 3 ảnh từ `preview` + "+N", tổng tiền.
  - Tab hủy: nút **Hủy đơn** → modal lý do 10–300 ký tự → `POST /api/customers/me/orders/{orderCode}/cancel` (A5); xong bỏ thẻ khỏi danh sách và tăng tab "Đã hủy".
  - Tab trả: dòng "Còn **X ngày** để trả hàng" tính từ `returnDeadline` (≤ 2 ngày hiện màu cảnh báo), "Còn trả được **N** sản phẩm"; nút **Trả hàng** → `/account/returns/new?order=<orderCode>` (form chọn SP lấy `GET …/orders/{orderCode}/returnable-items`).
  - Trạng thái rỗng: "Bạn không có đơn nào có thể hủy" / "…có thể trả hàng (đơn đã giao được trả trong 7 ngày)" + nút "Xem tất cả đơn hàng".
  - Có thể dùng làm nguồn cho nút "Hủy/Trả hàng" ở menu tài khoản kèm huy hiệu số (`cancellableCount + returnableCount`).
- **Lỗi**: `400 action` (giá trị khác `all|cancel|return`); danh sách là ảnh chụp tại lúc tải — bấm hủy khi đơn vừa sang đóng gói → `409 {message}` → hiện message và tải lại danh sách.
- **Nghiệm thu**: đơn pending/confirmed chỉ ở tab hủy; đơn giao 2 ngày trước chỉ ở tab trả; đơn đóng gói/giao thất bại/hủy/giao 10 ngày trước không xuất hiện; trả một phần → đơn vẫn còn với số lượng giảm; trả hết → biến mất; admin từ chối yêu cầu trả → đơn quay lại.

## A6. Đánh giá gắn đơn đã giao ☐ (nối `Reviews.tsx`, form ở chi tiết SP)

- **API**: `GET /api/customers/me/reviewable-items` · `POST /api/products/{id}/reviews` `{rating 1–5, comment 10–500}` · `GET /api/customers/me/reviews` · `DELETE /api/customers/me/reviews/{id}`.
- **Luồng**: trang "Đánh giá" có 2 tab: "Chờ đánh giá" (từ `reviewable-items`: ảnh, tên, size/màu, mã đơn, ngày giao, nút "Viết đánh giá") và "Đã đánh giá". Form: chọn sao + nội dung → `POST`. Mỗi SP một đánh giá (`409` nếu trùng). Trang chi tiết SP chỉ hiện form khi SP có trong `reviewable-items`.
- **Lưu ý**: đánh giá **đăng ngay** (khác bản mock "chờ duyệt"); admin ẩn/hiện ở màn Đánh giá admin. Sửa wording "đang chờ duyệt" nếu có.
- **Lỗi**: `409 {code:"REVIEW_NOT_PURCHASED"}` → "Bạn chỉ đánh giá được sản phẩm đã mua và đã nhận."

## A7. Đổi mật khẩu ☐ (thêm vào `Profile.tsx` hoặc trang `/account/password`)

- **API**: `POST /api/customers/me/password` `{currentPassword, newPassword}` → `{accessToken, expiresAt, user}`.
- **Luồng**: form 3 ô (hiện tại / mới / nhập lại — kiểm khớp ở FE; mới 8 ký tự..72 byte, khác cũ). Thành công: **thay token trong `sessionStorage` + `scheduleExpiry` bằng token mới** (dùng lại `setToken` ở `api.ts`), toast "Đã đổi mật khẩu". Mọi token cũ (tab khác) bị `401` ngay — FE đã tự đăng xuất khi 401.
- **Lỗi**: sai mật khẩu hiện tại là `400` key `currentPassword` (không phải 401) → hiện dưới ô đó.

## A8. CSS ảnh theo chuẩn mới ☐ (`src/App.css`, `ProductCard.tsx`, `ProductDetail.tsx`, các thumbnail)

Làm theo bảng và quy tắc ở **Phụ lục 2.5**: khung 3:4 ở thẻ SP và gallery (hiện gallery đang 4:5), thumbnail 60×80, ảnh giỏ 90×120, thanh toán 48×64, đơn/trả hàng/đánh giá 54×72; `srcset` + `sizes`; `object-position` theo `focalX/focalY`; placeholder khi thiếu ảnh; không phóng to ảnh nhỏ.

---

# PHẦN B — ADMIN (màn mới)

> Tất cả `/api/admin/*` cần token admin (`401` chưa đăng nhập, `403` nếu là khách). Lỗi, phân trang `?page=&pageSize=` (≤100) → `{items,page,pageSize,totalCount}`: xem file cũ mục 1.

## B1. Khung admin ☐ (L2, L3 làm trước)

- Route `/admin` (dashboard — API đã có ở nhánh trước), sidebar: Tổng quan · Đơn hàng · Trả hàng · Sản phẩm · Danh mục · **Chất liệu · Công dụng** · Bảng size · Size & màu · Tồn kho · **Nhà cung cấp · Nhập hàng** · Mã giảm giá · Khách hàng · Đánh giá · **Báo cáo**. Phụ lục 2.6 mô tả giao diện.
- Mục nào chưa làm hiện trang "Sắp có" thay vì 404.

## B2. Chất liệu & Công dụng ☐ (một component dùng cho cả hai)

- **API**: `GET/POST /api/admin/materials` · `GET/PATCH/DELETE /api/admin/materials/{id}`; y hệt cho `/api/admin/occasions`. Body `{name, slug?, description?, displayOrder?, status: active|hidden}`.
- **Màn**: bảng (thứ tự, tên, slug, mô tả, **số SP đang dùng** `productCount`, trạng thái, hành động). Nút "Thêm". Sửa trong drawer/modal. Ẩn/Hiện bằng switch (PATCH `status`). Sắp xếp thứ tự bằng ô số `displayOrder` (hoặc kéo thả rồi gửi lại thứ tự từng dòng).
- **Quy tắc**: tên 2–100 ký tự, **không trùng (không phân biệt hoa/thường, dấu)** → `400 name`; slug tự sinh từ tên nếu bỏ trống (chữ thường, số, gạch ngang); xóa khi còn SP dùng → `409 {message}` → gợi ý "Ẩn thay vì xóa". Ẩn → mất khỏi bộ lọc khách nhưng SP đang gắn vẫn giữ.
- **Nghiệm thu**: thêm "Du lịch" → xuất hiện ở bộ lọc khách (A2) và ở select của form SP (B3); ẩn → biến mất khỏi bộ lọc.

## B3. Sản phẩm: danh sách + form tạo/sửa ☐

- **API**: `GET /api/admin/products?q=&categoryId=&materialId=&occasionId=&status=&sort=&page=&pageSize=` · `POST /api/admin/products` · `GET/PATCH /api/admin/products/{id}` · `PATCH /api/admin/products/{id}/status` · `POST /api/admin/products/{id}/variants` · `PATCH/DELETE /api/admin/variants/{variantId}` (chi tiết ở file cũ B3/B4 + README).
- **Bảng**: ảnh nhỏ (`thumbnailUrl`), tên, danh mục, **chất liệu** (`materialName`), số biến thể, khoảng giá, tồn khả dụng, trạng thái, cập nhật. Lọc: tìm (tên/SKU), danh mục, **chất liệu, công dụng**, trạng thái. Hành động nhanh: Sửa, Ẩn/Hiện.
- **Form** (trang riêng `/admin/products/new`, `/admin/products/:id`), 3 thẻ: *Thông tin* — tên*, slug, danh mục*, mô tả (≥20 ký tự khi bán), **chất liệu\*** (`materialId`, select từ B2), **chi tiết chất liệu** (`materialDetail`, vd "Cotton 95%, spandex 5%", ≤150), **công dụng\*** (`occasionIds`, multi-chip ≤10), **bảo quản** (`careInstructions` ≤1000), giá gốc*, bảng size (`sizeChartId`, trống = dùng bảng mặc định của danh mục), trạng thái. *Ảnh* — B4. *Biến thể* — bảng (SKU*, size*, màu*, giá, trạng thái, tồn hiện tại **chỉ đọc**, nút Xóa/Ẩn); nút "Thêm biến thể" hoặc "Tạo ma trận size × màu" (sinh nhiều dòng, SKU gợi ý `<mã>-<size>-<màu>`).
- **Không còn `initialOnHand`**: dưới bảng biến thể có ghi chú "Tồn kho được thêm bằng Phiếu nhập hàng" + link B7.
- **Kích hoạt** (`status: active`): thiếu gì → `409 {message}` liệt kê (mô tả, chất liệu, công dụng, ảnh, biến thể) → hiện checklist "Còn thiếu" trong form thay vì chỉ toast. Nút "Đăng bán" disabled khi checklist chưa đủ (kiểm ở FE trước, BE vẫn kiểm lại).
- **Lỗi field**: `name, slug, categoryId, description, materialId, materialDetail, occasionIds, careInstructions, basePrice, sizeChartId, status, images[], variants[]` (hai key cuối là mảng message).
- **Nghiệm thu**: tạo SP đủ trường → bật bán → xuất hiện ở trang khách đúng bộ lọc chất liệu/công dụng; sửa tên → tìm kiếm khách (không dấu) ra tên mới.

## B4. Ảnh sản phẩm (upload theo chuẩn) ☐ (thẻ "Ảnh" trong B3)

- **API**: `POST /api/admin/uploads/images` (multipart, field `file`) → `201 {mediaId, url, width, height, aspectRatio, srcset, warnings[]}`; `PUT /api/admin/products/{id}/images` `{images:[{mediaId | url, altText?, colorId?, sortOrder?, isPrimary?, focalX?, focalY?}]}`; `GET /api/media-standard`.
- **Chuẩn**: ảnh dọc **3:4**, **tối thiểu 900×1200** (khuyến nghị 1200×1600), JPEG/PNG/WebP ≤ **20 MB**; nên có **≥ 3 ảnh** (chính, góc khác, cận chất liệu). BE giải mã thật, xoay theo EXIF, đổi sRGB, lưu webp + bản 400/800/1200.
- **Giao diện**: ô kéo-thả nhiều file; mỗi file hiện tiến trình + lỗi riêng (`400 file`: "dưới 900×1200", "không đọc được ảnh", "chỉ nhận JPEG/PNG/WebP", ">20 MB"). Lưới ảnh có: ảnh chính (huy hiệu "Ảnh chính"), kéo đổi thứ tự (`sortOrder`), chọn màu (`colorId` — để khách chọn màu nhảy ảnh), ô alt text, nút xóa, **click vào ảnh để đặt điểm lấy nét** (chấm tròn, lưu `focalX/focalY` 0–1) với **xem trước khung 3:4 (thẻ SP) và 1:1/4:5 nếu cần**. Hiện `warnings[]` (ít hơn 3 ảnh, ảnh không đúng 3:4) dưới lưới — **chỉ cảnh báo, không chặn**.
- **Luồng**: upload từng file ngay khi thả (nhận `mediaId`) → khi bấm Lưu mới `PUT` cả danh sách (ghi đè toàn bộ). SP đang bán phải còn ≥ 1 ảnh.
- **Nghiệm thu**: ảnh ngang 1600×1200 được nhận kèm cảnh báo 3:4; ảnh 800×1000 bị từ chối rõ lý do; ảnh điện thoại xoay sẵn hiện đúng chiều; trang khách tải đúng bản `srcset`.

## B5. Bảng size ☐

- **API**: `GET /api/admin/size-charts?categoryId=` · `GET/PUT/DELETE /api/admin/size-charts/{id}` · `POST /api/admin/size-charts` · `POST /api/admin/size-charts/{id}/duplicate`.
- **Màn**: danh sách (tên, danh mục, số dòng, số SP đang dùng, huy hiệu "Mặc định"). Form: tên, danh mục, mô tả, checkbox **"Bảng mặc định của danh mục"** (`isDefault`, cần chọn danh mục; mỗi danh mục một bảng mặc định — SP không có bảng riêng sẽ dùng bảng này), bảng dòng: nhãn size*, ngực, eo, mông, dài (cm), **chiều cao min–max (cm), cân nặng min–max (kg)**, ghi chú. Nút "Thêm dòng", xóa dòng, kéo thả thứ tự (`displayOrder`). Nút **Nhân bản** (hỏi tên mới, mặc định "… (bản sao)") → mở bảng mới để sửa.
- **Quy tắc**: 1–30 dòng, nhãn không trùng; số đo 0–300; cao 50–250, nặng 10–250, min ≤ max (lỗi `400 items` — mảng thông báo); xóa khi SP còn dùng → `409`. Sửa không đổi id dòng nếu giữ nhãn.
- **Nghiệm thu**: bảng có khoảng cao/nặng → trang khách "Tìm size" (A3) gợi ý đúng; đặt mặc định bảng mới → bảng cũ mất huy hiệu.

## B6. Size & màu ☐

- **API**: `GET/POST /api/admin/sizes` · `PATCH/DELETE /api/admin/sizes/{id}` (có `displayOrder`); `GET/POST /api/admin/colors` · `PATCH/DELETE /api/admin/colors/{id}` (`hexCode` `#RRGGBB`).
- **Màn**: hai bảng nhỏ. Size sắp xếp được (thứ tự dùng ở bộ lọc/chip size khách). Màu có ô chọn màu + chấm xem trước. Xóa khi còn biến thể (hoặc ảnh, với màu) dùng → `409` → gợi ý Ẩn.

## B7. Nhà cung cấp + Phiếu nhập hàng + In phiếu ☐ ⭐

Luồng: **Nhà cung cấp → Tạo phiếu nháp → (sửa) → Hoàn tất (tăng tồn) → In phiếu**.

- **API nhà cung cấp**: `GET/POST /api/admin/suppliers` · `GET/PATCH/DELETE /api/admin/suppliers/{id}`; trường `name*` (2–150, không trùng), `phone` (9–12 số, bỏ dấu cách/chấm), `email`, `address`, `taxCode` (10 số hoặc `0312345678-001`), `note`, `status`. Có phiếu rồi → không xóa được, chỉ ẩn (`409`).
- **API phiếu**: `GET /api/admin/goods-receipts?q=&status=&supplierId=&from=&to=&page=` · `POST /api/admin/goods-receipts` · `GET/PUT /api/admin/goods-receipts/{code}` · `POST …/{code}/complete` · `POST …/{code}/cancel` `{reason}` · `GET …/{code}/print`.
- **Danh sách phiếu**: tab trạng thái (Nháp / Đã nhập kho / Đã hủy), lọc NCC + khoảng ngày + ô tìm (mã phiếu, số hóa đơn NCC, tên NCC). Cột: mã `PN-1001`, NCC, số HĐ NCC, ngày nhập, số dòng, tổng SL, tổng tiền, trạng thái.
- **Form tạo/sửa (chỉ khi `draft`)**: NCC* (select, có "Thêm nhanh"), số hóa đơn NCC (≤50, **không trùng** với phiếu khác còn hiệu lực của cùng NCC), ngày nhập (không ở tương lai), ghi chú; bảng hàng: ô tìm biến thể (gọi `GET /api/admin/inventory?q=` → hiện SKU, tên, size, màu, tồn hiện tại), **số lượng** (1–100000), **đơn giá nhập** (≥0); tự tính thành tiền từng dòng và tổng (xem trước ở FE, BE tính lại). Một biến thể một dòng (gộp số lượng).
- **Chi tiết phiếu**: dùng `canEdit / canComplete / canCancel` bật nút. **Hoàn tất**: hộp xác nhận "Tồn kho sẽ tăng ngay và không hoàn tác được" → `complete` → phiếu khóa, mỗi dòng `applied: true`. Phiếu đã hoàn tất sai sót → dùng "Giảm tồn" (B8). Hủy chỉ cho nháp, lý do 10–300 ký tự. Bấm hoàn tất 2 lần/2 người cùng lúc → chỉ một lần có hiệu lực (`409` cho lần còn lại → hiện `message` rồi tải lại).
- **In phiếu**: endpoint cần token nên không dùng `<a href>`. Cách làm: `fetch` với `Authorization` (`request(..., 'text')` ở L1) → mở cửa sổ mới: `const w = window.open('', '_blank'); w.document.write(html); w.document.close();` (hoặc Blob `text/html` + `URL.createObjectURL`). Trang HTML đã có nút "In phiếu" gọi `window.print()` (khổ A4, tự ẩn nút khi in; có tên cửa hàng, NCC, bảng hàng, tổng tiền **bằng chữ**, ô ký). Đặt nút "In phiếu" ở cả danh sách và chi tiết (với nháp/hủy trang in có dấu "BẢN NHÁP"/"ĐÃ HỦY").
- **Nghiệm thu**: tạo phiếu 2 dòng → tồn các biến thể chưa đổi; hoàn tất → tồn tăng đúng, ở Tồn kho cột **giá vốn bình quân** (`averageCost`) và **giá trị tồn** (`stockValue`) cập nhật, lịch sử biến động có dòng "Nhập hàng PN-…"; nhập lần hai khác giá → giá vốn bình quân gia quyền; in ra đúng tổng và số tiền bằng chữ.

## B8. Tồn kho ☐ (nối bảng tồn kho — API từ nhánh trước + thay đổi mới)

- **API**: `GET /api/admin/inventory?q=&lowStock=&status=&page=` (hàng thêm `averageCost`, `stockValue`) · `POST …/{variantId}/adjustments` · `PATCH …/{variantId}` (`lowStockThreshold`) · `GET …/{variantId}/movements`.
- **Làm**: thêm cột **Giá vốn BQ** và **Giá trị tồn**; modal "Giảm tồn": `reasonCode` (select: Hỏng, Mất, Kiểm kê, Khác) + số lượng + lý do (10–300); lỗi `409` = giảm quá số khả dụng. Link "Nhập hàng" mở form phiếu nhập với biến thể đã chọn sẵn. Ngăn kéo lịch sử: hiện loại `receipt` (Nhập hàng, kèm mã phiếu từ `refId`/`reason`), `adjust_decrease`, `reserve`, `release`, `ship`, `restock`, `compensation` (ghi chú "hoàn tác").
- Giảm tồn tự động khi bán/hàng trả về đạt kiểm — không cần UI, chỉ hiện trong lịch sử.

## B9. Báo cáo doanh thu theo ngày / tuần / tháng / quý / năm ☐ ⭐

- **API**: `GET /api/admin/reports?from=&to=&groupBy=day|week|month|quarter|year` · `GET /api/admin/reports/export?from=&to=&groupBy=` (CSV) · `GET /api/admin/dashboard?from=&to=&groupBy=`.
- **Bộ chọn**: khoảng ngày (`yyyy-MM-dd`, giờ VN) + nhóm theo. Có preset "7 ngày / 30 ngày / Tháng này / Quý này / Năm nay / Tùy chọn". Giới hạn độ dài: nhóm ngày ≤ 366 ngày, tuần ≤ 731 ngày, tháng/quý/năm ≤ 5 năm — **chặn trước ở FE** (tự đổi nhóm gợi ý khi chọn khoảng dài), vượt vẫn `400 range`.
- **Hiển thị**: hàng thẻ KPI (Doanh thu gộp, Doanh thu thuần, Số đơn, Đơn đã giao, Số SP bán, Giá trị đơn TB; phụ: Tiền hàng, Phí ship, Giảm giá, Hoàn tiền, Đơn hủy) — mỗi thẻ kèm **mũi tên % so kỳ trước** từ `comparison.changePercent.*` (`null` → "—"; tăng xanh, giảm đỏ; `previousRange` ghi ở chú thích). Biểu đồ cột (`BarChart` SVG): X = `series[].label`, Y = doanh thu gộp/thuần (chuyển được), tooltip: kỳ, doanh thu, số đơn, SP, giá trị đơn TB. Bảng chi tiết theo kỳ (đủ cột, dòng TỔNG). Top 10 sản phẩm (`topProducts`), bảng sắp hết hàng (`lowStock`). Dấu "?" cạnh từng KPI hiện `definitions[...]` (tiếng Việt có sẵn).
- **Lưu ý dữ liệu**: mọi kỳ trong khoảng đều có điểm (kỳ trống = 0); kỳ đầu/cuối có thể chỉ tính **một phần** (ghi chú nhỏ nếu `start < from` hoặc `end > to`); `period` là khóa, **dùng `label` để hiển thị**.
- **Xuất CSV**: `fetch` có token, tải Blob; tên file lấy từ `Content-Disposition` (`report_month_2026-01-01_2026-11-23.csv`); UTF-8 có BOM, mở thẳng bằng Excel.
- **Dashboard**: tùy chọn `groupBy` (mặc định `day`).
- **Nghiệm thu**: cùng khoảng, tổng chuỗi theo tháng = tổng theo ngày = KPI; đổi nhóm không đổi KPI; CSV khớp bảng.

## B10. Đổi mật khẩu admin ☐

- **API**: `POST /api/admin/me/password` (cùng body và cách xử lý token như A7). Đặt trong menu người dùng ở góc trên admin.

---

# PHẦN C — Dành cho người làm API **đặt hàng** (không thuộc FE trong file này)

Đã có sẵn, hãy dùng lại:
- `Features/Orders/OrderDocuments.cs`; mã đơn `THM-<1000 + order_id>` (counter `orders`); item **nên ghi `image_url_snapshot`** (ảnh chính lúc đặt).
- `CouponService.QuoteAsync` (giá, giảm, ship tính ở server) + `CheckoutPricing` (ship 30.000đ, miễn phí khi tạm tính − giảm ≥ 500.000đ) → `RedeemAsync` → `InventoryService.ReserveAsync` từng dòng (thiếu tồn thì hoàn tác các dòng đã giữ) → ghi `order_status_history` (`pending`) + `NotificationService`.
- Chỉ lấy **dòng giỏ `selected`**, xóa dòng đã mua khỏi giỏ sau khi đặt; `payment_status`: COD=`unpaid`, chuyển khoản=`pending`; `Idempotency-Key` chống đặt đôi.
- Xem/hủy đơn của khách, danh sách/chi tiết **đã có** (A5) — không làm lại. Thông tin chuyển khoản: `Store__Bank*`.

---

# PHỤ LỤC

## 1. Thành phần đã có sẵn ở BE, FE chỉ việc dùng

`GET /api/storefront/policies` (ship, đổi trả, tài khoản chuyển khoản, chuẩn ảnh) · `GET /api/media-standard` · `GET /api/products/slug/{slug}` · `GET /api/size-charts/{id}` · `PATCH /api/admin/sizes/{id}` (`displayOrder`).

## 2. Hướng thiết kế UI (tham khảo web lớn, giữ theme coral cream + CSS thuần)

Nguyên tắc chung: nhiều khoảng trắng, ảnh là trọng tâm, chữ gọn; mọi danh sách có **skeleton khi tải**, **trạng thái rỗng có hướng dẫn**, **lỗi có nút Thử lại**; thao tác phá hủy luôn hỏi xác nhận; điều hướng được bằng bàn phím, `aria-label` cho icon, `role="status"`/`alert` cho thông báo; tối thiểu cỡ chạm 44 px trên mobile.

### 2.1 Danh sách sản phẩm (tham khảo Uniqlo, Zalando, Coolmate)
- Lưới 4 cột (≥1100px) / 3 / 2 cột mobile, khoảng cách 20–24px (mobile 10px). Thẻ: ảnh 3:4 → **hover đổi sang ảnh thứ hai** (Uniqlo/Zalando), dưới ảnh: chấm màu (tối đa 5, "+N" nếu nhiều) → tên **tối đa 2 dòng** → giá (hoặc "từ …"). Badge góc ảnh: "Mới" (≤14 ngày), "Hết hàng" (xám); không dùng nhiều badge cùng lúc.
- Thanh công cụ trên lưới: "N sản phẩm" bên trái, **select Sắp xếp** bên phải; bên dưới là **hàng chip bộ lọc đang chọn** (bấm × để bỏ, "Xóa tất cả").
- Bộ lọc: sidebar 240px dính (`position: sticky; top: 100px`) trên desktop, các nhóm **thu gọn được**: Danh mục · Khoảng giá (hai ô + các chip nhanh: `< 200k`, `200–400k`, `400–600k`, `> 600k`) · Size (chip vuông `S M L XL`, bật/tắt) · Chất liệu (checkbox) · Công dụng (checkbox) · "Chỉ hiện còn hàng" (switch). Mỗi mục kèm số SP (`productCount`) nếu có. Mobile: nút "Bộ lọc (N)" mở **bottom-sheet** toàn chiều rộng, nút "Xem N kết quả" cố định đáy.
- Phân trang số ở cuối, hoặc nút "Xem thêm" (giữ vị trí cuộn); khi đổi trang cuộn về đầu lưới. Skeleton là 8–12 thẻ ảnh 3:4 xám.

### 2.2 Chi tiết sản phẩm (tham khảo Uniqlo, Zalando, Routine)
- Desktop 2 cột: **trái** thư viện ảnh (cột thumbnail dọc 60×80 + ảnh lớn 3:4, mũi tên/nhấn để đổi, click mở xem lớn dùng ảnh gốc), **phải** panel mua `position: sticky`. Mobile: ảnh **vuốt ngang** có chấm trang.
- Panel mua: breadcrumb → tên → giá lớn → sao trung bình + "N đánh giá" (cuộn tới đánh giá) → **chấm màu tròn** (viền đậm khi chọn, tên màu bên cạnh) → **chip size** (hết hàng: gạch chéo + mờ, vẫn hiện) + liên kết "Hướng dẫn chọn size" → số lượng → nút "Thêm vào giỏ" (primary, full-width), thông báo "Còn N sản phẩm" khi ≤ 5.
- "Hướng dẫn chọn size": **drawer bên phải** có bảng số đo (cột: size, ngực, eo, mông, dài, cao, nặng), và ô **"Tìm size của bạn"** (chiều cao, cân nặng → kết quả "Gợi ý: M" + nút "Chọn size M").
- Dưới panel: **accordion** — Mô tả · Chất liệu & bảo quản (chất liệu, chi tiết chất liệu, `careInstructions`) · Phù hợp (chip công dụng, bấm = lọc danh sách theo công dụng) · Giao hàng & đổi trả (từ policies: phí ship, miễn phí từ …, đổi trả N ngày). Cuối trang: đánh giá (phân bố sao, danh sách) và "Sản phẩm cùng danh mục".

### 2.3 Giỏ hàng (tham khảo Coolmate, Uniqlo)
- Hai cột: danh sách dòng (checkbox chọn, ảnh 90×120, tên, size/màu, **stepper số lượng**, đơn giá, thành tiền, nút xóa) và **tóm tắt dính** bên phải (tạm tính chỉ tính dòng được tick, ô nhập mã/chọn voucher, phí ship, tổng, nút "Thanh toán").
- **Thanh tiến độ freeship** trên cùng: "Mua thêm 120.000đ để được miễn phí vận chuyển" (tính từ `freeShippingThreshold` − tạm tính; đủ thì "Bạn được miễn phí vận chuyển").
- Dòng `priceChanged`: ghi nhỏ "Giá đã thay đổi từ X → Y" màu cảnh báo; dòng `!isPurchasable`: mờ + "Tạm hết hàng", không tính vào tổng; có nút "Xóa các sản phẩm hết hàng".
- Giỏ trống: minh họa + nút "Tiếp tục mua sắm" + vài SP gợi ý mới nhất (`sort=newest`).

### 2.4 Đơn của tôi (tham khảo Shopee/Uniqlo account)
- Tab ngang cuộn được, có **badge số** từ `counts`. Mỗi đơn là một thẻ: dòng trên (mã đơn, ngày, badge trạng thái màu: chờ = vàng nhạt, đang giao = xanh dương, đã giao = xanh lá, hủy/thất bại = đỏ/xám), giữa (ảnh 54×72 tối đa 3 + "+N"), dưới (tổng tiền, nút "Xem chi tiết", "Hủy đơn").
- Chi tiết: **thanh bước (stepper)** Đặt hàng → Xác nhận → Đóng gói → Đang giao → Đã giao (nhánh hủy/thất bại hiển thị riêng), dưới là timeline có giờ. Thẻ "Chuyển khoản" nổi bật (nền `--accent-soft`) với nút **Sao chép** cho STK và nội dung chuyển khoản; không dùng ảnh QR ngoài trừ khi nhóm chọn dịch vụ VietQR.

### 2.5 Ảnh sản phẩm (chuẩn hiển thị)
Quy chuẩn: khung **dọc 3:4** (giống Uniqlo/Coolmate), ảnh nguồn ≥ 900×1200 (khuyến nghị 1200×1600); BE lưu webp + bản rộng 400/800/1200 px. Khung luôn `object-fit: cover`, điểm cắt theo `focalX/focalY` (mặc định giữa).

| Vị trí | Tỷ lệ / cỡ | Nguồn (`srcset`) | Ghi chú |
|---|---|---|---|
| Thẻ SP (`.product-photo`) | `aspect-ratio: 3/4` (đã đúng) | `w400` ≤600px, `w800` còn lại | `sizes="(max-width:600px) 50vw, (max-width:1100px) 33vw, 25vw"`, `loading="lazy"` |
| Gallery chính (`.gallery-main`) | **3/4** (đang 4/5 → đổi) | `w800` + `w1200` | `sizes="(max-width:900px) 100vw, 55vw"`; xem lớn = ảnh gốc `imageUrl` |
| Thumbnail gallery | 60×80 (đang 75×90 → đổi) | `w400` | |
| Ảnh "story" ở trang chi tiết | 3/4 (đang 4/5) | `w800` | |
| Giỏ hàng | 90×120 (mobile 68×90) | `w400` | |
| Thanh toán / tóm tắt đơn | 48×64 | `w400` | |
| Xem trước đơn, bảng SP trong đơn/trả hàng/đánh giá | 54×72 | `w400` | |
| Bảng admin | 48×64 | `w400` | |
| Danh mục, hero, banner | giữ tỷ lệ thiết kế riêng | — | không phải ảnh sản phẩm |

```css
.product-photo, .gallery-main { aspect-ratio: 3 / 4; overflow: hidden; background: var(--bg); }
.product-photo img, .gallery-main img { width: 100%; height: 100%; object-fit: cover;
  object-position: var(--fx, 50%) var(--fy, 50%); }  /* style={{'--fx': `${focalX*100}%`, '--fy': `${focalY*100}%`}} */
```
- Có `srcset` (`{w400, w800, w1200}`, chỉ gồm cỡ nhỏ hơn ảnh gốc) → `srcset="u400 400w, u800 800w"`; `srcset: null` (ảnh cũ `/images/products/…`) → chỉ `imageUrl`. Viết helper `imgProps(image, sizes)`.
- `width/height` có thể `null` → dùng `aspect-ratio` CSS, không dùng thuộc tính HTML.
- Thiếu ảnh / `onError`: khung 3:4 màu `--accent-soft` với icon, không để ô trống.

### 2.6 Khung admin (tham khảo Shopify/Haravan admin, nhưng gọn)
- Sidebar trái 232px (thu gọn còn icon dưới 1100px), nhóm menu có tiêu đề nhỏ; thanh trên có tên trang, ô tìm nhanh (tùy chọn), menu người dùng (đổi mật khẩu, đăng xuất).
- Nội dung: tiêu đề + hành động chính góc phải; **thanh lọc** một hàng trên bảng; bảng có header dính, hàng hover, cột số căn phải, cột trạng thái dùng `StatusBadge`; hành động hàng ở menu "⋯"; chi tiết mở **Drawer** phải thay vì chuyển trang khi nhỏ. Form dài chia thẻ, nút Lưu **dính ở đáy** và cảnh báo khi rời trang có thay đổi chưa lưu.
- Số tiền định dạng `1.234.000đ`, căn phải; ngày giờ giờ Việt Nam. Thẻ KPI: số lớn, nhãn nhỏ, chip % xanh/đỏ. Biểu đồ: cột `--primary`, lưới nhạt, tooltip; cho phép chuyển chỉ số.
- Hướng dẫn thao tác nguy hiểm (hoàn tất phiếu nhập, hủy, xóa) dùng `ConfirmDialog` nêu hậu quả bằng một câu.

## 3. Key lỗi mới cho `fieldMessages` (câu tiếng Việt gợi ý)

| Key | Câu gợi ý |
|---|---|
| `materialId` | Vui lòng chọn chất liệu hợp lệ. |
| `materialDetail` | Chi tiết chất liệu tối đa 150 ký tự. |
| `occasionIds` | Chọn từ 1 đến 10 công dụng hợp lệ. |
| `careInstructions` | Hướng dẫn bảo quản tối đa 1000 ký tự. |
| `name`, `slug` (chất liệu/công dụng/NCC) | Tên 2–100 ký tự và không trùng · Slug chỉ gồm chữ thường, số, dấu gạch ngang. *(Lưu ý: key `name` cũ đang ánh xạ sang "Từ khóa…" của tìm kiếm — đổi theo ngữ cảnh màn.)* |
| `description` | Mô tả tối đa 500 ký tự (chất liệu/công dụng); 20–3000 (sản phẩm đang bán). |
| `displayOrder` | Thứ tự hiển thị là số từ 0 đến 10000. |
| `isDefault` | Bảng mặc định phải thuộc một danh mục. |
| `items` (mảng) | Hiện từng dòng lỗi (bảng size / phiếu nhập: thiếu nhãn, trùng biến thể, số lượng/đơn giá sai, khoảng chiều cao–cân nặng sai). |
| `supplierId` | Chọn nhà cung cấp đang hoạt động. |
| `supplierInvoiceNo` | Số hóa đơn tối đa 50 ký tự và chưa dùng cho phiếu khác của nhà cung cấp này. |
| `receivedAt` | Ngày nhập không được ở tương lai. |
| `phone` / `email` / `taxCode` (NCC) | SĐT 9–12 số · Email không hợp lệ · Mã số thuế gồm 10 số (có thể kèm -NNN). |
| `reasonCode` | Chọn lý do: hỏng, mất, kiểm kê hoặc khác. |
| `quantity` | Số lượng từ 1 đến 100000 (giỏ hàng: 1–99). |
| `reason` | Lý do từ 10 đến 300 ký tự. |
| `currentPassword` | Mật khẩu hiện tại chưa đúng. |
| `newPassword` | Mật khẩu mới từ 8 ký tự (tối đa 72 byte) và khác mật khẩu cũ. |
| `heightCm` / `weightKg` | Chiều cao 50–250 cm · Cân nặng 10–250 kg. |
| `minPrice` / `maxPrice` | Giá không âm và giá tối đa không nhỏ hơn giá tối thiểu. |
| `q` / `sort` | Từ khóa tối đa 100 ký tự · Cách sắp xếp không hợp lệ. |
| `groupBy` / `range` / `from` / `to` | Nhóm theo ngày/tuần/tháng/quý/năm · Khoảng thời gian quá dài cho cách nhóm này · Ngày không hợp lệ. |
| `file` | Ảnh không đạt chuẩn (tối thiểu 900×1200, ≤ 20 MB, JPEG/PNG/WebP). |
| `status` | Trạng thái không hợp lệ. |

Lỗi theo mã: `409 {code:"REVIEW_NOT_PURCHASED"}` → "Bạn chỉ đánh giá được sản phẩm đã mua và đã nhận." · `429` + `Retry-After` → "Bạn thao tác quá nhanh, thử lại sau N giây." · `409 {message, availableQuantity}` → "Chỉ còn N sản phẩm." · các `409 {message}` khác (phiếu đã hoàn tất, hủy đơn sai trạng thái, xóa mục đang dùng…) → hiện nguyên `message` rồi tải lại dữ liệu.

## 4. Kiểu dữ liệu TypeScript gợi ý (`src/api/store.ts`, `src/api/admin.ts`)

```ts
export type ImageDto = { imageId: number; imageUrl: string | null; altText: string | null; colorId: number | null; sortOrder: number
  isPrimary: boolean; width: number | null; height: number | null; focalX: number | null; focalY: number | null
  srcset: { w400?: string; w800?: string; w1200?: string } | null }
export type RefDto = { id: number; name: string; slug: string }
export type ProductListItem = { productId: number; slug: string | null; name: string; categoryId: number; categoryName: string | null
  priceMin: number | null; priceMax: number | null; image: ImageDto | null; hoverImage: ImageDto | null
  colors: { colorId: number; name: string | null; hexCode: string | null }[]; sizes: string[]; inStock: boolean
  material: string | null; materialGroup: RefDto | null; occasions: RefDto[]; createdAt: string | null }
export type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number }
export type ProductQuery = { q?: string; category?: string; size?: string[]; material?: string[]; occasion?: string[]
  minPrice?: number; maxPrice?: number; inStock?: boolean; sort?: 'newest' | 'price_asc' | 'price_desc' | 'name_asc'; page?: number; pageSize?: number }
export type SizeSuggestion = { suggestedSize: string | null; matchType: 'exact' | 'closest' | 'none'; inStock: boolean
  candidates: { sizeLabel: string; distance: number }[] }
export type CartItem = { variantId: number; productId: number; productName: string; imageUrl: string | null; sku: string | null
  size: string | null; color: string | null; hexCode: string | null; unitPrice: number | null; quantity: number
  availableQuantity: number; lineTotal: number; isPurchasable: boolean; selected: boolean; priceAtAdd: number | null; priceChanged: boolean }
export type Cart = { cartId: number | null; items: CartItem[]; totalQuantity: number; subtotal: number; selectedQuantity: number; selectedSubtotal: number }
export type MergeResult = { cart: Cart; results: { variantId: number; requested: number; added: number; quantity: number
  status: 'added' | 'capped' | 'out_of_stock' | 'unavailable' | 'conflict' }[] }
export type MyOrderListItem = { orderId: number; orderCode: string; status: string; placedAt: string; totalAmount: number
  paymentMethod: string | null; paymentStatus: string | null; itemCount: number; canCancel: boolean
  preview: { productName: string | null; imageUrl: string | null; size: string | null; color: string | null; quantity: number }[] }
export type ActionableOrder = { orderId: number; orderCode: string; status: string; placedAt: string; deliveredAt: string | null
  totalAmount: number; paymentMethod: string | null; paymentStatus: string | null; itemCount: number
  preview: MyOrderListItem['preview']; canCancel: boolean; canReturn: boolean; returnDeadline: string | null; returnableQuantity: number }
export type ActionableOrders = { items: ActionableOrder[]; cancellableCount: number; returnableCount: number; returnWindowDays: number }
export type MyOrderCounts = { all: number; active: number; shipping: number; delivered: number; cancelled: number
  deliveryFailed: number; byStatus: Record<string, number> }
export type ReportPoint = { period: string; label: string; start: string; end: string; orderCount: number; deliveredCount: number
  cancelledCount: number; grossRevenue: number; merchandiseRevenue: number; shippingRevenue: number; discountAmount: number
  refundAmount: number; netRevenue: number; unitsSold: number; averageOrderValue: number }
export type ReportGroup = 'day' | 'week' | 'month' | 'quarter' | 'year'
export type GoodsReceiptItem = { variantId: number; productId: number; productName: string | null; sku: string | null
  size: string | null; color: string | null; quantity: number; unitCost: number; lineTotal: number; applied: boolean; currentOnHand: number | null }
export type GoodsReceiptRequest = { supplierId: number; supplierInvoiceNo?: string; receivedAt?: string; note?: string
  items: { variantId: number; quantity: number; unitCost: number }[] }
```

Response mẫu đầy đủ (thật, từ dữ liệu demo) xem trong `README.md` từng mục; thử nhanh bằng Swagger-less cách: đăng nhập admin demo rồi `curl -H "Authorization: Bearer <token>" http://localhost:8080/api/admin/reports?groupBy=month`.

## 5. Quy trình làm gợi ý (cho agent)

1. L1 → L5 (lõi) · 2. A1–A4 (storefront đọc) · 3. A5–A8 (đơn, đánh giá, mật khẩu, CSS ảnh) · 4. B1 (khung) → B2 → B6 → B5 → B3/B4 → B7 → B8 → B9 → B10.
Sau mỗi mục: chạy `npm run build` (type-check), thử luồng với dữ liệu demo, và tick ☑ ở checklist trên.
