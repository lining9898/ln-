param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts/windows"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$dotnet = Join-Path $repo ".tools/dotnet/dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$publishDir = Join-Path $repo "$OutputRoot/publish"
$packageDir = Join-Path $repo "$OutputRoot/package"
$zipPath = Join-Path $repo "$OutputRoot/AIKnowledgeAssistant-$Runtime.zip"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir,$packageDir | Out-Null

& $dotnet publish (Join-Path $repo "src/AiKnowledgeAssistant.Desktop/AiKnowledgeAssistant.Desktop.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$ocrDir = Join-Path $publishDir "tools/ocr"
$tessdataDir = Join-Path $ocrDir "tessdata"
New-Item -ItemType Directory -Force -Path $tessdataDir | Out-Null
$tesseractRoot = "C:\Program Files\Tesseract-OCR"
if (Test-Path (Join-Path $tesseractRoot "tesseract.exe")) {
    Copy-Item (Join-Path $tesseractRoot "*") -Destination $ocrDir -Recurse -Force
}
$pdftoppmCandidates = @(
    $env:AKA_PDFTOPPM,
    (Join-Path $repo ".tools/poppler/pdftoppm.exe"),
    "C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\poppler\Library\bin\pdftoppm.exe"
) | Where-Object { $_ }
$pdftoppm = $pdftoppmCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $pdftoppm) {
    $pdftoppmCommand = Get-Command "pdftoppm.exe" -ErrorAction SilentlyContinue
    $pdftoppm = if ($pdftoppmCommand) { $pdftoppmCommand.Source } else { $null }
}
if ($pdftoppm) {
    Copy-Item $pdftoppm -Destination (Join-Path $ocrDir "pdftoppm.exe") -Force
    $popplerBin = Split-Path $pdftoppm
    Get-ChildItem $popplerBin -Filter "*.dll" -ErrorAction SilentlyContinue |
        Copy-Item -Destination $ocrDir -Force
}
if (-not (Test-Path (Join-Path $tessdataDir "chi_sim.traineddata"))) {
    $localChi = Join-Path $repo ".tools/tessdata/chi_sim.traineddata"
    if (Test-Path $localChi) { Copy-Item $localChi -Destination $tessdataDir -Force }
}
if (-not (Test-Path (Join-Path $ocrDir "tesseract.exe"))) {
    throw "Missing tesseract.exe. Cannot create OCR-capable Windows package."
}
if (-not (Test-Path (Join-Path $ocrDir "pdftoppm.exe"))) {
    throw "Missing pdftoppm.exe. Cannot create scanned-PDF OCR Windows package."
}
if (-not (Test-Path (Join-Path $tessdataDir "chi_sim.traineddata"))) {
    throw "Missing chi_sim.traineddata. Cannot create Chinese OCR package."
}

Copy-Item $publishDir -Destination (Join-Path $packageDir "AIKnowledgeAssistant") -Recurse
Set-Content -Path (Join-Path $packageDir "README.txt") -Encoding UTF8 -Value @"
AI Knowledge Assistant Windows x64 self-contained package

1. Run AIKnowledgeAssistant\AiKnowledgeAssistant.Desktop.exe.
2. User data is stored in %LOCALAPPDATA%\AIKnowledgeAssistant, not in the app folder.
3. The package includes local OCR tools and chi_sim/eng language data.
"@
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force
Write-Output $zipPath
