const colors = [
  ['Xanh Dương Nhạt', '#a7c7e7'], ['Xanh Dương Đậm', '#183c64'],
  ['Xám Nhạt', '#c4c4c4'], ['Xám Đậm', '#555555'], ['Đỏ Đậm', '#8b2232'],
  ['Nâu Nhạt', '#b68b69'], ['Đen', '#222222'], ['Trắng', '#f5f5f5'],
  ['Be', '#d9c6a5'], ['Xám', '#888888'], ['Nâu', '#795548'],
]
export function attributes(title) {
  const color = colors.find(([name]) => title.endsWith(name))
  if (!color) throw new Error(`Unknown color: ${title}`)
  let material = 'Vải tổng hợp', materialGroup = 'synthetic'
  if (/Linen/i.test(title)) { material = /Rayon/.test(title) ? 'Linen phối Rayon' : 'Linen dệt'; materialGroup = 'linen' }
  else if (/Cotton/.test(title)) { material = 'Cotton'; materialGroup = 'cotton' }
  else if (/Kaki/.test(title)) { material = 'Kaki'; materialGroup = 'kaki' }
  else if (/Modal/.test(title)) material = 'Modal phối sợi'
  else if (/Viscose/.test(title)) material = 'Viscose phối Polyester'
  return { color: { name: color[0], hex: color[1] }, material, materialGroup }
}
