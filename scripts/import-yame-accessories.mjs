import fs from 'node:fs/promises'
import path from 'node:path'
const root = path.resolve(import.meta.dirname, '..')
const images = path.resolve(root, '../se310-thesis-FE/public/images/seed-accessories')
await fs.mkdir(images, { recursive: true })
const groups = [
  ['non', 'Nón', ['Freesize']], ['balo', 'Balo', ['Freesize']],
  ['tui-deo', 'Túi xách & túi đeo', ['Freesize']], ['vi', 'Ví', ['Freesize']],
  ['day-nit', 'Thắt lưng', ['110 cm', '120 cm']], ['vo', 'Vớ', ['39–42', '43–46']],
]
const palette = [['Xanh Dương Đậm','#183c64'],['Xanh Dương Nhạt','#a7c7e7'],['Xanh Ngọc Nhạt','#a5d5cc'],['Xanh Lá Đậm','#245843'],['Nâu Đậm','#563b2c'],['Be Nhạt','#e5d5ba'],['Xám Đậm','#555555'],['Xám Nhạt','#c4c4c4'],['Đen','#222222'],['Trắng','#f5f5f5'],['Xám','#888888'],['Nâu','#795548'],['Be','#d9c6a5']]
async function get(url) {
  const r = await fetch(url, { signal: AbortSignal.timeout(60000) })
  if (!r.ok) throw new Error(`${r.status}: ${url}`)
  return r
}
const products = []
for (const [collection, category, sizes] of groups) {
  const data = await (await get(`https://yame.vn/collections/${collection}/products.json?limit=4`)).json()
  if (data.products?.length !== 4) throw new Error(`Missing source products: ${collection}`)
  for (const [i, p] of data.products.entries()) {
    const key = `${collection}-${p.id}`
    const imageUrls = []
    for (const [j, img] of p.images.slice(0, 2).entries()) {
      const buffer = Buffer.from(await (await get(`${img.src}${img.src.includes('?') ? '&' : '?'}width=1000`)).arrayBuffer())
      if (buffer.length < 1000) throw new Error(`Invalid image ${key}`)
      await fs.writeFile(path.join(images, `${key}-${j}.jpg`), buffer)
      imageUrls.push(`/images/seed-accessories/${key}-${j}.jpg`)
    }
    if (!imageUrls.length) throw new Error(`Missing image ${key}`)
    const colorTitle = p.title.replace(/\s+\d+$/, '')
    const color = /Free Color/.test(colorTitle) ? ['Phối màu', '#b5a18b'] : palette.find(([name]) => colorTitle.endsWith(name))
    if (!color) throw new Error(`Unknown color: ${p.title}`)
    const price = Number(p.variants.find(v => Number(v.price) > 0)?.price)
    if (!(price > 0)) throw new Error(`Invalid price ${key}`)
    products.push({ key, collection, category, name: p.title, price, sizes,
      color: { name: color[0], hex: color[1] }, imageUrls,
      material: /Da Bò/i.test(p.title) ? 'Da bò' : /Cotton/i.test(p.title) ? 'Cotton' : 'Vải và sợi tổng hợp',
      stock: i === 2 ? 2 : i === 3 ? 0 : 40,
      sourceUrl: `https://yame.vn/products/${p.handle}`,
      sourceImages: p.images.slice(0, 2).map(x => x.src), retrievedAt: new Date().toISOString() })
  }
}
await fs.writeFile(path.join(root, 'mongo-init/mock-data/accessories.json'), JSON.stringify(products, null, 2) + '\n')
console.log(`Saved ${products.length} accessories and ${products.reduce((s,p)=>s+p.imageUrls.length,0)} images`)
