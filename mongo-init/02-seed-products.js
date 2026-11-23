const fs = require("fs");
const fixtureDirectory = "/docker-entrypoint-initdb.d/mock-data";
const products = JSON.parse(
  fs.readFileSync(`${fixtureDirectory}/products.json`, "utf8")
);
const variantConfigurations = JSON.parse(
  fs.readFileSync(`${fixtureDirectory}/variants.json`, "utf8")
);

const categories = [
  { category_id: 1, name: "Áo thun", slug: "ao-thun" },
  { category_id: 2, name: "Áo sơ mi", slug: "ao-so-mi" },
  { category_id: 3, name: "Quần", slug: "quan" },
  { category_id: 4, name: "Áo khoác", slug: "ao-khoac" }
];
// Admin-managed lists the storefront filters by (slugs match the codes the frontend mock used).
const materials = [
  { material_id: 1, name: "Cotton", slug: "cotton" },
  { material_id: 2, name: "Denim", slug: "denim" },
  { material_id: 3, name: "Vải tổng hợp", slug: "synthetic" },
  { material_id: 4, name: "Linen (đũi)", slug: "linen" },
  { material_id: 5, name: "Kaki", slug: "kaki" },
  { material_id: 6, name: "Len", slug: "wool" }
];
const occasions = [
  { occasion_id: 1, name: "Mặc hằng ngày", slug: "daily" },
  { occasion_id: 2, name: "Đi làm / Công sở", slug: "work" },
  { occasion_id: 3, name: "Đi chơi", slug: "casual" },
  { occasion_id: 4, name: "Khoác ngoài", slug: "outdoor" },
  { occasion_id: 5, name: "Đi học", slug: "school" },
  { occasion_id: 6, name: "Thể thao", slug: "sport" }
];
const materialIds = Object.fromEntries(materials.map((m) => [m.slug, m.material_id]));
const occasionIds = Object.fromEntries(occasions.map((o) => [o.slug, o.occasion_id]));

// Same folding as the API (Common/Text.Fold): lower case, no accents, single spaces.
function fold(value) {
  return value.trim().toLowerCase().replace(/đ/g, "d").normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "").replace(/\./g, " ").replace(/\s+/g, " ").trim();
}

// Height/weight ranges per size for the size suggestion (overlapping like most shops' charts).
const bodyRanges = {
  S: { height_min_cm: 155, height_max_cm: 165, weight_min_kg: 48, weight_max_kg: 58 },
  M: { height_min_cm: 163, height_max_cm: 170, weight_min_kg: 56, weight_max_kg: 66 },
  L: { height_min_cm: 168, height_max_cm: 176, weight_min_kg: 64, weight_max_kg: 76 },
  XL: { height_min_cm: 174, height_max_cm: 183, weight_min_kg: 74, weight_max_kg: 88 }
};
const defaultChartCategories = new Set();
const sizes = ["S", "M", "L", "XL"];
const sizeIds = Object.fromEntries(sizes.map((size, index) => [size, index + 1]));
const variantsByProduct = new Map(
  variantConfigurations.map((configuration) => [
    configuration.productId,
    configuration
  ])
);
const colorsByKey = new Map();
let nextColorId = 1;
let nextVariantId = 1;

function upsert(collectionName, idField, document) {
  db.getCollection(collectionName).updateOne(
    { [idField]: document[idField] },
    { $set: document },
    { upsert: true }
  );
}

for (const category of categories) {
  upsert("categories", "category_id", {
    ...category,
    display_order: category.category_id,
    status: "active",
    created_at: new Date("2026-10-02T03:00:00.000Z"),
    updated_at: new Date("2026-10-18T03:00:00.000Z")
  });
}

for (const [index, size] of sizes.entries()) {
  upsert("sizes", "size_id", {
    size_id: sizeIds[size],
    name: size,
    status: "active",
    display_order: index + 1
  });
}

const seededAt = new Date("2026-10-02T03:00:00.000Z");
materials.forEach((material, index) => upsert("materials", "material_id", {
  ...material, description: null, display_order: index + 1, status: "active",
  created_at: seededAt, updated_at: seededAt
}));
occasions.forEach((occasion, index) => upsert("occasions", "occasion_id", {
  ...occasion, description: null, display_order: index + 1, status: "active",
  created_at: seededAt, updated_at: seededAt
}));

