param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
$exe = (Resolve-Path -LiteralPath 'build/publish/LunaCapture.exe').Path
$zip = (Resolve-Path -LiteralPath "배포/LunaCapture-$Version.zip").Path
$actual = (Get-Item -LiteralPath $exe).VersionInfo
if ("$($actual.FileMajorPart).$($actual.FileMinorPart).$($actual.FileBuildPart)" -ne $Version) { throw 'Executable version mismatch' }
$sha = Join-Path (Get-Location) 'build/publish/SHA256SUMS.txt'
@($exe, $zip) | ForEach-Object { "$( (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() )  $([IO.Path]::GetFileName($_))" } | Set-Content -LiteralPath $sha -Encoding utf8
$notes = Join-Path (Get-Location) 'build/publish/release-notes.md'
@"
LunaCapture $Version (Windows x64)

- 설치된 앱은 시작할 때와 하루 한 번 업데이트를 확인합니다.
- 편집·캡처·녹화·GIF 자르기 등 모든 작업 창이 닫힌 뒤 자동으로 적용합니다.
- 처음 설치하거나 자동 업데이트가 없는 구버전은 ZIP을 내려받아 LunaCapture.exe를 실행하고 설치/업데이트를 선택하세요.
- 피드백: 편집 창 설정 또는 트레이의 '이거 고쳐주세요!' (기본 메일 앱 사용)

자동 업데이트는 GitHub의 SHA-256 digest가 확인된 ZIP만 적용합니다.
"@ | Set-Content -LiteralPath $notes -Encoding utf8
$target = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { git rev-parse HEAD }
if (!$target) { throw 'Cannot resolve the release commit' }
gh release create "v$Version" $exe $zip $sha --target $target --title "LunaCapture $Version" --notes-file $notes --latest
if ($LASTEXITCODE -ne 0) { throw 'GitHub release creation failed' }
