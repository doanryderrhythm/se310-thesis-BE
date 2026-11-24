import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
const base = process.env.API_BASE_URL || 'http://127.0.0.1:8082/api'
const rows = JSON.parse(await fs.readFile(new URL('../mongo-init/mock-data/accessories.json', import.meta.url)))
let requests = 0, images = 0
async function get(url) {
  const response = await fetch(base + url)
  assert.equal(response.status, 200, url)
  requests++
  return response.json()
}
const categories = await get('/categories')
for (const name of new Set(rows.map(p => p.category))) {
  const category = categories.find(c => c.name === name)
  assert(category, name)
  const listing = await get('/products?category=' + category.categoryId)
  assert.equal(listing.totalCount, 4, name)
  const available = await get('/products?inStock=true&category=' + category.categoryId)
  assert.equal(available.totalCount, 3, name)
}
for (const p of rows) {
  const detail = await get('/products/slug/phu-kien-' + p.key)
  assert.equal(detail.name, p.name)
  assert.equal(detail.sizeChart, null, 'Accessories must not use clothing size charts')
  assert.deepEqual(detail.variants.map(v => v.size).sort(), [...p.sizes].sort())
  assert(detail.variants.every(v => v.availableQuantity === p.stock && v.price === p.price))
  assert.equal(detail.images.length, 2)
  for (const image of detail.images) {
    const file = path.resolve(import.meta.dirname, '../../se310-thesis-FE/public' + image.imageUrl)
    assert((await fs.stat(file)).size > 1000)
    images++
  }
}
for (const query of ['non', 'vi', 'that lung', 'vo']) {
  const result = await get('/products?q=' + encodeURIComponent(query))
  assert(result.totalCount >= 4, query)
}
console.log(JSON.stringify({ passedRequests: requests, accessories: rows.length, verifiedLocalImages: images }, null, 2))