for (const product of products) {
  const productId = Number(product.id.slice(1));
  const categoryId = Number(product.categoryId.slice(3));
  const sizeChartId = Number(product.sizeChartId.slice(4));
  const configuration = variantsByProduct.get(product.id);

  if (!Number.isInteger(productId) || !Number.isInteger(categoryId) ||
      !Number.isInteger(sizeChartId) || !configuration) {
    throw new Error(`Invalid product or missing variants fixture: ${product.id}`);
  }

  const createdAt = new Date(product.createdAt);
  const isPants = product.categoryId === "CAT3";
  const isOuterwear = product.categoryId === "CAT4";
  const chartSizes = [...new Set(configuration.sizes)];

  upsert("size_charts", "size_chart_id", {
    size_chart_id: sizeChartId,
    name: `Bảng chọn size · ${product.name}`,
    description: `Bảng chọn size cho ${product.name}`,
    category_id: categoryId,
    // The first chart of each category is what new products of that category fall back to.
    is_default: !defaultChartCategories.has(categoryId),
    created_at: createdAt,
    updated_at: createdAt
  });
  defaultChartCategories.add(categoryId);

  chartSizes.forEach((sizeLabel, index) => {
    upsert("size_chart_items", "size_chart_item_id", {
      size_chart_item_id: sizeChartId * 100 + index + 1,
      size_chart_id: sizeChartId,
      size_label: sizeLabel,
      chest_cm: isPants ? 0 : 88 + index * 6 + (isOuterwear ? 4 : 0),
      waist_cm: (isPants ? 70 : 72) + index * 6,
      hip_cm: (isPants ? 88 : 90) + index * 6,
      length_cm: (isPants ? 96 : isOuterwear ? 68 : 65) + index * 2,
      notes: "Số đo cơ thể tham khảo",
      ...(bodyRanges[sizeLabel] || {}),
      display_order: index
    });
  });

  upsert("products", "product_id", {
    product_id: productId,
    category_id: categoryId,
    size_chart_id: sizeChartId,
    name: product.name,
    description: product.description,
    material: product.material,
    material_id: materialIds[product.materialGroup],
    occasion_ids: product.suitableFor.map((code) => occasionIds[code]),
    care_instructions: product.careInstructions,
    search_text: fold(`${product.name} ${product.description}`),
    base_price: configuration.price,
    status: product.active ? "active" : "hidden",
    created_at: createdAt,
    updated_at: createdAt
  });

  product.imageUrls.forEach((imageUrl, index) => {
    upsert("product_images", "image_id", {
      image_id: productId * 10 + index + 1,
      product_id: productId,
      image_url: imageUrl,
      alt_text: `${product.name} - ảnh ${index + 1}`,
      sort_order: index,
      is_primary: index === 0
    });
  });

  configuration.sizes.forEach((size) => {
    if (!Object.hasOwn(sizeIds, size)) {
      throw new Error(`Unknown size "${size}" in variants for ${product.id}`);
    }

    configuration.colors.forEach((color, colorIndex) => {
      const colorKey = `${color.name}|${color.hex}`;
      if (!colorsByKey.has(colorKey)) {
        colorsByKey.set(colorKey, nextColorId++);
      }

      const colorId = colorsByKey.get(colorKey);
      upsert("colors", "color_id", {
        color_id: colorId,
        name: color.name,
        hex_code: color.hex,
        status: "active"
      });

      const variantId = nextVariantId++;
      const sku = `${product.id}-${size}-${colorIndex}`;
      const variant = {
        variant_id: variantId,
        product_id: productId,
        size_id: sizeIds[size],
        color_id: colorId,
        sku,
        price: configuration.price + (size === "XL" ? 20000 : 0),
        status: product.active ? "active" : "hidden"
      };

      upsert("product_variants", "variant_id", variant);

      const isSoldOut = sku === "P02-M-0" || sku === "P11-L-0";
      const isLowStock = sku === "P07-S-0" || sku === "P15-M-0";
      upsert("inventory", "inventory_id", {
        inventory_id: variantId,
        variant_id: variantId,
        quantity_on_hand: isSoldOut ? 0 : isLowStock ? 2 : 12,
        quantity_reserved: 0,
        low_stock_threshold: 5,
        updated_at: createdAt
      });
    });
  });
}

if (variantsByProduct.size !== products.length) {
  throw new Error("Products and variants fixtures do not contain matching products.");
}

print(`Seeded ${products.length} products and ${nextVariantId - 1} variants.`);
