// Demo data for the admin dashboard: users, orders (every status), returns and reviews.
// Run AFTER 01-schema.js, 02-seed-products.js, 02a-seed-locations.js and 03-seed-campaigns.js. Safe to rerun: it
// replaces everything it created before (documents carry is_demo: true) and recomputes the
// inventory of every variant from the base stock, so stock edits made in the admin UI are reset.
//
// Dates are relative to the moment the script runs, so the dashboard always has "recent" data.
// Demo login (all demo accounts): password  Demo@12345
//   admin     0900000001
//   customers 0900000011 .. 0900000018   (0900000018 is locked)

const DEMO_PASSWORD_HASH = "$2a$12$bHsWM8LRioI/BhlOH2DS9O6tUn97XXImhv8EWgVMtuEFuG6GO5zUS";
const NOW = new Date();
const HOUR = 3600 * 1000;
const DAY = 24 * HOUR;

// ---- deterministic randomness ------------------------------------------------------------
let seed = 20261123;
function rnd() {
  seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
  let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}
const pick = (list) => list[Math.floor(rnd() * list.length)];
const between = (lo, hi) => lo + rnd() * (hi - lo);

function maxId(collection, field) {
  const top = db.getCollection(collection).find({}, { [field]: 1 }).sort({ [field]: -1 }).limit(1).toArray();
  return top.length ? top[0][field] : 0;
}

// ---- 0. safety checks ------------------------------------------------------------------------
// Never touch a real account: abort if a demo phone number belongs to a non-demo user.
const DEMO_PHONES = ["0900000001", "0900000011", "0900000012", "0900000013", "0900000014",
                     "0900000015", "0900000016", "0900000017", "0900000018"];
const taken = db.users.find({ phone: { $in: DEMO_PHONES }, is_demo: { $ne: true } }, { phone: 1 }).toArray();
if (taken.length > 0) {
  throw new Error("Demo seed aborted: these phone numbers belong to real accounts: " +
    taken.map((u) => u.phone).join(", ") + ". Nothing was changed.");
}
if (db.wards.countDocuments({ province_code: 79 }) === 0) {
  throw new Error("Demo seed aborted: run 02a-seed-locations.js first (addresses use the location catalog).");
}

// ---- 0b. remove the previous demo data -----------------------------------------------------
for (const name of ["orders", "order_items", "order_status_history", "returns", "coupon_usages",
                    "product_reviews", "addresses", "inventory_movements"]) {
  db.getCollection(name).deleteMany({ is_demo: true });
}

// ---- 1. users ------------------------------------------------------------------------------
const people = [
  { phone: "0900000001", name: "Quản trị viên Themis", role: "admin", email: "admin@themis.local" },
  { phone: "0900000011", name: "Nguyễn Minh Tiến", email: "tien.demo@example.com" },
  { phone: "0900000012", name: "Trần Thị Bình" },
  { phone: "0900000013", name: "Lê Hoàng Nam", email: "nam.demo@example.com" },
  { phone: "0900000014", name: "Phạm Thu Hà" },
  { phone: "0900000015", name: "Võ Quốc Bảo", email: "bao.demo@example.com" },
  { phone: "0900000016", name: "Đặng Ngọc Anh" },
  { phone: "0900000017", name: "Bùi Thanh Tùng", email: "tung.demo@example.com" },
  { phone: "0900000018", name: "Hoàng Gia Hân", locked: true }
];
const demoUsers = [];
let nextUserId = maxId("users", "user_id");
people.forEach((person, index) => {
  const existing = db.users.findOne({ phone: person.phone });
  const userId = existing ? existing.user_id : ++nextUserId;
  const createdAt = new Date(NOW.getTime() - (75 - index * 3) * DAY);
  const doc = {
    user_id: userId, full_name: person.name, phone: person.phone,
    password_hash: DEMO_PASSWORD_HASH, role: person.role || "customer",
    status: person.locked ? "locked" : "active", created_at: createdAt, updated_at: createdAt, is_demo: true
  };
  if (person.email) doc.email = person.email;
  const unset = {};
  if (!person.email) unset.email = "";
  if (person.locked) {
    doc.status_reason = "Nhiều lần từ chối nhận hàng không lý do";
    doc.status_changed_at = new Date(NOW.getTime() - 5 * DAY);
  } else unset.status_reason = "";
  const update = { $set: doc };
  if (Object.keys(unset).length) update.$unset = unset;
  db.users.updateOne({ phone: person.phone }, update, { upsert: true });
  demoUsers.push({ ...person, userId });
});
const customers = demoUsers.filter((u) => u.role !== "admin");
const admin = demoUsers.find((u) => u.role === "admin");

