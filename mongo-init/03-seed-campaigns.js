// Sample promotion campaigns for local development. Safe to rerun (upserts by id).
const now = new Date();

const campaigns = [
  {
    campaign: {
      campaign_id: 1, name: "Chào mừng thành viên mới",
      description: "Tặng voucher giảm 10% cho thành viên vừa đăng ký.",
      trigger: "signup", distribution: "issued", status: "active",
      starts_at: new Date("2026-11-01T00:00:00.000Z"),
      ends_at: new Date("2027-06-30T16:59:59.000Z")
    },
    coupon: {
      coupon_id: 1, code: "WELCOME10", discount_type: "percent", discount_value: 10,
      max_discount_amount: 50000, minimum_order_amount: 300000,
      applies_to: "all", category_ids: [], usage_limit: null, per_user_limit: 1,
      valid_days_after_issue: 30
    }
  },
  {
    campaign: {
      campaign_id: 2, name: "Black Friday 2026",
      description: "Giảm 15% (tối đa 150.000đ) cho đơn từ 500.000đ. Số lượng có hạn.",
      trigger: "none", distribution: "public_code", status: "active",
      starts_at: new Date("2026-11-20T00:00:00.000Z"),
      ends_at: new Date("2026-12-01T16:59:59.000Z")
    },
    coupon: {
      coupon_id: 2, code: "BLACKFRIDAY", discount_type: "percent", discount_value: 15,
      max_discount_amount: 150000, minimum_order_amount: 500000,
      applies_to: "all", category_ids: [], usage_limit: 500, per_user_limit: 1,
      valid_days_after_issue: null
    }
  },
  {
    campaign: {
      campaign_id: 3, name: "Tri ân khách hàng",
      description: "Miễn phí vận chuyển cho mọi đơn hàng. Admin phát thủ công cho nhóm khách.",
      trigger: "manual", distribution: "issued", status: "active",
      starts_at: new Date("2026-11-01T00:00:00.000Z"),
      ends_at: new Date("2027-03-31T16:59:59.000Z")
    },
    coupon: {
      coupon_id: 3, code: "FREESHIP", discount_type: "free_shipping", discount_value: 0,
      max_discount_amount: null, minimum_order_amount: 0,
      applies_to: "all", category_ids: [], usage_limit: null, per_user_limit: 1,
      valid_days_after_issue: 30
    }
  },
  // ---- Holiday campaigns (windows are wide so they can be tested right away) ----
  {
    campaign: {
      campaign_id: 4, name: "Siêu sale 11.11",
      description: "Nhập mã 1111: giảm 11% (tối đa 111.000đ) cho đơn từ 300.000đ. 1.111 lượt đầu tiên.",
      trigger: "none", distribution: "public_code", status: "active",
      starts_at: new Date("2026-10-31T17:00:00.000Z"),
      ends_at: new Date("2026-11-30T16:59:59.000Z")
    },
    coupon: {
      coupon_id: 4, code: "1111", discount_type: "percent", discount_value: 11,
      max_discount_amount: 111000, minimum_order_amount: 300000,
      applies_to: "all", category_ids: [], usage_limit: 1111, per_user_limit: 1,
      valid_days_after_issue: null
    }
  },
  {
    campaign: {
      campaign_id: 5, name: "Quà Giáng sinh 24/12",
      description: "Giảm 50.000đ cho đơn từ 400.000đ. Admin phát cho tất cả khách hoặc khách chi tiêu từ một mức.",
      trigger: "manual", distribution: "issued", status: "active",
      starts_at: new Date("2026-11-14T17:00:00.000Z"),
      ends_at: new Date("2026-12-26T16:59:59.000Z")
    },
    coupon: {
      coupon_id: 5, code: "2412", discount_type: "fixed", discount_value: 50000,
      max_discount_amount: null, minimum_order_amount: 400000,
      applies_to: "all", category_ids: [], usage_limit: null, per_user_limit: 1,
      valid_days_after_issue: 30
    }
  },
  {
    campaign: {
      campaign_id: 6, name: "Chào năm mới 1/1",
      description: "Giảm 10% (tối đa 100.000đ) cho đơn từ 300.000đ. Admin phát thủ công.",
      trigger: "manual", distribution: "issued", status: "active",
      starts_at: new Date("2026-11-14T17:00:00.000Z"),
      ends_at: new Date("2027-01-15T16:59:59.000Z")
    },
    coupon: {
      coupon_id: 6, code: "0101", discount_type: "percent", discount_value: 10,
      max_discount_amount: 100000, minimum_order_amount: 300000,
      applies_to: "all", category_ids: [], usage_limit: null, per_user_limit: 1,
      valid_days_after_issue: 30
    }
  },
  {
    campaign: {
      campaign_id: 7, name: "Khách hàng thân thiết",
      description: "Tự động tặng voucher khi tổng chi tiêu (đơn đã giao) đạt 500.000đ.",
      trigger: "spend", trigger_min_spent: 500000, distribution: "issued", status: "active",
      starts_at: new Date("2026-11-14T17:00:00.000Z"),
      ends_at: new Date("2027-06-30T16:59:59.000Z")
    },
    coupon: {
      coupon_id: 7, code: "THANTHIET", discount_type: "percent", discount_value: 15,
      max_discount_amount: 120000, minimum_order_amount: 500000,
      applies_to: "all", category_ids: [], usage_limit: null, per_user_limit: 1,
      valid_days_after_issue: 60
    }
  }
];

for (const { campaign, coupon } of campaigns) {
  db.campaigns.updateOne(
    { campaign_id: campaign.campaign_id },
    {
      $set: { ...campaign, updated_at: now },
      $setOnInsert: { created_at: now }
    },
    { upsert: true }
  );
  db.coupons.updateOne(
    { coupon_id: coupon.coupon_id },
    {
      $set: {
        ...coupon,
        campaign_id: campaign.campaign_id,
        starts_at: campaign.starts_at,
        expires_at: campaign.ends_at,
        status: "active",
        updated_at: now
      },
      // Never reset usage_count when the seed is rerun.
      $setOnInsert: { usage_count: 0, created_at: now }
    },
    { upsert: true }
  );
}

// Keep the id counters ahead of the seeded ids so new campaigns do not collide.
for (const name of ["campaigns", "coupons"]) {
  db.counters.updateOne(
    { _id: name },
    { $max: { sequence_value: campaigns.length } },
    { upsert: true }
  );
}
