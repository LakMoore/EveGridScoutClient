$tessdataDir = "tessdata"
if (-not (Test-Path $tessdataDir)) {
    New-Item -ItemType Directory -Path $tessdataDir
}

$url = "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata"
$output = Join-Path $tessdataDir "eng.traineddata"

Invoke-WebRequest -Uri $url -OutFile $output
Write-Host "Downloaded English language data to $output"