// one default address each, taken from the real location catalog (Hồ Chí Minh City, code 79)
const streets = ["12 Nguyễn Huệ", "45 Lê Lợi", "78 Trần Hưng Đạo", "9 Phạm Văn Đồng", "103 Hai Bà Trưng",
                 "5 Võ Thị Sáu", "66 Cách Mạng Tháng 8", "21 Điện Biên Phủ"];
const hcm = db.provinces.findOne({ province_code: 79 });
const hcmDistricts = db.districts.find({ province_code: 79 }).sort({ district_code: 1 }).toArray();
let nextAddressId = maxId("addresses", "address_id");
customers.forEach((c, i) => {
  const district = hcmDistricts[i % hcmDistricts.length];
  const ward = db.wards.find({ district_code: district.district_code }).sort({ ward_code: 1 }).limit(1).toArray()[0];
  c.address = {
    recipient_name: c.name, recipient_phone: c.phone, address_line: streets[i % streets.length],
    ward: ward.name, district: district.name, province: hcm.name,
    ward_code: ward.ward_code, district_code: district.district_code, province_code: hcm.province_code
  };
  db.addresses.insertOne({ address_id: ++nextAddressId, user_id: c.userId, ...c.address, is_default: true,
                           created_at: NOW, is_demo: true });
});

// ---- 2. stock baseline (same rule as 02-seed-products.js) ------------------------------------------
const variants = db.product_variants.find({}).toArray();
const productsById = Object.fromEntries(db.products.find({}).toArray().map((p) => [p.product_id, p]));
const sizeNames = Object.fromEntries(db.sizes.find({}).toArray().map((s) => [s.size_id, s.name]));
const colorNames = Object.fromEntries(db.colors.find({}).toArray().map((c) => [c.color_id, c.name]));
const stock = {};
for (const v of variants) {
  const soldOut = v.sku === "P02-M-0" || v.sku === "P11-L-0";
  const low = v.sku === "P07-S-0" || v.sku === "P15-M-0";
  stock[v.variant_id] = { onHand: soldOut ? 0 : low ? 2 : 12, reserved: 0 };
}
// Variants we are allowed to sell in demo orders: well stocked and active.
const pool = variants.filter((v) => stock[v.variant_id].onHand === 12 && v.status === "active");
const remaining = (v) => stock[v.variant_id].onHand - stock[v.variant_id].reserved;

// ---- 3. orders --------------------------------------------------------------------------------------
const FAILURE_REASONS = ["unreachable", "wrong_address", "reschedule", "other"];
let nextOrderItemId = maxId("order_items", "order_item_id");
let nextHistoryId = maxId("order_status_history", "history_id");
const orderRows = [];
const PATH = {
  pending: ["pending"],
  confirmed: ["pending", "confirmed"],
  packing: ["pending", "confirmed", "packing"],
  shipping: ["pending", "confirmed", "packing", "shipping"],
  delivered: ["pending", "confirmed", "packing", "shipping", "delivered"],
  delivery_failed: ["pending", "confirmed", "packing", "shipping", "delivery_failed"],
  returning_to_seller: ["pending", "confirmed", "packing", "shipping", "delivery_failed", "returning_to_seller"],
  returned_to_seller: ["pending", "confirmed", "packing", "shipping", "delivery_failed", "returning_to_seller", "returned_to_seller"]
};

