// Vietnamese administrative units, 3 levels (province -> district -> ward), as they were before
// the July 2025 reorganisation: 63 provinces, 696 districts, 10,051 wards.
// Source: provinces.open-api.vn (API v1, public data). Safe to rerun: the three collections are
// replaced as a whole.
const fs = require("fs");
const data = JSON.parse(fs.readFileSync("/docker-entrypoint-initdb.d/mock-data/vn-locations.json", "utf8"));

const provinces = [];
const districts = [];
const wards = [];
for (const p of data) {
  provinces.push({ province_code: p.code, name: p.name });
  for (const d of p.districts) {
    districts.push({ district_code: d.code, province_code: p.code, name: d.name });
    for (const [code, name] of d.wards) {
      wards.push({ ward_code: code, district_code: d.code, province_code: p.code, name });
    }
  }
}

for (const [name, docs] of [["provinces", provinces], ["districts", districts], ["wards", wards]]) {
  db.getCollection(name).deleteMany({});
  db.getCollection(name).insertMany(docs, { ordered: false });
}

print(`Seeded ${provinces.length} provinces, ${districts.length} districts, ${wards.length} wards.`);
