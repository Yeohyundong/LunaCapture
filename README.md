# LunaCapture

Windows x64용 화면 캡처·주석 편집·GIF/MP4 녹화 앱입니다.

[최신 버전 다운로드](https://github.com/Yeohyundong/LunaCapture/releases/latest)

## 설치와 업데이트

1. 최신 Release의 `LunaCapture-버전.zip`을 풀고 `LunaCapture.exe`를 실행합니다.
2. 설치를 선택하면 현재 사용자 계정에 설치됩니다. 관리자 권한은 필요하지 않습니다.
3. 자동 업데이트가 없는 구버전에서 넘어오는 첫 설치는 기존 앱의 작업을 마무리하고 트레이에서 종료한 뒤 진행하세요.

설치된 앱은 실행 시와 실행 중 하루 한 번 최신 정식 Release를 확인합니다. 다운로드는 백그라운드에서 진행하며, 캡처·편집·녹화·저장·GIF 자르기·설정 창이 열려 있으면 적용을 기다립니다. 모든 작업 창이 닫힌 뒤 앱이 스스로 종료하고 업데이트 도우미가 실행 파일만 교체하여 다시 시작합니다. 업데이트 때문에 작업 창을 강제로 닫거나 프로세스를 강제로 종료하지 않습니다.

- 트레이 → **업데이트 확인**: 수동 확인 및 작업 종료 후 적용
- 트레이 → 설정 → **자동 업데이트**: 자동 확인·적용 켜기/끄기
- 설치 없이 실행하는 포터블 파일은 자동으로 교체하지 않습니다.
- 네트워크 오류·파일 검증 실패 시 기존 앱으로 계속 사용합니다.
- Release ZIP의 GitHub SHA-256 digest와 실행 파일 버전을 검증합니다. 설정과 캡처 기록은 교체하지 않습니다.
- 교체 전 실행 파일은 설치 폴더의 `LunaCapture.exe.previous`에 남깁니다. 새 버전이 시작 중 바로 종료되면 이전 실행 파일로 복구하여 자동 확인을 한 차례 건너뛰고 재실행합니다. 실행 중인 새 버전을 강제로 종료하지는 않습니다.

## 사용법과 피드백

- `PrtSc`: 캡처. 캡처 화면에서 `Tab`으로 녹화 모드 전환
- `Shift+PrtSc`: 저장 기록
- 번호 도구: 클릭하면 번호, 드래그하면 영역. 배지 더블클릭으로 번호 직접 수정
- 트레이 → **피드백 보내기**: 기본 메일 앱에서 `hyundongy@gmail.com`으로 메일을 작성합니다. 앱 버전과 작성 양식만 들어가며 전송은 사용자가 직접 합니다. 캡처 이미지·로그·설정은 자동으로 첨부하거나 전송하지 않습니다. 기본 메일 앱이 없으면 받을 주소를 안내합니다.

자세한 조작은 [사용법](tools/사용법.txt)을 참고하세요.

## 개발과 자동 배포

필요: .NET 10 SDK, Windows용 C++ 빌드 도구(Visual Studio 데스크톱 C++ 워크로드).

```powershell
dotnet build LunaCapture.slnx
./publish.ps1
```

`publish.ps1`은 Native AOT 단일 실행 파일을 빌드하고 프로젝트 루트·`dist`·버전별 배포 폴더/ZIP을 갱신합니다.

`src/LunaCapture/LunaCapture.csproj`의 `Version`을 새 `major.minor.patch`로 올려 `main`에 푸시하면 [Release 워크플로](.github/workflows/release.yml)가 다음 작업을 수행합니다.

1. Windows 러너에서 단일 실행 파일 및 ZIP 빌드
2. 배포 실행 파일로 주석 저장·복원, 업데이트 검증 테스트 실행
3. 같은 버전 Release가 없다면 `v버전` Release 생성
4. EXE·라이선스 포함 ZIP·`SHA256SUMS.txt` 업로드 후 최신 Release로 게시

GitHub Actions의 수동 실행도 지원합니다. 이미 게시된 동일 버전은 덮어쓰지 않으므로 새 배포에는 버전을 올려야 합니다. Release는 빌드와 테스트가 끝난 뒤 게시되며, 설치된 앱은 새 Release를 자동으로 받습니다. 기본 `GITHUB_TOKEN`만 사용하고 별도 PAT는 필요하지 않습니다.

자동화의 업로드 대상과 앱의 확인 경로는 같은 `Yeohyundong/LunaCapture` 저장소입니다. 저장소를 옮길 때는 `Services/ReleaseChannel.cs`도 함께 변경하세요.

테스트를 수동 실행할 때는 `LUNA_SETTINGS_DIR`·`LUNA_HISTORY_DIR`를 임시 폴더로 지정하여 실제 사용자 데이터와 분리하세요. 배포 실행 파일에 `--history-selftest 결과파일`, `--update-selftest 결과파일`을 전달할 수 있습니다.

인증서 기반 코드 서명은 아직 적용하지 않았습니다. ZIP 해시 검증은 전송·파일 무결성 확인이며 Windows의 실행 파일 게시자 서명과는 별개입니다.
