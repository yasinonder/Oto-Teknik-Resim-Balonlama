param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\WAT.DrawingInspector\tessdata")
)

$ErrorActionPreference = "Stop"
$revision = "923915d4ced2a7235221788285785a29c4a42d4a"
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null

foreach ($language in @("eng", "tur")) {
    $fileName = "$language.traineddata"
    $uri = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$revision/$fileName"
    $target = Join-Path $destinationPath $fileName
    $temporary = "$target.download"

    Write-Host "Downloading pinned Tesseract model: $fileName"
    Invoke-WebRequest -Uri $uri -OutFile $temporary

    $download = Get-Item $temporary
    if ($download.Length -lt 100000) {
        Remove-Item $temporary -Force -ErrorAction SilentlyContinue
        throw "Downloaded model is too small or incomplete: $fileName"
    }

    Move-Item $temporary $target -Force
    $hash = (Get-FileHash -Path $target -Algorithm SHA256).Hash
    Write-Host "$fileName ready ($([math]::Round((Get-Item $target).Length / 1MB, 2)) MB), SHA256: $hash"
}

Write-Host "Turkish and English OCR data installed at: $destinationPath"
