(() => {
  const assert = require('assert');
  // Existing documents must be byte-for-byte unchanged (only counters may advance).
  const snapshot = new Map(db.getCollectionNames().filter(n => !['counters', 'seed_runs'].includes(n)).map(n => [n, db.getCollection(n).find().toArray().map(d => ({ id: d._id, json: EJSON.stringify(d) }))]));
  load('/docker-entrypoint-initdb.d/06-seed-accessories.js');
  let preserved = 0;
  for (const [name, docs] of snapshot) for (const d of docs) {
    assert.strictEqual(EJSON.stringify(db.getCollection(name).findOne({ _id: d.id })), d.json, `Existing data changed: ${name}/${d.id}`);
    preserved++;
  }
  const products = db.products.find({ seed_suite: 'accessories-v1' }).toArray();
  assert.strictEqual(products.length, 24);
  assert.strictEqual(new Set(products.map(p => Number(p.category_id))).size, 6);
  let variants = 0, lowStock = 0, soldOut = 0;
  for (const p of products) {
    assert(db.categories.findOne({ category_id: p.category_id, status: 'active' }));
    assert(db.materials.findOne({ material_id: p.material_id }));
    assert.strictEqual(db.product_images.countDocuments({ product_id: p.product_id }), 2);
    for (const v of db.product_variants.find({ product_id: p.product_id }).toArray()) {
      assert(db.sizes.findOne({ size_id: v.size_id }));
      assert(db.colors.findOne({ color_id: v.color_id }));
      assert(Number(v.price) > 0);
      const stock = db.inventory.findOne({ variant_id: v.variant_id });
      assert(stock && stock.quantity_reserved === 0);
      if (stock.quantity_on_hand === 2) lowStock++;
      if (stock.quantity_on_hand === 0) soldOut++;
      variants++;
    }
  }
  assert.strictEqual(variants, 32);
  assert.strictEqual(lowStock, 8);
  assert.strictEqual(soldOut, 8);
  print(JSON.stringify({ accessories: products.length, categories: 6, variants, lowStock, soldOut, preservedExistingDocuments: preserved }));
})();
