import assert from 'node:assert/strict'

const base = process.env.API_BASE_URL || 'http://127.0.0.1:8082/api'
let checks = 0
async function request(path, token, body, expected = 200) {
  const response = await fetch(base + path, {
    method: body ? 'POST' : 'GET',
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  })
  assert.equal(response.status, expected, `${path}: ${response.status}`)
  checks++
  const text = await response.text()
  return text ? JSON.parse(text) : null
}
const login = phone => request('/auth/login', null, { phone, password: 'Demo@12345' })
const { accessToken: customer } = await login('0900000021')
const product = await request('/products/slug/test-trai-nghiem-1')
const reviews = await request(`/products/${product.productId}/reviews`)
assert.equal(reviews.reviewCount, 5)
assert.equal(reviews.averageRating, 3)
for (let rating = 1; rating <= 5; rating++) {
  const filtered = await request(`/products/${product.productId}/reviews?rating=${rating}`)
  assert.equal(filtered.totalCount, 1)
  assert.equal(filtered.averageRating, 3)
  assert.equal(filtered.ratingCounts[rating], 1)
}
const counts = await request('/customers/me/orders/counts', customer)
for (const status of ['pending', 'confirmed', 'packing', 'shipping', 'delivered', 'cancelled', 'delivery_failed', 'returning_to_seller', 'returned_to_seller'])
  assert(counts.byStatus[status] >= 1, status)
const detail = await request('/customers/me/orders/TEST-DELIVERED', customer)
assert.equal(detail.items.length, 3)
assert(detail.items.every(item => item.imageUrl && item.sku && item.unitPrice > 0))
assert(detail.canReturn)
const old = await request('/customers/me/orders/TEST-OUTSIDE-RETURN-WINDOW', customer)
assert.equal(old.canReturn, false)
const eligible = await request('/customers/me/reviewable-items', customer)
assert(eligible.some(item => item.productId === product.productId))
const mine = await request('/customers/me/reviews', customer)
assert.equal(mine.length, 2)
assert(mine.some(review => review.status === 'hidden'))
const cart = await request('/cart', customer)
assert.equal(cart.items.length, 5)
assert(cart.items.some(item => item.priceChanged))
assert(cart.items.some(item => !item.isPurchasable))
assert(cart.items.some(item => item.availableQuantity === 2))
for (const [status, count] of [['available', 2], ['expired', 1], ['used', 1]]) {
  const vouchers = await request(`/customers/me/vouchers?status=${status}`, customer)
  assert.equal(vouchers.length, count)
}
await request('/customers/me/returns', customer)
const notifications = await request('/customers/me/notifications', customer)
assert.equal(notifications.totalCount, 6)
const { accessToken: empty } = await login('0900000027')
const emptyOrders = await request('/customers/me/orders', empty)
assert.equal(emptyOrders.totalCount, 0)
await request('/auth/login', null, { phone: '0900000028', password: 'Demo@12345' }, 401)
await request('/admin/orders', customer, undefined, 403)
const { accessToken: admin } = await login('0900000001')
for (const path of ['/admin/dashboard', '/admin/orders', '/admin/returns', '/admin/reviews', '/admin/inventory', '/admin/goods-receipts', '/admin/campaigns', '/admin/reports'])
  await request(path, admin)
console.log(JSON.stringify({ passedRequests: checks, productId: product.productId, customer: '0900000021', dataMutations: 0 }, null, 2))
