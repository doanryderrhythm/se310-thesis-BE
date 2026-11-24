# Dữ liệu kiểm thử chức năng

Bộ `feature-tests-v1` đã được nạp mới vào Docker project `se310-integration` ngày 24/11/2026.
Đây là dữ liệu phát triển local, không phải dữ liệu kinh doanh.
Đã reset toàn bộ database `fashion-store-data` của project này theo yêu cầu. Project
`se310-thesis-be` ở cổng 8080 không bị thay đổi. Frontend `.env.local` dùng API cổng 8082.
Snapshot trước reset được lưu tại `.local-backups/before-reset-*.archive.gz` (không commit).
Catalog mới gồm 24 sản phẩm YaMe (6 áo thun dệt kim, 6 sơ mi, 6 quần dài, 6 áo khoác),
96 biến thể S/M/L/XL, 48 ảnh local trong repo frontend tại `public/images/seed-yame`.
Tên, giá và ảnh tham khảo website; tồn kho, bảng size, dữ liệu mua hàng và thông tin
người dùng là dữ liệu mô phỏng. Màu và nhóm chất liệu đối chiếu từ tên sản phẩm.
Nguồn và thời điểm tải từng sản phẩm: `mongo-init/mock-data/catalog-sources.json`.
Collection trực tiếp có thể khác trang hiển thị/cached theo thời điểm truy cập.
Sản phẩm lấy ảnh, chất liệu, bảng size và biến thể từ catalog hiện có; ID mới,
SKU `TEST-*`, slug `test-trai-nghiem-*`. Các bản ghi có `seed_suite` và `seed_key`.

## Bổ sung phụ kiện ngày 24/11/2026

Đã thêm độc lập **24 phụ kiện, 32 biến thể và 48 ảnh local** từ YaMe:
4 nón, 4 balo, 4 túi xách/túi đeo, 4 ví, 4 thắt lưng, 4 vớ.
Tổng sau hai lần nạp: 54 sản phẩm và 152 biến thể thuộc 10 danh mục.
Không sửa dữ liệu mua hàng, giỏ, tài khoản hoặc tồn kho có sẵn.

- Nón/balo/túi/ví: Freesize. Thắt lưng: 110 cm/120 cm. Vớ: 39–42/43–46.
- Size và tồn kho mô phỏng; tên, giá, màu và ảnh tham khảo YaMe.
- Trong mỗi nhóm: 2 sản phẩm còn 40/biến thể, 1 sản phẩm còn 2/biến thể,
  1 sản phẩm hết hàng. Tổng 8 biến thể tồn thấp và 8 hết hàng.
- Có màu phối, chất liệu da bò, giá thấp cho test voucher dưới mức tối thiểu;
  phụ kiện không dùng bảng số đo cơ thể của áo/quần.
- Chọn danh mục trên menu/bộ lọc; tìm `nón`, `ví`, `thắt lưng`, `vớ` hoặc từ không dấu.
  Thử đổi size, mua chung áo + phụ kiện, đổi số lượng vượt tồn và checkout hàng hết.
- Nguồn: `mongo-init/mock-data/accessories.json`. Ảnh trong frontend `public/images/seed-accessories`.
- Chạy `./scripts/seed-accessories.ps1` để bổ sung/xác minh; chạy lại không thay đổi
  dữ liệu đã cài. Reset toàn bộ bằng script reset cũng nạp lại phụ kiện.

Kiểm tra catalog/API phụ kiện: `node scripts/verify-accessories.mjs`.

## Tài khoản

Mật khẩu chung: **`Demo@12345`**.

| Số điện thoại | Mục đích |
| --- | --- |
| `0900000001` | Admin demo hiện có |
| `0900000021` | Nguyễn Minh An: tài khoản chính, đủ dữ liệu giỏ/đơn/đánh giá/voucher/đổi trả |
| `0900000022`–`0900000026` | Khách đã mua và đánh giá sản phẩm đầu tiên từ 1 đến 5 sao |
| `0900000027` | Khách mới: trạng thái rỗng, không có đơn/đánh giá/địa chỉ/giỏ |
| `0900000028` | Bị khóa: kiểm tra không đăng nhập được |

Đăng xuất tài khoản cũ và đăng nhập `0900000021` để thấy dữ liệu mới trong khu vực tài khoản.

## Các tình huống