function chooseStatus(ageDays) {
  const r = rnd();
  if (ageDays >= 20) return r < 0.74 ? "delivered" : r < 0.86 ? "cancelled" : "returned_to_seller";
  if (ageDays >= 8) return r < 0.62 ? "delivered" : r < 0.72 ? "cancelled" : r < 0.8 ? "shipping" :
    r < 0.88 ? "delivery_failed" : "returning_to_seller";
  if (ageDays >= 3) return r < 0.32 ? "delivered" : r < 0.55 ? "shipping" : r < 0.75 ? "packing" :
    r < 0.9 ? "confirmed" : "cancelled";
  return r < 0.45 ? "pending" : r < 0.7 ? "confirmed" : r < 0.9 ? "packing" : "cancelled";
}

const ORDER_COUNT = 45;
// The newest orders are fixed so every admin tab (new / to pack / shipping / failed / returning) has rows.
const FORCED_STATUS = {
  44: "pending", 43: "pending", 42: "confirmed", 41: "packing", 40: "packing", 39: "shipping", 38: "shipping",
  37: "confirmed", 36: "delivery_failed", 35: "returning_to_seller", 34: "shipping", 33: "delivered", 32: "delivered",
  31: "delivered", 30: "delivered"
};
let nextOrderId = maxId("orders", "order_id");
for (let i = 0; i < ORDER_COUNT; i++) {
  const customer = customers[i % customers.length];
  const ageDays = 60 - (i * 59.5) / (ORDER_COUNT - 1);       // spread from ~60 days ago to today
  const placedAt = new Date(NOW.getTime() - ageDays * DAY - between(0, 10) * HOUR);
  const placed = placedAt > NOW ? new Date(NOW.getTime() - HOUR) : placedAt;
  const status = FORCED_STATUS[i] || chooseStatus(ageDays);
  const paymentMethod = rnd() < 0.65 ? "cod" : "bank_transfer";

  // lines
  const lineCount = 1 + Math.floor(rnd() * 3);
  const lines = [];
  for (let k = 0; k < lineCount; k++) {
    const candidates = pool.filter((v) => !lines.some((l) => l.variant.variant_id === v.variant_id));
    const variant = pick(candidates);
    const quantity = 1 + Math.floor(rnd() * 2);
    if (remaining(variant) < quantity) continue;
    // Reserve at once so later lines/orders cannot oversell (rolled back below for cancelled orders).
    stock[variant.variant_id].reserved += quantity;
    lines.push({ variant, quantity });
  }
  if (lines.length === 0) continue;
  for (const l of lines) stock[l.variant.variant_id].reserved -= l.quantity;   // undo; applied by status below

  const gross = lines.map((l) => l.variant.price * l.quantity);
  const subtotal = gross.reduce((a, b) => a + b, 0);
  const useCoupon = i % 9 === 4 && subtotal >= 300000;
  const discount = useCoupon ? Math.min(Math.round(subtotal * 0.11), 111000) : 0;
  const shippingFee = subtotal - discount >= 500000 ? 0 : 30000;
  const total = subtotal - discount + shippingFee;

  // history path with believable timestamps
  const path = PATH[status] || ["pending", "cancelled"];
  const steps = status === "cancelled"
    ? (rnd() < 0.5 ? ["pending", "cancelled"] : ["pending", "confirmed", "cancelled"]) : path;
  const times = [placed];
  for (let s = 1; s < steps.length; s++) {
    const prev = times[s - 1];
    const room = Math.max(0, NOW.getTime() - prev.getTime());
    const gap = Math.min(between(4, 20) * HOUR, (0.9 * room) / (steps.length - s));
    times.push(new Date(prev.getTime() + gap));
  }
  const at = (name) => times[steps.indexOf(name)];

  const orderId = ++nextOrderId;
  const failures = steps.includes("delivery_failed")
    ? [{ attempt_number: 1, attempted_at: at("delivery_failed"), reason: pick(FAILURE_REASONS), note: "Không liên lạc được với người nhận sau 2 cuộc gọi." }]
    : [];
  if (steps.includes("returning_to_seller") && rnd() < 0.5) {
    failures.push({ attempt_number: 2, attempted_at: new Date(at("returning_to_seller").getTime() - 2 * HOUR),
                    reason: "customer_refused", note: "Khách từ chối nhận hàng khi giao lần 2." });
  }

  let paymentStatus = paymentMethod === "cod" ? "unpaid" : "pending";
  let paidAt = null, refundedAt = null, deliveredAt = null, cancelReason = null, inspection = null;
  if (status === "delivered") {
    deliveredAt = at("delivered");
    paymentStatus = "paid"; paidAt = paymentMethod === "cod" ? deliveredAt : at("packing");
  } else if (paymentMethod === "bank_transfer" && steps.includes("packing") && status !== "cancelled") {
    paymentStatus = "paid"; paidAt = at("packing");
  }
  if (status === "cancelled") {
    cancelReason = pick(["Khách muốn đổi sang sản phẩm khác", "Đặt nhầm size, cần đặt lại", "Không còn nhu cầu mua nữa"]);
    if (paymentMethod === "bank_transfer" && i % 3 === 0) {
      paymentStatus = ageDays > 10 ? "refunded" : "refund_pending";
      paidAt = placed; if (paymentStatus === "refunded") refundedAt = new Date(at("cancelled").getTime() + DAY);
    }
  }
  if (status === "returned_to_seller") {
    inspection = rnd() < 0.7 ? "passed" : "failed";
    if (paymentStatus === "paid") paymentStatus = ageDays > 15 ? "refunded" : "refund_pending";
    if (paymentStatus === "refunded") refundedAt = new Date(at("returned_to_seller").getTime() + DAY);
  }

  db.orders.insertOne({
    order_id: orderId, order_code: "THM-" + (1000 + orderId), user_id: customer.userId, coupon_id: useCoupon ? 4 : null,
    status, payment_method: paymentMethod, payment_status: paymentStatus,
    recipient_name: customer.address.recipient_name, recipient_phone: customer.address.recipient_phone,
    shipping_address_line: customer.address.address_line, shipping_ward: customer.address.ward,
    shipping_district: customer.address.district, shipping_province: customer.address.province,
    shipping_ward_code: customer.address.ward_code, shipping_district_code: customer.address.district_code,
    shipping_province_code: customer.address.province_code,
    subtotal, discount_amount: discount, shipping_fee: shippingFee, total_amount: total,
    customer_note: i % 7 === 0 ? "Giao giờ hành chính giúp mình" : null, cancel_reason: cancelReason,
    placed_at: placed, updated_at: times[times.length - 1],
    delivered_at: deliveredAt, paid_at: paidAt, refunded_at: refundedAt,
    carrier: steps.includes("shipping") ? pick(["GHN", "GHTK", "Viettel Post"]) : null,
    tracking_code: steps.includes("shipping") ? "VN" + (700000000 + orderId * 137) : null,
    delivery_attempts: failures, return_inspection: inspection, is_demo: true
  });

  // order lines with the discount spread by value; the last line takes the rounding remainder
  let allocated = 0;
  const orderLines = lines.map((l, idx) => {
    const lineGross = gross[idx];
    const itemDiscount = idx === lines.length - 1 ? discount - allocated : Math.round((discount * lineGross) / subtotal);
    allocated += itemDiscount;
    const product = productsById[l.variant.product_id];
    const doc = {
      order_item_id: ++nextOrderItemId, order_id: orderId, variant_id: l.variant.variant_id, product_id: product.product_id,
      product_name_snapshot: product.name, sku_snapshot: l.variant.sku,
      size_snapshot: sizeNames[l.variant.size_id], color_snapshot: colorNames[l.variant.color_id],
      quantity: l.quantity, unit_price: l.variant.price, discount_amount: itemDiscount,
      line_total: lineGross - itemDiscount, is_demo: true
    };
    db.order_items.insertOne(doc);
    return doc;
  });

  steps.forEach((name, s) => {
    db.order_status_history.insertOne({
      history_id: ++nextHistoryId, order_id: orderId, old_status: s === 0 ? null : steps[s - 1], new_status: name,
      changed_by: s === 0 ? customer.userId : admin.userId,
      note: name === "cancelled" ? cancelReason : name === "delivery_failed" ? failures[0].note : null,
      changed_at: times[s], is_demo: true
    });
  });

  // stock effects (see InventoryService): reserved while pending..packing, shelf stock leaves at shipping,
  // goods that pass inspection come back after returned_to_seller
  for (const l of orderLines) {
    const s = stock[l.variant_id];
    if (["pending", "confirmed", "packing"].includes(status)) s.reserved += l.quantity;
    else if (["shipping", "delivered", "delivery_failed", "returning_to_seller"].includes(status)) s.onHand -= l.quantity;
    else if (status === "returned_to_seller" && inspection === "failed") s.onHand -= l.quantity;
  }
  orderRows.push({ orderId, userId: customer.userId, status, deliveredAt, placed, lines: orderLines, useCoupon, total, discount });
}

