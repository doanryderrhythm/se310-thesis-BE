// Additive fixtures: never resets existing catalog, carts, orders or inventory.
(() => {
  const suite = 'accessories-v1', now = new Date();
  if (db.seed_runs.findOne({ _id: suite })) { print('Accessories already installed; no writes.'); return; }
  const rows = JSON.parse(require('fs').readFileSync('/docker-entrypoint-initdb.d/mock-data/accessories.json', 'utf8'));
  if (rows.length !== 24 || rows.some(p => !p.imageUrls.length || !(p.price > 0))) throw new Error('Invalid accessories snapshot');
  const fold = value => value.toLowerCase().replace(/đ/g, 'd').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/\s+/g, ' ').trim();
  const slug = value => fold(value).replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  function next(collection, field) {
    const top = db.getCollection(collection).find().sort({ [field]: -1 }).limit(1).toArray()[0];
    db.counters.updateOne({ _id: collection }, { $max: { sequence_value: top ? top[field] : 0 } }, { upsert: true });
    return db.counters.findOneAndUpdate({ _id: collection }, { $inc: { sequence_value: 1 } }, { returnDocument: 'after' }).sequence_value;
  }
  function ensure(collection, field, key, data, match) {
    const existing = db.getCollection(collection).findOne({ seed_key: suite + ':' + key }) || (match && db.getCollection(collection).findOne(match));
    if (existing) return existing;
    const doc = { ...data, [field]: next(collection, field), seed_suite: suite, seed_key: suite + ':' + key };
    db.getCollection(collection).insertOne(doc);
    return doc;
  }
  for (const [index, p] of rows.entries()) {
    const category = ensure('categories', 'category_id', p.collection, { name: p.category, slug: slug(p.category), status: 'active', display_order: 5 + Math.floor(index / 4), created_at: now, updated_at: now }, { slug: slug(p.category) });
    const color = ensure('colors', 'color_id', 'color-' + p.color.name, { name: p.color.name, hex_code: p.color.hex, status: 'active' }, { name: p.color.name, hex_code: p.color.hex });
    const material = ensure('materials', 'material_id', 'material-' + p.material, { name: p.material, slug: slug(p.material), status: 'active', display_order: 10 + index, created_at: now, updated_at: now }, { slug: slug(p.material) });
    const description = p.name + '. Tham khảo YaMe. Tồn kho và kích cỡ là dữ liệu mô phỏng phục vụ kiểm thử.';
    const product = ensure('products', 'product_id', p.key, { category_id: category.category_id, name: p.name,
      slug: 'phu-kien-' + p.key, description, search_text: fold(p.name + ' ' + description), material: p.material,
      material_id: material.material_id, occasion_ids: [1, 3], care_instructions: p.material === 'Da bò' ? 'Lau bằng khăn mềm; tránh ngâm nước và nguồn nhiệt.' : 'Vệ sinh nhẹ; không tẩy; phơi nơi thoáng mát.',
      base_price: p.price, status: 'active', created_at: now, updated_at: now });
    p.imageUrls.forEach((image, i) => ensure('product_images', 'image_id', p.key + '-image-' + i,
      { product_id: product.product_id, image_url: image, alt_text: p.name + ' - ảnh ' + (i + 1), sort_order: i, is_primary: i === 0, color_id: color.color_id }));
    p.sizes.forEach((name, i) => {
      const size = ensure('sizes', 'size_id', 'size-' + name, { name, status: 'active', display_order: 10 + i }, { name });
      const variant = ensure('product_variants', 'variant_id', p.key + '-variant-' + i, { product_id: product.product_id,
        size_id: size.size_id, color_id: color.color_id, sku: 'ACC-' + p.key + '-' + i, price: p.price, status: 'active' });
      ensure('inventory', 'inventory_id', p.key + '-stock-' + i, { variant_id: variant.variant_id,
        quantity_on_hand: p.stock, quantity_reserved: 0, low_stock_threshold: 5, average_cost: Math.round(p.price * .45), updated_at: now });
    });
  }
  db.seed_runs.insertOne({ _id: suite, completed_at: now });
  print(JSON.stringify({ suite, products: db.products.countDocuments({ seed_suite: suite }), variants: db.product_variants.countDocuments({ seed_suite: suite }), categories: db.categories.countDocuments({ seed_suite: suite }) }));
})();
