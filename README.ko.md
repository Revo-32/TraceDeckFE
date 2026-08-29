[English](README.md) | 한국어

<p align="center">
  <img src="src/TraceDeckFE/Assets/TraceDeck_FE_Mini_logo.png" alt="TraceDeck FE 로고" width="180">
</p>

# TraceDeck FE

### Forza Horizon 6 비닐 / 데칼 트레이싱 및 색상 보조 도구

TraceDeck FE는 Forza Horizon 6에서 비닐과 데칼을 직접 따라 그리는 작업을 돕는 Windows 프로그램입니다. 대상 창을 따라가는 레퍼런스 오버레이, 원본 픽셀 색상 추출, Forza HSB 변환, 팔레트와 자체 포함 프로젝트를 하나의 포터블 앱으로 제공합니다.

**최신 버전:** [v1.0.0](https://github.com/Revo-32/TraceDeckFE/releases/latest) · **지원 환경:** Windows 10/11 x64 · **라이선스:** MIT · **상태:** 안정 버전

> TraceDeck FE는 비공식 커뮤니티 도구이며 Microsoft, Xbox, Playground Games 또는 Forza 프랜차이즈와 제휴하거나 그들로부터 승인받은 제품이 아닙니다.

## 목차

- [주요 기능](#주요-기능)
- [다운로드](#다운로드)
- [빠른 시작](#빠른-시작)
- [문서](#문서)
- [지원 형식](#지원-형식)
- [프로젝트 파일](#프로젝트-파일)
- [요구사항](#요구사항)
- [소스에서 빌드](#소스에서-빌드)
- [개발 원칙](#개발-원칙)
- [라이선스](#라이선스)

## 주요 기능

- 수동 트레이싱을 위한 대상 창 기준 레퍼런스 오버레이
- 레퍼런스 이동, 확대/축소, 회전과 좌우·상하 반전
- 레퍼런스 불투명도 조절
- 클릭이 게임으로 통과하는 오버레이 잠금
- 흑백과 대비 보조
- 대상 화면 기준 격자와 중앙선
- 원본 픽셀 색상 추출
- HEX, RGB와 Forza HSB 변환
- 이름을 지정할 수 있는 팔레트와 자동 팔레트 추출
- 원본 레퍼런스를 포함하는 자체 포함 `.TDFE` 프로젝트
- 세션 실행 취소/다시 실행, 자동 저장과 복구
- 사용자 지정 단축키
- 영어/한국어 UI
- 자체 포함 단일 EXE 포터블 Windows 배포

## 다운로드

최신 포터블 ZIP은 [GitHub Releases](https://github.com/Revo-32/TraceDeckFE/releases)에서 내려받습니다. 실행 파일과 ZIP은 소스 저장소에 직접 커밋하지 않습니다.

v1.0.0 파일 이름:

`TraceDeckFE-v1.0.0-win-x64-portable.zip`

## 빠른 시작

1. 최신 포터블 ZIP을 내려받아 압축을 풉니다.
2. `TraceDeckFE.exe`를 실행합니다. 별도의 .NET 설치는 필요하지 않습니다.
3. Forza Horizon 6를 창 모드 또는 테두리 없는 창 모드로 실행합니다.
4. 자동 연결을 기다리거나 대상 창을 직접 선택합니다.
5. 레퍼런스 이미지를 열거나 드래그 앤 드롭 또는 붙여넣기로 불러옵니다.
6. 오버레이 위치를 맞춘 뒤 잠가서 클릭을 통과시키고 Forza에서 트레이싱을 시작합니다.

설정, 로그와 복구 스냅샷은 EXE 옆의 `data/` 폴더에 생성됩니다. TraceDeck FE는 애플리케이션 상태를 AppData에 저장하지 않습니다.

## 문서

- [현재 상태](docs/CURRENT_STATUS.md)
- [개발 명세](docs/DEVELOPMENT_SPEC.md)
- [아키텍처 결정](docs/DECISIONS.md)
- [자산 출처](docs/ASSET_PROVENANCE.md)
- [FH6 수동 검증 체크리스트](docs/FH6_MANUAL_VALIDATION_CHECKLIST.md)
- [v1.0.0 릴리스 노트](docs/releases/v1.0.0.md)

한국어 PDF 사용설명서 `TraceDeck FE 사용방법.pdf`는 포터블 릴리스에 포함됩니다. 재생성 가능한 원본과 검증기는 `tools/build_user_guide.py`, `tools/validate_user_guide.py`로 추적하며, 생성된 PDF는 Git 기록에 반복해서 넣지 않고 릴리스 자산으로 배포합니다.

## 지원 형식

| 종류 | 형식 | 동작 |
| --- | --- | --- |
| 래스터 | PNG, JPG/JPEG, WebP, BMP, TIFF/TIF, ICO, AVIF, GIF | 원본 바이트와 픽셀을 보존합니다. 애니메이션 GIF/WebP는 첫 프레임, TIFF는 첫 페이지를 사용합니다. |
| 벡터 | SVG | 원본 벡터 바이트를 유지하고 현재 보기에 알맞은 해상도로 렌더링합니다. |
| 프로젝트 | `.TDFE` | 버전이 지정된 자체 포함 TraceDeck FE 프로젝트입니다. |

## 프로젝트 파일

`.TDFE` 프로젝트에는 원본 레퍼런스와 함께 변형, 오버레이, 이미지 보조, 가이드, 색상과 팔레트 상태가 저장됩니다. 구조와 레퍼런스 해시 검증이 끝나야 현재 문서를 교체합니다. 세션 실행 취소/다시 실행 이력, 대상 창 핸들과 외부 원본 경로는 저장하지 않습니다.

## 요구사항

### 포터블 릴리스

- Windows 10 또는 Windows 11, x64
- Forza 창 모드 또는 테두리 없는 창 모드 권장
- 별도 .NET 런타임 설치 불필요

### 소스 빌드

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 또는 Windows 데스크톱 빌드를 지원하는 동등한 .NET 도구

## 소스에서 빌드

다음 명령은 저장소 루트에서 실행합니다.

```powershell
dotnet restore TraceDeckFE.sln
dotnet build TraceDeckFE.sln -c Debug --no-restore
dotnet test TraceDeckFE.sln -c Debug --no-build
dotnet build TraceDeckFE.sln -c Release --no-restore
dotnet test TraceDeckFE.sln -c Release --no-build
```

PDF 사용설명서를 만든 뒤 단일 파일 포터블 릴리스를 생성하려면 다음 명령을 사용합니다.

```powershell
python -m pip install -r tools/requirements-docs.txt
python tools/build_user_guide.py --output "output/pdf/TraceDeck FE 사용방법.pdf"
python tools/validate_user_guide.py "output/pdf/TraceDeck FE 사용방법.pdf"
dotnet publish src/TraceDeckFE/TraceDeckFE.csproj -c Release -p:PublishProfile=Portable
```

`tools/Build-V1Release.ps1`은 v1.0.0에서 사용한 배포 폴더와 ZIP 검증 절차를 자동화합니다.

## 개발 원칙

- 게임 코드 주입 및 게임 메모리 수정 없음
- 게임 화면 캡처 없음
- 비닐/데칼 자동 생성 없음
- 이벤트 기반 오버레이와 이미지 처리, 연속 FPS 렌더러 없음
- 파괴적인 최적화보다 원본 레퍼런스 품질 우선
- 색상 추출은 바탕 화면이나 게임 화면이 아닌 보관된 원본 레퍼런스를 사용
- 관계없는 다른 앱은 TopMost가 아닌 오버레이를 자연스럽게 가림

큰 아키텍처 또는 UX 변경을 제안하기 전 [CONTRIBUTING.md](CONTRIBUTING.md)를 확인하세요. 보안 관련 안내는 [SECURITY.md](SECURITY.md)를 참고하세요.

## 라이선스

TraceDeck FE의 독자 작성 소스 코드는 [MIT License](LICENSE)로 공개됩니다.

서드파티 라이브러리, 런타임 구성 요소와 글꼴은 각자의 라이선스를 유지합니다. [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)와 [`licenses/`](licenses/)의 원문을 확인하세요.

제작: **Revo\*32** · GitHub [@Revo-32](https://github.com/Revo-32)

