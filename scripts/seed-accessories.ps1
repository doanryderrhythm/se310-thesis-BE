param([string]$Project = 'se310-integration')
$ErrorActionPreference = 'Stop'
if ($Project -ne 'se310-integration') { throw 'This script targets the local integration database only.' }
$script = @'
db.getSiblingDB('admin').auth(process.env.MONGO_INITDB_ROOT_USERNAME, process.env.MONGO_INITDB_ROOT_PASSWORD);
if (process.env.MONGO_INITDB_DATABASE !== 'fashion-store-data') throw new Error('Unexpected database');
db = db.getSiblingDB('fashion-store-data');
load('/docker-entrypoint-initdb.d/verify-accessories.js');
'@
& docker exec "$Project-mongodb-1" mongosh --quiet --eval $script
if ($LASTEXITCODE -ne 0) { throw 'Accessories seed failed.' }