| Khu vực | Dữ liệu / cách kiểm tra |
| --- | --- |
| Catalog | Tổng 30 sản phẩm, 120 biến thể: 24 sản phẩm nguồn và 6 sản phẩm tình huống (24 biến thể); 5 tình huống đang bán và 1 ẩn. Tìm “Bộ sưu tập trải nghiệm”. Có còn hàng, tồn thấp và hết hàng. Test phân trang, tìm có/không dấu, tên dài, giá, size, màu, chất liệu và danh mục. Ảnh lưu local. |
| Giỏ | 5 dòng: 2 dòng được chọn; một dòng giá lúc thêm cao hơn hiện tại; một biến thể còn 2; một hết hàng; một sản phẩm đã ẩn. Thử tick, tăng/giảm, xóa và cảnh báo không thể mua. |
| Checkout | Dùng 2 dòng được chọn, 2 địa chỉ (nhà/công ty), COD/chuyển khoản và mã giảm giá. Chỉ nhập các thông tin thanh toán đã được cấu hình thật trong Store settings. |
| Đơn hàng | `TEST-PENDING`, `TEST-CONFIRMED`, `TEST-PACKING`, `TEST-SHIPPING`, `TEST-DELIVERED`, `TEST-CANCELLED`, `TEST-DELIVERY_FAILED`, `TEST-RETURNING_TO_SELLER`, `TEST-RETURNED_TO_SELLER`. Mỗi đơn này có 3 dòng, ảnh, size/màu, số lượng, giá và timeline. Có COD và chuyển khoản. |
| Hủy đơn | `TEST-PENDING` hoặc `TEST-CONFIRMED`: hủy từ khách và kiểm tra tồn giữ chỗ được giải phóng. Các đơn chưa giao cho vận chuyển có thể xử lý tiếp từ admin theo quyền hiện có. |
| Trả hàng mới | `TEST-RETURN-6`: đã giao trong hạn và chưa có yêu cầu. `TEST-OUTSIDE-RETURN-WINDOW` đã giao 20 ngày trước, không được trả. |
| Đổi trả | `TEST-RTN-1` → `TEST-RTN-6` lần lượt: requested, awaiting_return, received, refund_pending, refunded, rejected. Có sản phẩm, lý do, sự kiện, số tiền và mã chuyển khoản khi đã hoàn. |
| Đánh giá công khai | `/products/test-trai-nghiem-1`: 5 đánh giá công khai 1–5 sao, điểm trung bình 3.0; mỗi bộ lọc sao có 1 kết quả. |
| Đánh giá cá nhân | Tài khoản chính đã có 1 đánh giá công khai và 1 bị ẩn; sản phẩm đầu tiên đã mua nhưng chưa đánh giá. Thử gửi, xóa và xét quyền chưa mua/đã đánh giá. |
| Voucher | `TEST10`: công khai giảm 10%, tối đa 100.000đ, đơn từ 300.000đ. `TEST50K`: ví giảm 50.000đ, đơn từ 300.000đ. `TESTSHIP`: miễn phí vận chuyển. `TESTEXPIRED`: hết hạn. `TESTUSED`: đã dùng, liên kết đơn và lịch sử sử dụng thật. `TESTDRAFT`: chiến dịch nháp, không được áp dụng. |
| Thông báo | 6 thông báo có nội dung thực tế, 3 đã đọc / 3 chưa đọc. Thử đánh dấu từng mục và tất cả. |
| Nhập kho | Nhà cung cấp “Xưởng may An Nhiên — kiểm thử”; `TEST-PN-1` nháp, `TEST-PN-2` hoàn tất, `TEST-PN-3` đã hủy. Phiếu hoàn tất đã cộng tồn và có movement; giữ biến thể khác tồn thấp để kiểm tra cảnh báo. |
| Dashboard / báo cáo | Đơn có doanh thu đã thu, giảm giá, phí giao hàng, giao thất bại và đổi trả. Lọc 30 ngày để xem bộ mới cùng dữ liệu demo cũ. |
| Phân quyền | Khách không được gọi endpoint admin; tài khoản khóa không đăng nhập. Khách mới không nhìn thấy dữ liệu cá nhân của tài khoản chính. |

## Reset về baseline mới để test lại

Lệnh này **xóa toàn bộ dữ liệu integration**, tạo backup trước khi xóa và nạp lại
catalog snapshot cùng demo/test fixtures, với ngày đơn/voucher tính lại theo thời điểm chạy:

```powershell
./scripts/reset-test-data.ps1
```

Không cần tải lại ảnh. Chỉ khi muốn cập nhật nguồn YaMe: `node scripts/import-yame-catalog.mjs`
(cần mạng; ghi lại catalog backend và ảnh frontend), sau đó reset như trên.
Để phục hồi backup: dừng API và dùng `mongorestore --drop --gzip --archive=<file>`
trong container MongoDB với tài khoản root của môi trường; khởi động API sau khi phục hồi.

## Checklist thao tác nhanh

Mỗi luồng có thể thay đổi baseline. Reset lại khi muốn bắt đầu lượt test độc lập.