// ---- 4. coupon usages for the orders that used the 1111 code ---------------------------------------
let nextUsageId = maxId("coupon_usages", "coupon_usage_id");
for (const o of orderRows.filter((x) => x.useCoupon)) {
  db.coupon_usages.insertOne({
    coupon_usage_id: ++nextUsageId, coupon_id: 4, order_id: o.orderId, user_id: o.userId, discount_amount: o.discount,
    used_at: o.placed, status: o.status === "cancelled" ? "released" : "applied", is_demo: true
  });
}
db.coupons.updateOne({ coupon_id: 4 }, { $set: { usage_count: db.coupon_usages.countDocuments({ coupon_id: 4, status: "applied" }) } });

// ---- 5. returns ------------------------------------------------------------------------------------------
const RETURN_PLAN = ["requested", "requested", "awaiting_return", "received", "refund_pending", "refunded",
                     "rejected", "rejected"];
const REASONS = ["wrong_size", "defective", "changed_mind", "wrong_item"];
let nextReturnId = maxId("returns", "return_id");
const deliveredOrders = orderRows.filter((o) => o.status === "delivered").sort((a, b) => b.deliveredAt - a.deliveredAt);
const recentDelivered = deliveredOrders.filter((o) => NOW - o.deliveredAt < 6 * DAY);
const olderDelivered = deliveredOrders.filter((o) => NOW - o.deliveredAt >= 6 * DAY);
const returnCandidates = [...recentDelivered, ...olderDelivered];
RETURN_PLAN.forEach((target, index) => {
  const order = returnCandidates[index];
  if (!order) return;
  const line = order.lines[0];
  const quantity = 1;
  const refund = Math.round((line.line_total * quantity) / line.quantity);
  const requestedAt = new Date(Math.min(order.deliveredAt.getTime() + DAY, NOW.getTime() - 2 * HOUR));
  const flow = { requested: ["requested"], awaiting_return: ["requested", "awaiting_return"],
    received: ["requested", "awaiting_return", "received"], refund_pending: ["requested", "awaiting_return", "received", "refund_pending"],
    refunded: ["requested", "awaiting_return", "received", "refund_pending", "refunded"], rejected: ["requested", "rejected"] }[target];
  const events = flow.map((name, s) => ({
    status: name,
    note: name === "requested" ? "Sản phẩm không vừa, mình muốn trả lại shop." : name === "rejected" ? "Sản phẩm đã qua sử dụng, không đủ điều kiện đổi trả." : null,
    actor_id: name === "requested" ? order.userId : admin.userId,
    at: new Date(Math.min(requestedAt.getTime() + s * 8 * HOUR, NOW.getTime() - HOUR))
  }));
  const returnId = ++nextReturnId;
  db.returns.insertOne({
    return_id: returnId, return_code: "RTN-" + returnId, order_id: order.orderId,
    order_code: "THM-" + (1000 + order.orderId), user_id: order.userId, status: target, reason: REASONS[index % REASONS.length],
    description: "Sản phẩm không đúng như mong đợi nên mình muốn trả lại.",
    items: [{ order_item_id: line.order_item_id, variant_id: line.variant_id, product_name_snapshot: line.product_name_snapshot,
              sku_snapshot: line.sku_snapshot, size_snapshot: line.size_snapshot, color_snapshot: line.color_snapshot,
              quantity, unit_price: line.unit_price, allocated_discount: Math.round((line.discount_amount * quantity) / line.quantity),
              estimated_refund: refund }],
    estimated_refund: refund, refund_amount: target === "refunded" ? refund : null,
    refund_reference: target === "refunded" ? "CK-" + (880000 + returnId) : null,
    reject_reason: target === "rejected" ? "Sản phẩm đã qua sử dụng, không đủ điều kiện đổi trả." : null,
    assignee_id: target === "requested" ? null : admin.userId, requested_at: requestedAt,
    updated_at: events[events.length - 1].at, refunded_at: target === "refunded" ? events[events.length - 1].at : null,
    events, is_demo: true
  });
  if (target === "refund_pending" || target === "refunded") stock[line.variant_id].onHand += quantity;   // restocked after inspection
});

