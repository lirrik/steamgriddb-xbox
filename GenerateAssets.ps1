# PowerShell Script to Generate UWP App Assets from a Logo
# Prerequisites: 
#   1. ImageMagick installed (https://imagemagick.org/script/download.php)
#   2. High-resolution PNG of the logo (e.g., logo.png)

param(
    [Parameter(Mandatory = $true)]
    [string]$SourceLogo,
    
    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "..\Assets"
)

# Check if ImageMagick is installed
try {
    $magickVersion = magick -version
    Write-Host "ImageMagick found: $($magickVersion[0])" -ForegroundColor Green
}
catch {
    Write-Host "ERROR: ImageMagick not found. Please install from https://imagemagick.org/" -ForegroundColor Red
    exit 1
}

# Check if source logo exists
if (-not (Test-Path $SourceLogo)) {
    Write-Host "ERROR: Source logo not found: $SourceLogo" -ForegroundColor Red
    exit 1
}

# Create output directory if it doesn't exist
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Write-Host "Generating UWP assets from: $SourceLogo" -ForegroundColor Cyan
Write-Host "Output directory: $OutputDir" -ForegroundColor Cyan
Write-Host ""

# Define all required assets with their dimensions
$assets = @(
    # LockScreen Logo
    @{Name = "LockScreenLogo.scale-200.png"; Width = 48; Height = 48 },
    
    # Splash Screens
    @{Name = "SplashScreen.scale-100.png"; Width = 620; Height = 300 },
    @{Name = "SplashScreen.scale-125.png"; Width = 775; Height = 375 },
    @{Name = "SplashScreen.scale-150.png"; Width = 930; Height = 450 },
    @{Name = "SplashScreen.scale-200.png"; Width = 1240; Height = 600 },
    @{Name = "SplashScreen.scale-400.png"; Width = 2480; Height = 1200 },
    
    # Square 150x150 Logos
    @{Name = "Square150x150Logo.scale-100.png"; Width = 150; Height = 150 },
    @{Name = "Square150x150Logo.scale-125.png"; Width = 188; Height = 188 },
    @{Name = "Square150x150Logo.scale-150.png"; Width = 225; Height = 225 },
    @{Name = "Square150x150Logo.scale-200.png"; Width = 300; Height = 300 },
    @{Name = "Square150x150Logo.scale-400.png"; Width = 600; Height = 600 },
    
    # Square 44x44 Logos (scale variants)
    @{Name = "Square44x44Logo.scale-100.png"; Width = 44; Height = 44 },
    @{Name = "Square44x44Logo.scale-125.png"; Width = 55; Height = 55 },
    @{Name = "Square44x44Logo.scale-150.png"; Width = 66; Height = 66 },
    @{Name = "Square44x44Logo.scale-200.png"; Width = 88; Height = 88 },
    @{Name = "Square44x44Logo.scale-400.png"; Width = 176; Height = 176 },
    
    # Square 44x44 Logos (target size variants - unplated)
    @{Name = "Square44x44Logo.targetsize-16.png"; Width = 16; Height = 16 },
    @{Name = "Square44x44Logo.targetsize-24.png"; Width = 24; Height = 24 },
    @{Name = "Square44x44Logo.targetsize-24_altform-unplated.png"; Width = 24; Height = 24 },
    @{Name = "Square44x44Logo.targetsize-30.png"; Width = 30; Height = 30 },
    @{Name = "Square44x44Logo.targetsize-32.png"; Width = 32; Height = 32 },
    @{Name = "Square44x44Logo.targetsize-36.png"; Width = 36; Height = 36 },
    @{Name = "Square44x44Logo.targetsize-40.png"; Width = 40; Height = 40 },
    @{Name = "Square44x44Logo.targetsize-44.png"; Width = 44; Height = 44 },
    @{Name = "Square44x44Logo.targetsize-48.png"; Width = 48; Height = 48 },
    @{Name = "Square44x44Logo.targetsize-256.png"; Width = 256; Height = 256 },
    
    # Square 44x44 Logos (altform-unplated variants - transparent, no background plate)
    @{Name = "Square44x44Logo.altform-unplated_targetsize-16.png"; Width = 16; Height = 16 },
    @{Name = "Square44x44Logo.altform-unplated_targetsize-32.png"; Width = 32; Height = 32 },
    @{Name = "Square44x44Logo.altform-unplated_targetsize-48.png"; Width = 48; Height = 48 },
    @{Name = "Square44x44Logo.altform-unplated_targetsize-256.png"; Width = 256; Height = 256 },
    
    # Large Tiles (310x310)
    @{Name = "LargeTile.scale-100.png"; Width = 310; Height = 310 },
    @{Name = "LargeTile.scale-125.png"; Width = 388; Height = 388 },
    @{Name = "LargeTile.scale-150.png"; Width = 465; Height = 465 },
    @{Name = "LargeTile.scale-200.png"; Width = 620; Height = 620 },
    @{Name = "LargeTile.scale-400.png"; Width = 1240; Height = 1240 },
    
    # Small Tiles (71x71)
    @{Name = "SmallTile.scale-100.png"; Width = 71; Height = 71 },
    @{Name = "SmallTile.scale-125.png"; Width = 89; Height = 89 },
    @{Name = "SmallTile.scale-150.png"; Width = 107; Height = 107 },
    @{Name = "SmallTile.scale-200.png"; Width = 142; Height = 142 },
    @{Name = "SmallTile.scale-400.png"; Width = 284; Height = 284 },
    
    # Store Logos
    @{Name = "StoreLogo.scale-100.png"; Width = 50; Height = 50 },
    @{Name = "StoreLogo.scale-125.png"; Width = 63; Height = 63 },
    @{Name = "StoreLogo.scale-150.png"; Width = 75; Height = 75 },
    @{Name = "StoreLogo.scale-200.png"; Width = 100; Height = 100 },
    @{Name = "StoreLogo.scale-400.png"; Width = 200; Height = 200 },
    @{Name = "StoreLogo.backup.png"; Width = 50; Height = 50 },
    
    # Wide 310x150 Logos
    @{Name = "Wide310x150Logo.scale-100.png"; Width = 310; Height = 150 },
    @{Name = "Wide310x150Logo.scale-125.png"; Width = 388; Height = 188 },
    @{Name = "Wide310x150Logo.scale-150.png"; Width = 465; Height = 225 },
    @{Name = "Wide310x150Logo.scale-200.png"; Width = 620; Height = 300 },
    @{Name = "Wide310x150Logo.scale-400.png"; Width = 1240; Height = 600 }
)

$successCount = 0
$failCount = 0

foreach ($asset in $assets) {
    $outputPath = Join-Path $OutputDir $asset.Name
    $width = $asset.Width
    $height = $asset.Height
    
    try {
        # Use ImageMagick to resize with high quality
        # -background transparent: preserve transparency
        # -gravity center: center the image
        # -extent: add padding if needed
        magick convert "$SourceLogo" `
            -background transparent `
            -gravity center `
            -resize "${width}x${height}" `
            -extent "${width}x${height}" `
            "$outputPath"
        
        Write-Host "[OK] Generated: $($asset.Name) (${width}x${height})" -ForegroundColor Green
        $successCount++
    }
    catch {
        Write-Host "[FAIL] Failed to generate: $($asset.Name) - $($_.Exception.Message)" -ForegroundColor Red
        $failCount++
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Generation Complete!" -ForegroundColor Cyan
Write-Host "  Successful: $successCount" -ForegroundColor Green
Write-Host "  Failed: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" }else { "Green" })
Write-Host "  Total Assets: 51" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
