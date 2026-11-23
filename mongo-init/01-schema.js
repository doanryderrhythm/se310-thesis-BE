const schema = [
  {
    name: "users",
    id: "user_id",
    fields: {
      user_id: "int", full_name: "string", email: "string", phone: "string",
      password_hash: "string", role: "string", status: "string",
      status_reason: "string", status_changed_at: "date", password_changed_at: "date",
      created_at: "date", updated_at: "date"
    },
    required: ["user_id", "full_name", "phone", "password_hash"],
    indexes: [],
    uniqueIndexes: ["phone"]
  },
  {
    name: "addresses",
    id: "address_id",
    fields: {
      address_id: "int", user_id: "int", recipient_name: "string",
      recipient_phone: "string", address_line: "string", ward: "string",
      district: "string", province: "string", is_default: "bool",
      province_code: "int", district_code: "int", ward_code: "int",
      created_at: "date"
    },
    required: ["address_id", "user_id"],
    indexes: ["user_id"]
  },
  {
    name: "provinces",
    id: "province_code",
    fields: { province_code: "int", name: "string" },
    required: ["province_code", "name"],
    indexes: []
  },
  {
    name: "districts",
    id: "district_code",
    fields: { district_code: "int", province_code: "int", name: "string" },
    required: ["district_code", "province_code", "name"],
    indexes: ["province_code"]
  },
  {
    name: "wards",
    id: "ward_code",
    fields: { ward_code: "int", district_code: "int", province_code: "int", name: "string" },
    required: ["ward_code", "district_code", "province_code", "name"],
    indexes: ["district_code"]
  },
  {
    name: "categories",
    id: "category_id",
    fields: {
      category_id: "int", name: "string", slug: "string",
      description: "string", status: "string", display_order: "int",
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
      category_id: "int", is_default: "bool", created_at: "date", updated_at: "date"
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
      length_cm: "decimal", notes: "string",
      height_min_cm: "decimal", height_max_cm: "decimal",
      weight_min_kg: "decimal", weight_max_kg: "decimal", display_order: "int"
    },
    required: ["size_chart_item_id", "size_chart_id"],
    indexes: ["size_chart_id"]
  },
  {
    name: "sizes",
    id: "size_id",
    fields: { size_id: "int", name: "string", status: "string", display_order: "int" },
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
    name: "materials",
    id: "material_id",
    fields: {
      material_id: "int", name: "string", slug: "string", description: "string",
      display_order: "int", status: "string", created_at: "date", updated_at: "date"
    },
    required: ["material_id", "name"],
    indexes: [],
    uniqueIndexes: ["slug"]
  },
  {
    name: "occasions",
    id: "occasion_id",
    fields: {
      occasion_id: "int", name: "string", slug: "string", description: "string",
      display_order: "int", status: "string", created_at: "date", updated_at: "date"
    },
    required: ["occasion_id", "name"],
    indexes: [],
    uniqueIndexes: ["slug"]
  },
  {
    name: "media",
    id: "media_id",
    fields: {
      media_id: "int", file: "string", width: "int", height: "int", widths: "array",
      original_format: "string", original_bytes: "int", uploaded_by: "int", created_at: "date"
    },
    required: ["media_id", "file"],
    indexes: []
  },
  {
    name: "suppliers",
    id: "supplier_id",
    fields: {
      supplier_id: "int", name: "string", phone: "string", email: "string",
      address: "string", tax_code: "string", note: "string", status: "string",
      created_at: "date", updated_at: "date"
    },
    required: ["supplier_id", "name"],
    indexes: []
  },
  {
    name: "goods_receipts",
    id: "receipt_id",
    fields: {
      receipt_id: "int", receipt_code: "string", supplier_id: "int", supplier_name: "string",
      supplier_invoice_no: "string", received_at: "date", note: "string", status: "string",
      items: "array", total_quantity: "int", total_amount: "decimal",
      created_by: "int", created_at: "date", updated_at: "date",
      completed_at: "date", completed_by: "int", cancelled_at: "date", cancel_reason: "string",
      completion_token: "string", completion_locked_at: "date"
    },
    required: ["receipt_id", "receipt_code", "supplier_id"],
    indexes: ["supplier_id"],
    uniqueIndexes: ["receipt_code"]
  },
  {
    name: "products",
    id: "product_id",
    fields: {
      product_id: "int", category_id: "int", size_chart_id: "int",
      name: "string", slug: "string", description: "string", material: "string",
      base_price: "decimal", status: "string",
      material_id: "int", occasion_ids: "array", care_instructions: "string", search_text: "string",
      created_at: "date", updated_at: "date"
    },
    required: ["product_id", "category_id"],
    indexes: ["category_id", "size_chart_id", "material_id", "occasion_ids"]
  },
  {
    name: "product_images",
    id: "image_id",
    fields: {
      image_id: "int", product_id: "int", image_url: "string",
      alt_text: "string", color_id: "int", sort_order: "int", is_primary: "bool",
      media_id: "int", media_file: "string", width: "int", height: "int", widths: "array",
      focal_x: "decimal", focal_y: "decimal"
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
    indexes: ["product_id", "size_id", "color_id"],
    uniqueIndexes: ["sku"]
  },
  {
    name: "inventory",
    id: "inventory_id",
    fields: {
      inventory_id: "int", variant_id: "int", quantity_on_hand: "int",
      quantity_reserved: "int", low_stock_threshold: "int", average_cost: "decimal",
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
      quantity: "int", added_at: "date", selected: "bool", price_at_add: "decimal"
    },
    required: ["cart_item_id", "cart_id", "variant_id"],
    indexes: ["cart_id", "variant_id"]
  },
  {
    name: "coupons",
    id: "coupon_id",
    fields: {
      coupon_id: "int", campaign_id: "int", code: "string", discount_type: "string",
      discount_value: "decimal", max_discount_amount: "decimal",
      minimum_order_amount: "decimal", applies_to: "string", category_ids: "array",
      starts_at: "date", expires_at: "date", usage_limit: "int",
      usage_count: "int", per_user_limit: "int", valid_days_after_issue: "int",
      status: "string", created_at: "date", updated_at: "date"
    },
    required: ["coupon_id", "campaign_id", "code"],
    indexes: [],
    uniqueIndexes: ["code", "campaign_id"]
  },
  {
    name: "campaigns",
    id: "campaign_id",
    fields: {
      campaign_id: "int", name: "string", description: "string",
      trigger: "string", trigger_min_spent: "decimal",
      distribution: "string", status: "string",
      starts_at: "date", ends_at: "date", created_by: "int",
      created_at: "date", updated_at: "date"
    },
    required: ["campaign_id", "name"],
    indexes: []
  },
  {
    name: "user_coupons",
    id: "user_coupon_id",
    fields: {
      user_coupon_id: "int", coupon_id: "int", campaign_id: "int",
      user_id: "int", status: "string", issued_at: "date",
      expires_at: "date", used_order_id: "int", used_at: "date"
    },
    required: ["user_coupon_id", "coupon_id", "user_id"],
    indexes: ["campaign_id"]
  },
  {
    name: "notifications",
    id: "notification_id",
    fields: {
      notification_id: "int", user_id: "int", type: "string",
      title: "string", body: "string", read_at: "date", created_at: "date"
    },
    required: ["notification_id", "user_id"],
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
      shipping_province_code: "int", shipping_district_code: "int",
      shipping_ward_code: "int",
      subtotal: "decimal", discount_amount: "decimal",
      shipping_fee: "decimal", total_amount: "decimal",
      customer_note: "string", cancel_reason: "string",
      placed_at: "date", updated_at: "date",
      delivered_at: "date", paid_at: "date", refunded_at: "date",
      carrier: "string", tracking_code: "string", return_inspection: "string"
    },
    required: ["order_id"],
    indexes: ["user_id", "coupon_id"],
    uniqueIndexes: ["order_code"]
  },
  {
    name: "returns",
    id: "return_id",
    fields: {
      return_id: "int", return_code: "string", order_id: "int",
      order_code: "string", user_id: "int", status: "string",
      reason: "string", description: "string",
      estimated_refund: "decimal", refund_amount: "decimal",
      refund_reference: "string", reject_reason: "string",
      assignee_id: "int", requested_at: "date", updated_at: "date",
      refunded_at: "date", items: "array", events: "array"
    },
    required: ["return_id", "return_code", "order_id", "user_id"],
    indexes: ["order_id", "user_id"],
    uniqueIndexes: ["return_code"]
  },
  {
    name: "inventory_movements",
    id: "movement_id",
    fields: {
      movement_id: "int", variant_id: "int", type: "string",
      delta_on_hand: "int", delta_reserved: "int", on_hand_after: "int",
      reserved_after: "int", reason: "string", ref_type: "string",
      ref_id: "int", actor_id: "int", created_at: "date"
    },
    required: ["movement_id", "variant_id"],
    indexes: []
  },
  {
    name: "order_items",
    id: "order_item_id",
    fields: {
      order_item_id: "int", order_id: "int", variant_id: "int",
      product_id: "int", product_name_snapshot: "string", sku_snapshot: "string",
      size_snapshot: "string", color_snapshot: "string", image_url_snapshot: "string",
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
      coupon_usage_id: "int", coupon_id: "int", user_coupon_id: "int",
      order_id: "int", user_id: "int", discount_amount: "decimal",
      used_at: "date", status: "string", released_at: "date"
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
  bool: ["bool"],
  array: ["array"]
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
  if (collection.name === "users") {
    validator.$jsonSchema.properties.phone.pattern = "^[0-9]{10}$";
  }
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

  for (const field of collection.uniqueIndexes || []) {
    db.getCollection(collection.name).createIndex(
      { [field]: 1 },
      { unique: true, name: `${field}_unique` }
    );
  }
}
