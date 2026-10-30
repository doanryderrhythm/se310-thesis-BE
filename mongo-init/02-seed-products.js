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
    status: "active",
    created_at: new Date("2026-10-02T03:00:00.000Z"),
    updated_at: new Date("2026-10-18T03:00:00.000Z")
  });
}

for (const [index, size] of sizes.entries()) {
  upsert("sizes", "size_id", {
    size_id: sizeIds[size],
    name: size,
    status: "active"
  });
}

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
    created_at: createdAt
  });

  chartSizes.forEach((sizeLabel, index) => {
    upsert("size_chart_items", "size_chart_item_id", {
      size_chart_item_id: sizeChartId * 100 + index + 1,
      size_chart_id: sizeChartId,
      size_label: sizeLabel,
      chest_cm: isPants ? 0 : 88 + index * 6 + (isOuterwear ? 4 : 0),
      waist_cm: (isPants ? 70 : 72) + index * 6,
      hip_cm: (isPants ? 88 : 90) + index * 6,
      length_cm: (isPants ? 96 : isOuterwear ? 68 : 65) + index * 2,
      notes: "Số đo cơ thể tham khảo"
    });
  });

  upsert("products", "product_id", {
    product_id: productId,
    category_id: categoryId,
    size_chart_id: sizeChartId,
    name: product.name,
    description: product.description,
    material: product.material,
    base_price: configuration.price,
    status: product.active ? "active" : "inactive",
    created_at: createdAt,
    updated_at: createdAt
  });

  product.imageUrls.forEach((imageUrl, index) => {
    upsert("product_images", "image_id", {
      image_id: productId * 10 + index + 1,
      product_id: productId,
      image_url: imageUrl,
      alt_text: `${product.name} - ảnh ${index + 1}`
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
        status: product.active ? "active" : "inactive"
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
