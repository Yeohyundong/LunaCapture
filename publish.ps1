# LunaCapture 배포 빌드
# 1) exe 하나(Native AOT)로 빌드 → dist\
# 2) 팀원에게 줄 파일을 배포\LunaCapture-<버전>\ 폴더와 zip 으로 정리
# 필요: .NET 10 SDK + Visual Studio "C++를 사용한 데스크톱 개발"
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# AOT 링커가 Visual Studio 위치를 찾을 때 쓰는 vswhere.exe 가 기본 PATH 에 없어서 추가
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"

$csproj = 'src\LunaCapture\LunaCapture.csproj'
$version = ([xml](Get-Content $csproj -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "LunaCapture $version 빌드 중... (처음에는 몇 분 걸릴 수 있어요)"

dotnet publish $csproj -c Release -r win-x64 -o build\publish
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }

# dist: 개발용 최신 결과물
New-Item -ItemType Directory -Force dist | Out-Null
Copy-Item build\publish\LunaCapture.exe dist\ -Force
Copy-Item build\publish\Fonts\*.txt dist\ -Force

# 배포 폴더
$name = "LunaCapture-$version"
$pkg = Join-Path '배포' $name
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item build\publish\LunaCapture.exe $pkg -Force
Copy-Item build\publish\Fonts\*.txt $pkg -Force
(Get-Content 'tools\사용법.txt' -Raw -Encoding UTF8).Replace('{VERSION}', $version) |
    Set-Content (Join-Path $pkg '사용법.txt') -Encoding UTF8

$zip = Join-Path '배포' "$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$pkg\*" -DestinationPath $zip

$exeKB = [math]::Round((Get-Item (Join-Path $pkg 'LunaCapture.exe')).Length / 1KB)
$zipKB = [math]::Round((Get-Item $zip).Length / 1KB)
Write-Host ""
Write-Host "완료"
Write-Host "  배포\$name\        (exe $exeKB KB + 사용법 + 라이선스)"
Write-Host "  배포\$name.zip     ($zipKB KB) ← 팀원에게 이 파일을 보내세요"
