// Additive, local-only fixtures. Never rerun 04 to refresh these scenarios.
// Every document has a stable seed_key. Existing documents and UI edits are preserved.
(() => {
  const suite = "feature-tests-v1";
  if (db.seed_runs.findOne({ _id: suite })) {
    print("Feature test dataset already installed; all UI changes preserved (no writes).");
    return;
  }
  const now = new Date(), day = 86400000;
  const ago = (days) => new Date(now.getTime() - days * day);
  const money = (n) => Number(n);
  const ids = {};
  function next(collection, field) {
    if (!ids[collection]) {
      const last = db.getCollection(collection).find().sort({ [field]: -1 }).limit(1).toArray()[0];
      db.counters.updateOne({ _id: collection }, { $max: { sequence_value: last ? last[field] : 0 } }, { upsert: true });
      ids[collection] = true;
    }
    return db.counters.findOneAndUpdate({ _id: collection }, { $inc: { sequence_value: 1 } }, { returnDocument: "after" }).sequence_value;
  }
  function ensure(collection, field, key, data) {
    const seed_key = suite + ":" + key;
    const existing = db.getCollection(collection).findOne({ seed_key });
    if (existing) return existing;
    const doc = { ...data, [field]: next(collection, field), seed_suite: suite, seed_key };
    db.getCollection(collection).insertOne(doc);
    return doc;
  }
  const clean = (doc) => {
    const copy = { ...doc };
    for (const key of ["_id", "is_demo", "seed_key", "seed_suite"]) delete copy[key];
    return copy;
  };
  const phones = Array.from({ length: 8 }, (_, i) => "090000002" + (i + 1));
  if (db.users.countDocuments({ phone: { $in: phones }, seed_suite: { $ne: suite } }))
    throw new Error("Test phones already belong to another account; nothing changed.");
  const templates = db.products.find({ status: "active", seed_suite: { $exists: false } }).sort({ product_id: 1 }).limit(6).toArray();
  const ward = db.wards.findOne({ province_code: 79 });
  const admin = db.users.findOne({ phone: "0900000001", role: "admin" });
  if (templates.length < 6 || !ward || !admin) throw new Error("Run base catalog, locations and demo admin seeds first.");
  // Preflight all source data before making changes.
  for (const p of templates) {
    if (!db.product_variants.findOne({ product_id: p.product_id, status: "active" }) || !db.product_images.findOne({ product_id: p.product_id }))
      throw new Error("Source product needs an active variant and image: " + p.product_id);
  }
  const names = ["Nguyễn Minh An", "Trần Thảo Vy", "Lê Hoàng Phúc", "Phạm Ngọc Linh", "Võ Gia Huy", "Đặng Khánh Chi", "Khách mới kiểm thử", "Tài khoản tạm khóa"];
  const users = names.map((name, i) => ensure("users", "user_id", "user-" + i, {
    full_name: name, phone: phones[i], email: "test" + (i + 1) + "@example.com",
    password_hash: "$2a$12$bHsWM8LRioI/BhlOH2DS9O6tUn97XXImhv8EWgVMtuEFuG6GO5zUS",
    role: "customer", status: i === 7 ? "locked" : "active",
    ...(i === 7 ? { status_reason: "Tài khoản kiểm thử phân quyền bị khóa", status_changed_at: ago(1) } : {}),
    created_at: ago(60), updated_at: now,
  }));
  const district = db.districts.findOne({ district_code: ward.district_code });
  const province = db.provinces.findOne({ province_code: ward.province_code });
  const address = (user, street) => ({ recipient_name: user.full_name, recipient_phone: user.phone,
    address_line: street, ward: ward.name, district: district.name, province: province.name,
    ward_code: ward.ward_code, district_code: ward.district_code, province_code: ward.province_code });
  const home = address(users[0], "125 Nguyễn Thị Minh Khai, căn hộ A12.08");
  ensure("addresses", "address_id", "home", { user_id: users[0].user_id, ...home, is_default: true, created_at: ago(30) });
  ensure("addresses", "address_id", "office", { user_id: users[0].user_id, ...address(users[0], "36 Lê Duẩn, tầng 8 — nhận hàng giờ hành chính"), is_default: false, created_at: ago(20) });

  const products = [], variants = [], images = [];
  templates.forEach((source, i) => {
    const product = ensure("products", "product_id", "product-" + i, {
      ...clean(source), name: source.name + " — Bộ sưu tập trải nghiệm", slug: "test-trai-nghiem-" + (i + 1),
      status: i === 5 ? "hidden" : "active", created_at: ago(20 - i), updated_at: now,
    });
    products.push(product);
    const sourceImages = db.product_images.find({ product_id: source.product_id }).sort({ sort_order: 1 }).toArray();
    sourceImages.forEach((image, j) => ensure("product_images", "image_id", `image-${i}-${j}`, { ...clean(image), product_id: product.product_id }));
    images.push(sourceImages[0].image_url);
    const sourceVariants = db.product_variants.find({ product_id: source.product_id, status: "active" }).sort({ variant_id: 1 }).limit(4).toArray();
    variants[i] = sourceVariants.map((v, j) => {
      const variant = ensure("product_variants", "variant_id", `variant-${i}-${j}`, {
        ...clean(v), product_id: product.product_id, sku: `TEST-P${i + 1}-${j + 1}`, status: "active", created_at: ago(20), updated_at: now,
      });
      ensure("inventory", "inventory_id", `stock-${i}-${j}`, {
        variant_id: variant.variant_id, quantity_on_hand: i === 3 ? 2 : i === 4 ? 0 : 100,
        quantity_reserved: 0, low_stock_threshold: 5, average_cost: Math.round(money(variant.price) * .45), updated_at: now,
      });
      return variant;
    });
  });
  const sizes = Object.fromEntries(db.sizes.find().toArray().map(s => [s.size_id, s.name]));
  const colors = Object.fromEntries(db.colors.find().toArray().map(c => [c.color_id, c.name]));
  const paths = {
    pending: ["pending"], confirmed: ["pending", "confirmed"], packing: ["pending", "confirmed", "packing"],
    shipping: ["pending", "confirmed", "packing", "shipping"], delivered: ["pending", "confirmed", "packing", "shipping", "delivered"],
    cancelled: ["pending", "cancelled"], delivery_failed: ["pending", "confirmed", "packing", "shipping", "delivery_failed"],
    returning_to_seller: ["pending", "confirmed", "packing", "shipping", "delivery_failed", "returning_to_seller"],
    returned_to_seller: ["pending", "confirmed", "packing", "shipping", "delivery_failed", "returning_to_seller", "returned_to_seller"],
  };
  const orders = [];
  function makeOrder(key, user, status, age, productIndices, bank = false, discount = 0, couponId = null) {
    const lines = productIndices.map((pi, li) => {
      const v = variants[pi][0];
      return { v, pi, quantity: li === 0 ? 2 : 1 };
    });
    const subtotal = lines.reduce((sum, l) => sum + money(l.v.price) * l.quantity, 0);
    const fee = subtotal - discount >= 500000 ? 0 : 30000;
    const path = paths[status], placed = ago(age), end = ago(Math.max(0.05, age - .8));
    const paid = ["delivered", "returned_to_seller"].includes(status) || (bank && path.includes("packing"));
    const order = ensure("orders", "order_id", "order-" + key, {
      order_code: "TEST-" + key.toUpperCase(), user_id: user.user_id, status,
      payment_method: bank ? "bank_transfer" : "cod", payment_status: status === "returned_to_seller" ? "refund_pending" : paid ? "paid" : bank ? "pending" : "unpaid",
      recipient_name: user.full_name, recipient_phone: user.phone,
      shipping_address_line: home.address_line, shipping_ward: home.ward, shipping_district: home.district, shipping_province: home.province,
      shipping_ward_code: home.ward_code, shipping_district_code: home.district_code, shipping_province_code: home.province_code,
      subtotal, discount_amount: discount, coupon_id: couponId, shipping_fee: fee, total_amount: subtotal - discount + fee,
      customer_note: "Dữ liệu test: gọi trước khi giao, không giao sau 18 giờ.",
      cancel_reason: status === "cancelled" ? "Đặt nhầm kích thước, cần đặt lại sản phẩm khác." : null,
      placed_at: placed, updated_at: end, delivered_at: status === "delivered" ? end : null, paid_at: paid ? end : null,
      carrier: path.includes("shipping") ? "GHN" : null, tracking_code: path.includes("shipping") ? "TEST-GHN-" + key.toUpperCase() : null,
      delivery_attempts: path.includes("delivery_failed") ? [{ attempt_number: 1, attempted_at: end, reason: "unreachable", note: "Gọi hai lần chưa liên lạc được, khách hẹn giao lại ngày mai." }] : [],
      return_inspection: status === "returned_to_seller" ? "passed" : null,
    });
    const orderLines = lines.map((l, i) => {
      const amount = i === 0 ? discount : 0;
      return ensure("order_items", "order_item_id", `line-${key}-${i}`, {
        order_id: order.order_id, variant_id: l.v.variant_id, product_id: products[l.pi].product_id,
        product_name_snapshot: products[l.pi].name, sku_snapshot: l.v.sku, image_url_snapshot: images[l.pi],
        size_snapshot: sizes[l.v.size_id], color_snapshot: colors[l.v.color_id], quantity: l.quantity,
        unit_price: money(l.v.price), discount_amount: amount, line_total: money(l.v.price) * l.quantity - amount,
      });
    });
    path.forEach((s, i) => ensure("order_status_history", "history_id", `history-${key}-${i}`, {
      order_id: order.order_id, old_status: i ? path[i - 1] : null, new_status: s, changed_by: i ? admin.user_id : user.user_id,
      note: i ? "Cập nhật trạng thái cho tình huống kiểm thử." : "Khách đặt hàng thành công.",
      changed_at: new Date(placed.getTime() + (end - placed) * i / Math.max(1, path.length - 1)),
    }));
    // Movement marker makes stock effects idempotent, even when rerun after UI operations.
    orderLines.forEach((line, i) => {
      if (db.inventory_movements.findOne({ seed_key: suite + `:sale-${key}-${i}` })) return;
      const reserve = ["pending", "confirmed", "packing"].includes(status) ? line.quantity : 0;
      const shipped = ["shipping", "delivered", "delivery_failed", "returning_to_seller"].includes(status) ? line.quantity : 0;
      if (!reserve && !shipped) return;
      const inv = db.inventory.findOneAndUpdate({ variant_id: line.variant_id }, { $inc: { quantity_on_hand: NumberInt(-shipped), quantity_reserved: NumberInt(reserve) } }, { returnDocument: "after" });
      ensure("inventory_movements", "movement_id", `sale-${key}-${i}`, { variant_id: line.variant_id, type: reserve ? "reserve" : "ship",
        delta_on_hand: NumberInt(-shipped), delta_reserved: reserve, on_hand_after: inv.quantity_on_hand, reserved_after: inv.quantity_reserved,
        reason: "Đơn kiểm thử " + order.order_code, ref_type: "order", ref_id: order.order_id, actor_id: admin.user_id, created_at: end });
    });
    return { order, lines: orderLines };
  }
  Object.keys(paths).forEach((status, i) => orders.push(makeOrder(status, users[0], status, 1 + i * .15, [0, 1, 2], i % 2 === 1)));
  // Independent delivered purchases leave real opportunities to write reviews and open returns.
  const returnOrders = Array.from({ length: 7 }, (_, i) => makeOrder("return-" + i, users[0], "delivered", 3 + i * .1, [1, 2]));
  const expiredOrder = makeOrder("outside-return-window", users[0], "delivered", 20, [2]);
  const comments = ["Đường may chưa đều, phần cổ bị lệch sau lần giặt đầu tiên.", "Chất vải hơi mỏng so với mong đợi, màu thực tế nhạt hơn ảnh.", "Sản phẩm sử dụng ổn, phom hơi rộng nên cân nhắc bảng kích thước.", "Chất vải mềm, màu đúng ảnh, đóng gói cẩn thận và giao đúng hẹn.", "Rất hài lòng: đường may gọn, mặc thoáng mát, sẽ tiếp tục ủng hộ shop."];
  users.slice(1, 6).forEach((user, i) => {
    const purchase = makeOrder("review-" + (i + 1), user, "delivered", 4 + i, [0]);
    ensure("product_reviews", "review_id", "review-" + i, { product_id: products[0].product_id, user_id: user.user_id,
      order_item_id: purchase.lines[0].order_item_id, rating: i + 1, comment: comments[i], status: "published", created_at: ago(2 + i * .1) });
  });
  ensure("product_reviews", "review_id", "my-published", { product_id: products[1].product_id, user_id: users[0].user_id,
    order_item_id: orders[4].lines[1].order_item_id, rating: 4, comment: comments[3], status: "published", created_at: ago(.5) });
  ensure("product_reviews", "review_id", "my-hidden", { product_id: products[2].product_id, user_id: users[0].user_id,
    order_item_id: orders[4].lines[2].order_item_id, rating: 3, comment: "Màu sắc và kích thước cần được mô tả kỹ hơn trên trang sản phẩm.", status: "hidden", created_at: ago(.4) });
  ["requested", "awaiting_return", "received", "refund_pending", "refunded", "rejected"].forEach((status, i) => {
    const purchase = returnOrders[i], line = purchase.lines[0], refund = line.line_total / line.quantity;
    const flow = status === "rejected" ? ["requested", "rejected"] : ["requested", "awaiting_return", "received", "refund_pending", "refunded"].slice(0, ["requested", "awaiting_return", "received", "refund_pending", "refunded"].indexOf(status) + 1);
    const returned = ensure("returns", "return_id", "return-" + i, {
      return_code: "TEST-RTN-" + (i + 1), order_id: purchase.order.order_id, order_code: purchase.order.order_code, user_id: users[0].user_id,
      status, reason: i % 2 ? "wrong_size" : "defective", description: i % 2 ? "Size thực tế rộng hơn bảng kích thước, sản phẩm còn nguyên tem." : "Đường chỉ ở tay áo bị bung khi mở kiện hàng, đề nghị shop kiểm tra.",
      items: [{ order_item_id: line.order_item_id, variant_id: line.variant_id, product_name_snapshot: line.product_name_snapshot,
        sku_snapshot: line.sku_snapshot, size_snapshot: line.size_snapshot, color_snapshot: line.color_snapshot,
        quantity: 1, unit_price: line.unit_price, allocated_discount: 0, estimated_refund: refund }],
      estimated_refund: refund, refund_amount: status === "refunded" ? refund : null,
      refund_reference: status === "refunded" ? "TEST-CK-0005" : null, reject_reason: status === "rejected" ? "Sản phẩm đã qua sử dụng và không còn tem gốc." : null,
      assignee_id: i ? admin.user_id : null, requested_at: ago(1.8), updated_at: ago(.2), refunded_at: status === "refunded" ? ago(.2) : null,
      events: flow.map((s, j) => ({ status: s, actor_id: j ? admin.user_id : users[0].user_id, note: j ? "Xử lý yêu cầu kiểm thử theo quy trình." : "Khách gửi yêu cầu trả hàng.", at: ago(1.8 - j * .3) })),
    });
    if (["refund_pending", "refunded"].includes(status) && !db.inventory_movements.findOne({ seed_key: suite + ":restock-" + i })) {
      const inv = db.inventory.findOneAndUpdate({ variant_id: line.variant_id }, { $inc: { quantity_on_hand: NumberInt(1) } }, { returnDocument: "after" });
      ensure("inventory_movements", "movement_id", "restock-" + i, { variant_id: line.variant_id, type: "restock", delta_on_hand: 1, delta_reserved: 0,
        on_hand_after: inv.quantity_on_hand, reserved_after: inv.quantity_reserved, reason: "Hàng trả đạt kiểm tra", ref_type: "return", ref_id: returned.return_id, actor_id: admin.user_id, created_at: ago(.3) });
    }
  });
  const promotionPlans = [
    ["TEST10", "Giảm 10% trải nghiệm", "percent", 10, "public_code", "active", -30],
    ["TEST50K", "Voucher giảm 50.000đ", "fixed", 50000, "issued", "active", -30],
    ["TESTSHIP", "Miễn phí vận chuyển", "free_shipping", 0, "issued", "active", -30],
    ["TESTEXPIRED", "Voucher đã hết hạn", "fixed", 30000, "issued", "ended", 1],
    ["TESTUSED", "Voucher đã sử dụng", "fixed", 20000, "issued", "active", -30],
    ["TESTDRAFT", "Chiến dịch đang soạn", "percent", 15, "public_code", "draft", -30],
  ];
  promotionPlans.forEach(([code, name, type, value, distribution, status, endDays], i) => {
    const campaign = ensure("campaigns", "campaign_id", "campaign-" + i, { name, description: "Chiến dịch phục vụ kiểm thử, không gửi email ra ngoài.",
      trigger: distribution === "issued" ? "manual" : "none", distribution, status, starts_at: ago(30), ends_at: ago(endDays), created_by: admin.user_id, created_at: ago(30), updated_at: now });
    const coupon = ensure("coupons", "coupon_id", "coupon-" + i, { campaign_id: campaign.campaign_id, code, discount_type: type, discount_value: value,
      max_discount_amount: type === "percent" ? 100000 : null, minimum_order_amount: type === "free_shipping" ? 0 : 300000,
      applies_to: "all", category_ids: [], starts_at: ago(30), expires_at: ago(endDays), usage_limit: 100, usage_count: i === 4 ? 1 : 0,
      per_user_limit: 1, valid_days_after_issue: distribution === "issued" ? 30 : null, status, created_at: ago(30), updated_at: now });
    if (distribution !== "issued") return;
    let used = null; if (i === 4) used = makeOrder("voucher-used", users[0], "delivered", 5, [0, 1], false, value, coupon.coupon_id);
    const wallet = ensure("user_coupons", "user_coupon_id", "wallet-" + i, { coupon_id: coupon.coupon_id, campaign_id: campaign.campaign_id, user_id: users[0].user_id,
      status: used ? "used" : "available", issued_at: ago(15), expires_at: ago(endDays), used_order_id: used ? used.order.order_id : null, used_at: used ? ago(5) : null });
    if (used) ensure("coupon_usages", "coupon_usage_id", "usage-" + i, { coupon_id: coupon.coupon_id, user_coupon_id: wallet.user_coupon_id,
      order_id: used.order.order_id, user_id: users[0].user_id, discount_amount: value, used_at: ago(5), status: "applied" });
  });
  const cart = ensure("carts", "cart_id", "cart", { user_id: users[0].user_id, status: "active", created_at: ago(1), updated_at: now });
  [0, 1, 3, 4, 5].forEach((pi, i) => ensure("cart_items", "cart_item_id", "cart-item-" + i, {
    cart_id: cart.cart_id, variant_id: variants[pi][0].variant_id, quantity: pi === 0 ? 2 : 1,
    selected: pi === 0 || pi === 1, added_at: ago(.1), price_at_add: money(variants[pi][0].price) + (pi === 1 ? 30000 : 0),
  }));
  ["Đơn hàng đã được xác nhận", "Đơn hàng đang giao đến bạn", "Shop đã nhận yêu cầu trả hàng", "Bạn được tặng voucher giảm 50.000đ", "Hoàn tiền thành công", "Hãy chia sẻ trải nghiệm sản phẩm"].forEach((title, i) =>
    ensure("notifications", "notification_id", "notification-" + i, { user_id: users[0].user_id, type: i === 3 ? "voucher" : "order", title,
      body: ["Shop đang chuẩn bị sản phẩm. Bạn có thể xem địa chỉ và các món đã đặt trong chi tiết đơn hàng.", "Vui lòng giữ điện thoại để đơn vị giao hàng liên hệ.", "Shop sẽ kiểm tra hàng trả trước khi xác nhận hoàn tiền.", "Voucher TEST50K có hiệu lực cho đơn từ 300.000đ. Xem ví voucher để biết thời hạn.", "Shop đã xử lý hoàn tiền cho yêu cầu TEST-RTN-5.", "Sản phẩm đã giao thành công. Bạn có thể đánh giá trong mục Đánh giá của tôi."][i],
      read_at: i % 2 ? ago(.2) : null, created_at: ago(1 + i * .1) }));
  const supplier = ensure("suppliers", "supplier_id", "supplier", { name: "Xưởng may An Nhiên — kiểm thử", phone: "0901234567", email: "xuong.test@example.com",
    address: "82 Tân Kỳ Tân Quý, TP. Hồ Chí Minh", tax_code: "0312345000", note: "Nhà cung cấp dành cho kiểm thử phiếu nhập.", status: "active", created_at: ago(20), updated_at: now });
  ["draft", "completed", "cancelled"].forEach((status, i) => {
    const v = variants[3][0], quantity = 10, cost = Math.round(money(v.price) * .45);
    const receipt = ensure("goods_receipts", "receipt_id", "receipt-" + i, { receipt_code: "TEST-PN-" + (i + 1), supplier_id: supplier.supplier_id, supplier_name: supplier.name,
      status, received_at: ago(2), items: [{ variant_id: v.variant_id, product_id: v.product_id, product_name: products[3].name, sku: v.sku,
        size: sizes[v.size_id], color: colors[v.color_id], quantity, unit_cost: cost, line_total: quantity * cost, applied: status === "completed" }],
      total_quantity: quantity, total_amount: quantity * cost, created_by: admin.user_id, created_at: ago(2), updated_at: ago(1),
      ...(status === "completed" ? { completed_at: ago(1), completed_by: admin.user_id } : {}),
      ...(status === "cancelled" ? { cancelled_at: ago(1), cancel_reason: "Nhà cung cấp giao sai màu, hủy phiếu nháp." } : {}),
      note: "Phiếu nhập kiểm thử; phiếu hoàn tất nhập vào biến thể thứ hai để giữ tình huống tồn thấp.",
    });
    if (status === "completed" && !db.inventory_movements.findOne({ seed_key: suite + ":receipt-movement" })) {
      // Dedicated second variant receives the shipment, so the cart's first variant stays low.
      const target = variants[3][1] || v;
      db.goods_receipts.updateOne({ receipt_id: receipt.receipt_id }, { $set: { "items.0.variant_id": target.variant_id, "items.0.sku": target.sku,
        "items.0.size": sizes[target.size_id], "items.0.color": colors[target.color_id] } });
      const inv = db.inventory.findOneAndUpdate({ variant_id: target.variant_id }, { $inc: { quantity_on_hand: NumberInt(quantity) } }, { returnDocument: "after" });
      ensure("inventory_movements", "movement_id", "receipt-movement", { variant_id: target.variant_id, type: "receipt", delta_on_hand: quantity, delta_reserved: 0,
        on_hand_after: inv.quantity_on_hand, reserved_after: inv.quantity_reserved, reason: "Phiếu nhập " + receipt.receipt_code,
        ref_type: "goods_receipt", ref_id: receipt.receipt_id, actor_id: admin.user_id, created_at: ago(1) });
    }
  });
  // Finish a partially interrupted FIRST run from its movement ledger. Once complete,
  // reruns never reset stock, orders, passwords, reviews or any other UI changes.
  if (!db.seed_runs.findOne({ _id: suite })) {
    variants.forEach((rows, pi) => rows.forEach(v => {
      let onHand = pi === 3 ? 2 : pi === 4 ? 0 : 100, reserved = 0;
      db.inventory_movements.find({ seed_suite: suite, variant_id: v.variant_id }).sort({ movement_id: 1 }).toArray().forEach(m => {
        onHand += money(m.delta_on_hand); reserved += money(m.delta_reserved);
        db.inventory_movements.updateOne({ _id: m._id }, { $set: { on_hand_after: NumberInt(onHand), reserved_after: NumberInt(reserved) } });
      });
      if (onHand < reserved || reserved < 0) throw new Error("Invalid fixture inventory: " + v.sku);
      db.inventory.updateOne({ variant_id: v.variant_id }, { $set: { quantity_on_hand: NumberInt(onHand), quantity_reserved: NumberInt(reserved) } });
    }));
    // Correct only this suite's interrupted first-run product state.
    db.products.updateOne({ seed_key: suite + ":product-5" }, { $set: { status: "hidden" } });
    db.seed_runs.insertOne({ _id: suite, completed_at: now });
  }
  print(JSON.stringify({ suite, customer: phones[0], emptyCustomer: phones[6], lockedCustomer: phones[7], password: "Demo@12345",
    reviewProductId: Number(products[0].product_id), reviewProductSlug: products[0].slug, outsideReturnWindow: expiredOrder.order.order_code,
    counts: Object.fromEntries(["users", "products", "product_variants", "orders", "returns", "product_reviews", "user_coupons", "notifications", "goods_receipts"].map(c => [c, db.getCollection(c).countDocuments({ seed_suite: suite })])) }, null, 2));
})();