// ---- 6. reviews ---------------------------------------------------------------------------------------------
let nextReviewId = maxId("product_reviews", "review_id");
const COMMENTS = ["Chất vải mát, mặc rất thoải mái, đúng size.", "Form đẹp nhưng màu hơi khác ảnh một chút.",
  "Giao hàng nhanh, đóng gói cẩn thận, sẽ ủng hộ tiếp.", "Đường may chưa được gọn lắm, mong shop kiểm tra kỹ hơn.",
  "Sản phẩm đúng mô tả, giá hợp lý cho chất lượng này.", "Mặc lên hơi rộng so với bảng size, nên lấy nhỏ hơn một size."];
const reviewed = new Set(db.product_reviews.find({}, { user_id: 1, product_id: 1 }).toArray().map((r) => r.user_id + ":" + r.product_id));
let reviewCount = 0;
for (const order of deliveredOrders) {
  if (reviewCount >= COMMENTS.length) break;
  const line = order.lines[0];
  const key = order.userId + ":" + line.product_id;
  if (reviewed.has(key)) continue;
  reviewed.add(key);
  db.product_reviews.insertOne({
    review_id: ++nextReviewId, product_id: line.product_id, user_id: order.userId, order_item_id: line.order_item_id,
    rating: [5, 4, 5, 2, 4, 3][reviewCount], comment: COMMENTS[reviewCount],
    status: reviewCount === 3 || reviewCount === 5 ? "hidden" : "published",
    created_at: new Date(Math.min(order.deliveredAt.getTime() + 2 * DAY, NOW.getTime() - HOUR)), is_demo: true
  });
  reviewCount++;
}

// ---- 7. write the stock back and keep the id counters ahead -----------------------------------------------------
for (const v of variants) {
  const s = stock[v.variant_id];
  db.inventory.updateOne({ variant_id: v.variant_id }, {
    $set: { quantity_on_hand: s.onHand, quantity_reserved: s.reserved, updated_at: NOW }
  });
}
const counterTargets = {
  users: maxId("users", "user_id"), addresses: maxId("addresses", "address_id"), orders: maxId("orders", "order_id"),
  order_items: maxId("order_items", "order_item_id"), order_status_history: maxId("order_status_history", "history_id"),
  returns: maxId("returns", "return_id"), coupon_usages: maxId("coupon_usages", "coupon_usage_id"),
  product_reviews: maxId("product_reviews", "review_id")
};
for (const [name, value] of Object.entries(counterTargets)) {
  db.counters.updateOne({ _id: name }, { $max: { sequence_value: value } }, { upsert: true });
}

print(`Demo data: ${db.orders.countDocuments({ is_demo: true })} orders, ${db.returns.countDocuments({ is_demo: true })} returns, ` +
      `${db.product_reviews.countDocuments({ is_demo: true })} reviews, ${customers.length} customers + 1 admin (password Demo@12345).`);
