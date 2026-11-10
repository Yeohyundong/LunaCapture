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
$changelog = Get-Content -LiteralPath 'CHANGELOG.json' -Raw -Encoding utf8 | ConvertFrom-Json
$entry = @($changelog | Where-Object { $_.version -eq $Version })
if ($entry.Count -ne 1 -or !$entry[0].changes.Count) { throw 'Release changelog entry is missing' }
$changes = ($entry[0].changes | ForEach-Object { "- $_" }) -join [Environment]::NewLine
@"
LunaCapture $Version (Windows x64)

$changes

- 설치된 앱은 시작할 때와 하루 한 번 업데이트를 확인합니다.
- 편집·캡처·녹화·GIF 자르기 등 모든 작업 창이 닫힌 뒤 자동으로 적용합니다.
- 처음 설치하거나 자동 업데이트가 없는 구버전은 ZIP을 내려받아 LunaCapture.exe를 실행하고 설치/업데이트를 선택하세요.
- 피드백: '이거 고쳐주세요!'에서 앱 안에서 바로 접수 (답장 이메일 선택, 실패 시 입력 유지)

지난 변경 사항은 앱의 설정 → 업데이트 내역에서 확인할 수 있습니다.
"@ | Set-Content -LiteralPath $notes -Encoding utf8
$target = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { git rev-parse HEAD }
if (!$target) { throw 'Cannot resolve the release commit' }
gh release create "v$Version" $exe $zip $sha --target $target --title "LunaCapture $Version" --notes-file $notes --latest
if ($LASTEXITCODE -ne 0) { throw 'GitHub release creation failed' }
