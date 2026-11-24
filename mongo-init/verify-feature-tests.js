// Read-only invariants + proof that rerunning the additive seed changes no document.
(() => {
  const suite = "feature-tests-v1", filter = { seed_suite: suite };
  let checks = 0;
  function check(ok, message) { if (!ok) throw new Error(message); checks++; }
  const orders = db.orders.find(filter).toArray();
  for (const order of orders) {
    const lines = db.order_items.find({ order_id: order.order_id }).toArray();
    check(lines.length > 0, "Order without lines " + order.order_code);
    check(lines.every(l => l.image_url_snapshot && l.sku_snapshot && l.size_snapshot && l.color_snapshot), "Incomplete snapshot " + order.order_code);
    check(lines.reduce((s, l) => s + Number(l.unit_price) * l.quantity, 0) === Number(order.subtotal), "Incorrect subtotal " + order.order_code);
    check(Number(order.subtotal) - Number(order.discount_amount) + Number(order.shipping_fee) === Number(order.total_amount), "Incorrect total " + order.order_code);
    check(lines.reduce((s, l) => s + Number(l.line_total), 0) === Number(order.subtotal) - Number(order.discount_amount), "Incorrect allocation " + order.order_code);
    check(db.users.countDocuments({ user_id: order.user_id }) === 1, "Missing owner " + order.order_code);
    const history = db.order_status_history.find({ order_id: order.order_id }).sort({ changed_at: 1 }).toArray();
    check(history.length > 0 && history[history.length - 1].new_status === order.status, "Incorrect timeline " + order.order_code);
    check(history.every(h => h.changed_at <= new Date()), "Future history " + order.order_code);
  }
  for (const review of db.product_reviews.find(filter).toArray()) {
    const line = db.order_items.findOne({ order_item_id: review.order_item_id });
    const order = line && db.orders.findOne({ order_id: line.order_id });
    check(order && order.status === "delivered" && Number(order.user_id) === Number(review.user_id), "Review without delivered purchase");
    check(Number(line.product_id) === Number(review.product_id), "Review product mismatch");
    check(review.created_at >= order.delivered_at && review.created_at <= new Date(), "Incorrect review date");
  }
  for (const inv of db.inventory.find(filter).toArray()) {
    check(inv.quantity_on_hand >= inv.quantity_reserved && inv.quantity_reserved >= 0, "Invalid inventory");
    const lines = db.order_items.find({ variant_id: inv.variant_id }).toArray();
    const reserved = lines.reduce((sum, l) => {
      const order = db.orders.findOne({ order_id: l.order_id });
      return sum + (["pending", "confirmed", "packing"].includes(order.status) ? l.quantity : 0);
    }, 0);
    check(reserved === Number(inv.quantity_reserved), "Reservation mismatch " + inv.variant_id);
  }
  for (const returned of db.returns.find(filter).toArray()) {
    const order = db.orders.findOne({ order_id: returned.order_id });
    check(order && Number(order.user_id) === Number(returned.user_id) && order.status === "delivered", "Return ownership mismatch");
    check(returned.requested_at >= order.delivered_at && returned.requested_at - order.delivered_at < 7 * 86400000, "Return outside policy");
    check(returned.items.every(i => db.order_items.countDocuments({ order_id: order.order_id, order_item_id: i.order_item_id }) === 1), "Invalid return line");
  }
  const crypto = require("crypto");
  const snapshot = () => Object.fromEntries(db.getCollectionNames().sort().map(name => [name,
    crypto.createHash("sha256").update(EJSON.stringify(db.getCollection(name).find().sort({ _id: 1 }).toArray())).digest("hex")]));
  const before = snapshot();
  load("/docker-entrypoint-initdb.d/05-seed-feature-tests.js");
  const after = snapshot();
  check(JSON.stringify(before) === JSON.stringify(after), "Rerunning seed changed existing data");
  print(JSON.stringify({ passed: checks, idempotent: true, preservedAllCollections: true }));
})();
