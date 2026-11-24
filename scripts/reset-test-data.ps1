param([string]$Project = 'se310-integration')
$ErrorActionPreference = 'Stop'
if ($Project -ne 'se310-integration') { throw 'Reset is restricted to the local se310-integration project.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiStopped = $false
$databaseReset = $false
$completed = $false
Push-Location $repoRoot
try {
    $container = "$Project-mongodb-1"
    $backupName = 'before-reset-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.archive.gz'
    $backupDirectory = Join-Path $repoRoot '.local-backups'
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $preflight = @'
db.getSiblingDB('admin').auth(process.env.MONGO_INITDB_ROOT_USERNAME, process.env.MONGO_INITDB_ROOT_PASSWORD);
if (process.env.MONGO_INITDB_DATABASE !== 'fashion-store-data') throw new Error('Unexpected database; reset aborted');
const fs = require('fs');
for (const name of ['products', 'variants']) {
  const rows = JSON.parse(fs.readFileSync('/docker-entrypoint-initdb.d/mock-data/' + name + '.json', 'utf8'));
  if (rows.length < 24) throw new Error('New catalog snapshot is missing');
}
'@
    & docker exec $container mongosh --quiet --eval $preflight
    if ($LASTEXITCODE -ne 0) { throw 'Preflight failed; no data changed.' }
    & docker compose -p $Project stop api
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop API; no data changed.' }
    $apiStopped = $true
    $dumpCommand = 'mongodump --host 127.0.0.1 --username "$MONGO_INITDB_ROOT_USERNAME" --password "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin --db "$MONGO_INITDB_DATABASE" --archive=/tmp/' + $backupName + ' --gzip --quiet'
    & docker exec $container sh -c $dumpCommand
    if ($LASTEXITCODE -ne 0) { throw 'Backup failed; no data changed.' }
    & docker cp "${container}:/tmp/$backupName" (Join-Path $backupDirectory $backupName)
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy backup; no data changed.' }
    $auth = "db.getSiblingDB('admin').auth(process.env.MONGO_INITDB_ROOT_USERNAME, process.env.MONGO_INITDB_ROOT_PASSWORD); db = db.getSiblingDB('fashion-store-data'); "
    & docker exec $container mongosh --quiet --eval ($auth + 'db.dropDatabase();')
    if ($LASTEXITCODE -ne 0) { throw 'Database reset failed.' }
    $databaseReset = $true
    foreach ($file in @('01-schema.js', '02-seed-products.js', '02a-seed-locations.js', '03-seed-campaigns.js', '04-seed-demo-data.js', '05-seed-feature-tests.js', '06-seed-accessories.js', 'verify-feature-tests.js')) {
        & docker exec $container mongosh --quiet --eval ($auth + "load('/docker-entrypoint-initdb.d/$file');")
        if ($LASTEXITCODE -ne 0) { throw "Seed failed at $file. Backup: $backupDirectory/$backupName" }
    }
    $completed = $true
    Write-Host "Reset and verification complete. Backup: $backupDirectory/$backupName"
}
finally {
    if ($apiStopped -and (!$databaseReset -or $completed)) {
        & docker compose -p $Project start api
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Start API failed. Run docker compose -p se310-integration start api.' }
    }
    elseif ($databaseReset) { Write-Warning 'Seed failed. API remains stopped; restore the backup or fix the seed and rerun reset.' }
    Pop-Location
}
