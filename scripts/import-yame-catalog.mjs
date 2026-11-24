import fs from 'node:fs/promises'
import path from 'node:path'
import { attributes } from './yame-product-attributes.mjs'

// Explicit refresh only; normal database resets use the checked-in snapshot.
const root = path.resolve(import.meta.dirname, '..')
const imageRoot = path.resolve(root, '../se310-thesis-FE/public/images/seed-yame')
const groups = [
  ['ao-thun-det-kim', 'CAT1', 'cotton', ['daily', 'casual']],
  ['ao-so-mi', 'CAT2', 'cotton', ['work', 'school']],
  ['quan-dai', 'CAT3', 'kaki', ['daily', 'work']],
  ['ao-khoac', 'CAT4', 'synthetic', ['outdoor', 'casual']],
]
async function get(url) {
  const response = await fetch(url, { signal: AbortSignal.timeout(60000) })
  if (!response.ok) throw new Error(`${response.status}: ${url}`)
  return response
}
const products = [], variants = [], sources = []
await fs.mkdir(imageRoot, { recursive: true })
for (const [collection, categoryId, materialGroup, suitableFor] of groups) {
  const url = `https://yame.vn/collections/${collection}/products.json?limit=8`
  const data = await (await get(url)).json()
  if (data.products?.length < 6) throw new Error(`Not enough products: ${collection}`)
  for (const item of data.products.slice(0, 6)) {
    const id = `P${String(products.length + 1).padStart(2, '0')}`
    const imageUrls = []
    for (const [index, image] of item.images.slice(0, 2).entries()) {
      const filename = `${id}-${index + 1}.jpg`
      const bytes = Buffer.from(await (await get(`${image.src}${image.src.includes('?') ? '&' : '?'}width=1000`)).arrayBuffer())
      if (bytes.length < 1000) throw new Error(`Invalid image: ${image.src}`)
      await fs.writeFile(path.join(imageRoot, filename), bytes)
      imageUrls.push(`/images/seed-yame/${filename}`)
    }
    if (!imageUrls.length) throw new Error(`Missing images: ${item.handle}`)
    const price = Number(item.variants.find(v => Number(v.price) > 0)?.price)
    if (!Number.isFinite(price) || price <= 0) throw new Error(`Invalid price: ${item.handle}`)
    const attrs = attributes(item.title)
    products.push({ id, categoryId, sizeChartId: `SC-${id}`, name: item.title,
      description: `${item.title}. Dữ liệu tham khảo YaMe phục vụ kiểm thử. Size, tồn kho và bảng số đo là dữ liệu mô phỏng của Themis.`,
      material: attrs.material,
      materialGroup: attrs.materialGroup, suitableFor, careInstructions: 'Giặt nhẹ với nước mát; lộn trái, không tẩy; phơi nơi thoáng mát.',
      imageUrls, active: true, createdAt: new Date(Date.now() - products.length * 86400000).toISOString() })
    variants.push({ productId: id, price, sizes: ['S', 'M', 'L', 'XL'],
      colors: [attrs.color] })
    sources.push({ id, url: `https://yame.vn/products/${item.handle}`, originalTitle: item.title,
      price, images: item.images.slice(0, 2).map(i => i.src), retrievedAt: new Date().toISOString() })
  }
}
for (const [name, data] of Object.entries({ products, variants, 'catalog-sources': sources }))
  await fs.writeFile(path.join(root, 'mongo-init/mock-data', `${name}.json`), JSON.stringify(data, null, 2) + '\n')
console.log(`Saved ${products.length} products, ${products.length * 4} variants and local images.`)
