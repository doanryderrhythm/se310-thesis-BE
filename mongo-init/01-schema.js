const schema = [
  {
    name: "users",
    id: "user_id",
    fields: {
      user_id: "int", full_name: "string", email: "string", phone: "string",
      password_hash: "string", role: "string", status: "string",
      created_at: "date", updated_at: "date"
    },
    required: ["user_id"],
    indexes: []
  },
  {
    name: "addresses",
    id: "address_id",
    fields: {
      address_id: "int", user_id: "int", recipient_name: "string",
      recipient_phone: "string", address_line: "string", ward: "string",
      district: "string", province: "string", is_default: "bool",
      created_at: "date"
    },
    required: ["address_id", "user_id"],
    indexes: ["user_id"]
  },
  {
    name: "categories",
    id: "category_id",
    fields: {
      category_id: "int", name: "string", slug: "string",
      description: "string", status: "string",
      created_at: "date", updated_at: "date"
    },
    required: ["category_id"],
    indexes: []
  },
  {
    name: "size_charts",
    id: "size_chart_id",
    fields: {
      size_chart_id: "int", name: "string", description: "string",
      category_id: "int", created_at: "date"
    },
    required: ["size_chart_id"],
    indexes: ["category_id"]
  },
  {
    name: "size_chart_items",
    id: "size_chart_item_id",
    fields: {
      size_chart_item_id: "int", size_chart_id: "int", size_label: "string",
      chest_cm: "decimal", waist_cm: "decimal", hip_cm: "decimal",
      length_cm: "decimal", notes: "string"
    },
    required: ["size_chart_item_id", "size_chart_id"],
    indexes: ["size_chart_id"]
  },
  {
    name: "sizes",
    id: "size_id",
    fields: { size_id: "int", name: "string", status: "string" },
    required: ["size_id"],
    indexes: []
  },
  {
    name: "colors",
    id: "color_id",
    fields: {
      color_id: "int", name: "string", hex_code: "string", status: "string"
    },
    required: ["color_id"],
    indexes: []
  },
  {
    name: "products",
    id: "product_id",
    fields: {
      product_id: "int", category_id: "int", size_chart_id: "int",
      name: "string", description: "string", material: "string",
      base_price: "decimal", status: "string",
      created_at: "date", updated_at: "date"
    },
    required: ["product_id", "category_id"],
    indexes: ["category_id", "size_chart_id"]
  },
  {
    name: "product_images",
    id: "image_id",
    fields: {
      image_id: "int", product_id: "int", image_url: "string",
      alt_text: "string"
    },
    required: ["image_id", "product_id"],
    indexes: ["product_id"]
  },
  {
    name: "product_variants",
    id: "variant_id",
    fields: {
      variant_id: "int", product_id: "int", size_id: "int",
      color_id: "int", sku: "string", price: "decimal", status: "string"
    },
    required: ["variant_id", "product_id", "size_id", "color_id"],
    indexes: ["product_id", "size_id", "color_id"]
  },
  {
    name: "inventory",
    id: "inventory_id",
    fields: {
      inventory_id: "int", variant_id: "int", quantity_on_hand: "int",
      quantity_reserved: "int", low_stock_threshold: "int",
      updated_at: "date"
    },
    required: ["inventory_id", "variant_id"],
    indexes: ["variant_id"]
  },
  {
    name: "carts",
    id: "cart_id",
    fields: {
      cart_id: "int", user_id: "int", status: "string",
      created_at: "date", updated_at: "date"
    },
    required: ["cart_id"],
    indexes: ["user_id"]
  },
  {
    name: "cart_items",
    id: "cart_item_id",
    fields: {
      cart_item_id: "int", cart_id: "int", variant_id: "int",
      quantity: "int", added_at: "date"
    },
    required: ["cart_item_id", "cart_id", "variant_id"],
    indexes: ["cart_id", "variant_id"]
  },
  {
    name: "coupons",
    id: "coupon_id",
    fields: {
      coupon_id: "int", code: "string", discount_type: "string",
      discount_value: "decimal", minimum_order_amount: "decimal",
      starts_at: "date", expires_at: "date", usage_limit: "int",
      usage_count: "int", status: "string", created_at: "date"
    },
    required: ["coupon_id"],
    indexes: []
  },
  {
    name: "orders",
    id: "order_id",
    fields: {
      order_id: "int", order_code: "string", user_id: "int",
      coupon_id: "int", status: "string", payment_method: "string",
      payment_status: "string", recipient_name: "string",
      recipient_phone: "string", shipping_address_line: "string",
      shipping_ward: "string", shipping_district: "string",
      shipping_province: "string", shipping_postal_code: "string",
      subtotal: "decimal", discount_amount: "decimal",
      shipping_fee: "decimal", total_amount: "decimal",
      customer_note: "string", cancel_reason: "string",
      placed_at: "date", updated_at: "date"
    },
    required: ["order_id"],
    indexes: ["user_id", "coupon_id"]
  },
  {
    name: "order_items",
    id: "order_item_id",
    fields: {
      order_item_id: "int", order_id: "int", variant_id: "int",
      product_name_snapshot: "string", sku_snapshot: "string",
      size_snapshot: "string", color_snapshot: "string",
      quantity: "int", unit_price: "decimal", discount_amount: "decimal",
      line_total: "decimal"
    },
    required: ["order_item_id", "order_id"],
    indexes: ["order_id", "variant_id"]
  },
  {
    name: "order_status_history",
    id: "history_id",
    fields: {
      history_id: "int", order_id: "int", old_status: "string",
      new_status: "string", changed_by: "int", note: "string",
      changed_at: "date"
    },
    required: ["history_id", "order_id"],
    indexes: ["order_id", "changed_by"]
  },
  {
    name: "coupon_usages",
    id: "coupon_usage_id",
    fields: {
      coupon_usage_id: "int", coupon_id: "int", order_id: "int",
      user_id: "int", discount_amount: "decimal", used_at: "date"
    },
    required: ["coupon_usage_id", "coupon_id", "order_id"],
    indexes: ["coupon_id", "order_id", "user_id"]
  },
  {
    name: "product_reviews",
    id: "review_id",
    fields: {
      review_id: "int", product_id: "int", user_id: "int",
      order_item_id: "int", rating: "int", comment: "string",
      status: "string", created_at: "date"
    },
    required: ["review_id", "product_id", "user_id"],
    indexes: ["product_id", "user_id", "order_item_id"]
  }
];

const bsonTypes = {
  int: ["int", "long"],
  decimal: ["decimal", "double", "int", "long"],
  string: ["string"],
  date: ["date"],
  bool: ["bool"]
};

for (const collection of schema) {
  const properties = {};
  for (const [field, type] of Object.entries(collection.fields)) {
    properties[field] = {
      bsonType: bsonTypes[type].concat(
        collection.required.includes(field) ? [] : ["null"]
      )
    };
  }

  const validator = {
    $jsonSchema: {
      bsonType: "object",
      required: collection.required,
      properties
    }
  };
  const exists = db.getCollectionInfos({ name: collection.name }).length > 0;

  if (exists) {
    db.runCommand({
      collMod: collection.name,
      validator,
      validationLevel: "strict",
      validationAction: "error"
    });
  } else {
    db.createCollection(collection.name, {
      validator,
      validationLevel: "strict",
      validationAction: "error"
    });
  }

  db.getCollection(collection.name).createIndex(
    { [collection.id]: 1 },
    { unique: true }
  );

  for (const field of collection.indexes) {
    db.getCollection(collection.name).createIndex({ [field]: 1 });
  }
}