| Test | Thao tác | Kết quả mong đợi |
| --- | --- | --- |
| Đăng nhập | Admin / khách chính / khách mới / tài khoản khóa | Đúng quyền; khách mới rỗng; tài khoản khóa bị từ chối |
| Tìm kiếm | Nhập `det kim`, `dệt kim`, rồi chuỗi không có sản phẩm | Tìm có/không dấu; chuỗi không khớp hiển thị trạng thái rỗng |
| Bộ lọc | Chọn danh mục, màu Đen/Be, size XL, chất liệu Linen, khoảng giá; xóa bộ lọc | Danh sách đúng điều kiện, giá XL có chênh 20.000đ; xóa khôi phục danh sách |
| Chi tiết | Mở một sản phẩm nguồn và `/products/test-trai-nghiem-1` | Ảnh, giá, size, màu, bảng size, đánh giá hiển thị; sản phẩm ẩn không bán công khai |
| Giỏ lỗi | Tăng dòng tồn 2 lên 3; chọn hàng hết/hàng ẩn; kiểm tra dòng đổi giá | Cảnh báo và không cho checkout hàng không thể mua |
| Checkout | Chỉ chọn hàng còn; đổi địa chỉ; thử COD và chuyển khoản | Tổng tiền khớp dòng hàng, phí ship và giảm giá; đơn tạo đúng tài khoản |
| Voucher | Lần lượt thử TEST10, TEST50K, TESTSHIP, TESTEXPIRED, TESTUSED, TESTDRAFT | Áp dụng đúng điều kiện; mã hết hạn/đã dùng/nháp bị từ chối; thử dưới mức tối thiểu |
| Đơn hàng | Mở từng tab trạng thái; mở chi tiết; hủy TEST-PENDING | Đủ dòng/timeline; hủy cập nhật trạng thái và giải phóng tồn giữ chỗ |
| Đổi trả | Dùng TEST-RETURN-6, sau đó thử TEST-OUTSIDE-RETURN-WINDOW | Đơn trong hạn tạo được yêu cầu; đơn ngoài hạn không đủ điều kiện; admin xử lý đúng bước |
| Đánh giá | Lọc 1–5 sao ở sản phẩm test 1; gửi đánh giá hàng đã giao chưa đánh giá | Mỗi sao có 1 đánh giá ban đầu, trung bình 3.0; chặn chưa mua và đánh giá trùng |
| Nhập kho | Hoàn tất TEST-PN-1 rồi thử hoàn tất lần nữa; xem TEST-PN-3 | Tồn cộng đúng một lần, có lịch sử; phiếu hủy không được nhập |
| Thông báo | Đọc một mục rồi đánh dấu tất cả đã đọc | Số chưa đọc giảm tương ứng, trạng thái giữ sau tải lại |
| Phân quyền | Khách truy cập admin; khách mới xem đơn khách chính | API từ chối; không lộ dữ liệu người khác |

Các bước ghi ở trên là checklist thủ công, chưa được chạy tự động trong lần nạp dữ liệu.

## Lệnh xác minh

Tại repo backend:

```powershell
./scripts/seed-feature-tests.ps1
./scripts/seed-feature-tests.ps1 -Verify
node scripts/verify-feature-data.mjs
```

Đổi Docker project bằng `-Project <name>`; API kiểm tra mặc định `http://127.0.0.1:8082/api`,
có thể thay bằng biến môi trường `API_BASE_URL`.

Seed chỉ cài một lần. Khi đã hoàn tất, chạy lại **không ghi dữ liệu**, kể cả khi bạn đã
sửa/xóa đơn, giỏ, đánh giá hay đổi mật khẩu trong lúc test. Không dùng seed `04` để
“refresh”: seed cũ đặt lại tồn kho toàn catalog. Không cần xóa volume hoặc reset database.

Kiểm tra baseline trước khi thao tác: 272 điều kiện liên kết, tiền, timeline, tồn giữ chỗ,
quyền đánh giá và hạn đổi trả; hash toàn bộ collection trước/sau seed bằng nhau.
Kiểm tra API: 32 request thực, không tạo giao dịch. Sau khi tự thao tác, baseline có thể
thay đổi hợp lệ (số sao, giỏ, số đơn…), vì vậy các assertion baseline không còn áp dụng nguyên vẹn.

Ngày của bộ dữ liệu được tính theo lần cài đầu tiên. Voucher và hạn đổi trả sẽ hết hạn
theo thời gian thật; chạy lại không gia hạn ngầm. Ảnh/video đánh giá và phản hồi shop
chưa có API nên bộ này không tạo dữ liệu ngoài contract đó. Không khẳng định mọi luồng
ghi đã được kiểm thử tự động; các tình huống trên dùng để tiếp tục kiểm thử thủ công.
