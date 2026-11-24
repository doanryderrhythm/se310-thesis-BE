param([string]$Project = 'se310-integration', [switch]$Verify)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $mongoScript = @'
db.getSiblingDB('admin').auth(process.env.MONGO_INITDB_ROOT_USERNAME, process.env.MONGO_INITDB_ROOT_PASSWORD);
db = db.getSiblingDB(process.env.MONGO_INITDB_DATABASE || 'fashion-store-data');
try { load('/docker-entrypoint-initdb.d/05-seed-feature-tests.js'); }
catch (error) { print(error.stack); if (error.errInfo) printjson(error.errInfo); throw error; }
'@
    if ($Verify) { $mongoScript = $mongoScript.Replace('05-seed-feature-tests.js', 'verify-feature-tests.js') }
    & docker compose -p $Project exec -T mongodb mongosh --quiet --eval $mongoScript
    if ($LASTEXITCODE -ne 0) { throw 'Feature test seed failed. See MongoDB error above.' }
}
finally { Pop-Location }
