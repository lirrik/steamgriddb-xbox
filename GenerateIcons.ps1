#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates regular and inverted icons for the SteamGridDB Xbox widget using ImageMagick.

.DESCRIPTION
    Takes icon.png and generates icons in sizes: 256, 44, 32, 24, 20, and 16.
    Creates both regular versions and inverted (light theme) versions.
    Light theme versions have inverted colors and use ".light." in the filename.
    Output: SteamGridDB.Xbox\Assets\Icons\
    
    REQUIRES: ImageMagick (magick command)
    Download: https://imagemagick.org/script/download.php

.PARAMETER SourceImage
    Source icon image path. Default: icon.png

.PARAMETER OutputDir
    Output directory. Default: SteamGridDB.Xbox\Assets\Icons

.EXAMPLE
    .\GenerateIcons.ps1
    Generates regular and light theme icons from icon.png
#>

param(
    [string]$SourceImage = "icon.png",
    [string]$OutputDir = "SteamGridDB.Xbox\Assets\Icons"
)

$ErrorActionPreference = "Stop"

# Icon sizes required by Xbox widget
$sizes = @(256, 44, 32, 24, 20, 16)

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host " SteamGridDB Xbox Widget - Icon Generator" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Check if ImageMagick is installed
Write-Host "Checking dependencies..." -ForegroundColor Yellow
try {
    $null = magick --version 2>&1
    Write-Host "  [OK] ImageMagick installed" -ForegroundColor Green
} catch {
    Write-Host "  [FAIL] ImageMagick not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install ImageMagick:" -ForegroundColor Yellow
    Write-Host "  1. Download from: https://imagemagick.org/script/download.php" -ForegroundColor White
    Write-Host "  2. During install, check 'Add to PATH'" -ForegroundColor White
    Write-Host "  3. Restart PowerShell after installation" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host ""

# Resolve paths
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) {
    $scriptDir = Get-Location
}

$sourceImagePath = $SourceImage
if (-not [System.IO.Path]::IsPathRooted($sourceImagePath)) {
    $sourceImagePath = Join-Path $scriptDir $SourceImage
}

if (-not (Test-Path $sourceImagePath)) {
    Write-Host "ERROR: Source image not found!" -ForegroundColor Red
    Write-Host "  Looking for: $sourceImagePath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Please ensure icon.png exists in the solution root." -ForegroundColor White
    Write-Host ""
    exit 1
}

$outputDirPath = $OutputDir
if (-not [System.IO.Path]::IsPathRooted($outputDirPath)) {
    $outputDirPath = Join-Path $scriptDir $OutputDir
}

# Create output directory
if (-not (Test-Path $outputDirPath)) {
    Write-Host "Creating output directory..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $outputDirPath -Force | Out-Null
    Write-Host "  Created: $outputDirPath" -ForegroundColor Green
    Write-Host ""
}

# Display settings
Write-Host "Settings:" -ForegroundColor Cyan
Write-Host "  Source:     $sourceImagePath" -ForegroundColor Gray
Write-Host "  Output:     $outputDirPath" -ForegroundColor Gray
Write-Host "  Sizes:      $($sizes -join ', ')" -ForegroundColor Gray
Write-Host "  Variants:   Regular + Light theme (inverted)" -ForegroundColor Gray
Write-Host ""

# Generate icons
Write-Host "Generating icons..." -ForegroundColor Yellow
Write-Host ""

$successCount = 0
$failCount = 0

try {
    foreach ($size in $sizes) {
        # Generate regular icon
        $regularFileName = "icon.targetsize-$size.png"
        $regularPath = Join-Path $outputDirPath $regularFileName
        
        Write-Host "  [$size x $size] Regular... " -NoNewline -ForegroundColor Cyan
        
        $args = @(
            $sourceImagePath
            '-resize', "${size}x${size}"
            '-define', 'png:color-type=6'
            '-define', 'png:compression-level=9'
            $regularPath
        )
        
        $output = & magick @args 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            $fileInfo = Get-Item $regularPath
            Write-Host "Done ($($fileInfo.Length) bytes)" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host "Failed" -ForegroundColor Red
            Write-Host "    Error: $output" -ForegroundColor Red
            $failCount++
        }
        
        # Generate light theme (inverted) icon
        $lightFileName = "icon.light.targetsize-$size.png"
        $lightPath = Join-Path $outputDirPath $lightFileName
        
        Write-Host "  [$size x $size] Light...   " -NoNewline -ForegroundColor Cyan
        
        $args = @(
            $sourceImagePath
            '-resize', "${size}x${size}"
            '-channel', 'RGB'
            '-negate'
            '+channel'
            '-define', 'png:color-type=6'
            '-define', 'png:compression-level=9'
            $lightPath
        )
        
        $output = & magick @args 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            $fileInfo = Get-Item $lightPath
            Write-Host "Done ($($fileInfo.Length) bytes)" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host "Failed" -ForegroundColor Red
            Write-Host "    Error: $output" -ForegroundColor Red
            $failCount++
        }
    }
    
    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host "Generation Complete!" -ForegroundColor Cyan
    Write-Host "  Successful: $successCount / $($sizes.Count * 2)" -ForegroundColor Green
    Write-Host "  Failed: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host ""
    
    if ($successCount -gt 0) {
        # Show generated files
        Write-Host "Generated icons:" -ForegroundColor Cyan
        foreach ($size in $sizes) {
            $regularFileName = "icon.targetsize-$size.png"
            $lightFileName = "icon.light.targetsize-$size.png"
            
            $regularPath = Join-Path $outputDirPath $regularFileName
            $lightPath = Join-Path $outputDirPath $lightFileName
            
            if (Test-Path $regularPath) {
                $fileInfo = Get-Item $regularPath
                $sizeKB = [math]::Round($fileInfo.Length / 1KB, 1)
                Write-Host "  - $regularFileName ($sizeKB KB)" -ForegroundColor Gray
            }
            
            if (Test-Path $lightPath) {
                $fileInfo = Get-Item $lightPath
                $sizeKB = [math]::Round($fileInfo.Length / 1KB, 1)
                Write-Host "  - $lightFileName ($sizeKB KB)" -ForegroundColor Gray
            }
        }
        
        Write-Host ""
        Write-Host "Note:" -ForegroundColor Yellow
        Write-Host "  - Regular icons: for dark themes" -ForegroundColor White
        Write-Host "  - Light icons (.light.): for light themes (inverted colors)" -ForegroundColor White
        Write-Host ""
    }
    
} catch {
    Write-Host ""
    Write-Host "ERROR: Failed to generate icons" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    exit 1
}
