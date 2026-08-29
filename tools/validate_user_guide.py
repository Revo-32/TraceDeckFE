from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from pypdf import PdfReader


EXPECTED_CHAPTERS = [
    "1. 시작하기",
    "2. 화면 구성 알아보기",
    "3. 레퍼런스 이미지로 작업하기",
    "4. 이미지를 더 쉽게 따라 그리기",
    "5. 색상 정확하게 가져오기",
    "6. 팔레트 활용하기",
    "7. 처음부터 끝까지 - 실제 작업 예제",
    "8. 프로젝트 저장과 관리",
    "9. 작업을 되돌리고 보호하기",
    "10. 설정 사용자 지정",
    "11. 단축키",
    "12. 문제가 생겼을 때",
    "13. 포터블 버전 사용과 데이터 관리",
    "14. 지원 이미지 형식",
    "15. 자주 묻는 질문과 작업 팁",
    "16. 부록",
]

REQUIRED_TERMS = [
    "5분 빠른 시작",
    "오버레이 잠금",
    "커서 중심",
    "Forza HSB",
    "자동 팔레트",
    ".TDFE",
    "자동 저장",
    "data/settings.json",
    "data/recovery/<Project-ID>/",
    "Windows 실행 경고",
    "GIF",
    "AVIF",
    "비공식 커뮤니티 도구",
]


def dereference(value):
    return value.get_object() if hasattr(value, "get_object") else value


def outline_titles(reader: PdfReader) -> list[str]:
    return [str(getattr(item, "title", "")) for item in reader.outline if not isinstance(item, list)]


def font_inventory(reader: PdfReader) -> dict[str, bool]:
    result: dict[str, bool] = {}
    for page in reader.pages:
        resources = dereference(page.get("/Resources", {})) or {}
        fonts = dereference(resources.get("/Font", {})) or {}
        for reference in fonts.values():
            font = dereference(reference)
            base_name = str(font.get("/BaseFont", ""))
            descriptors = []
            direct_descriptor = font.get("/FontDescriptor")
            if direct_descriptor:
                descriptors.append(dereference(direct_descriptor))
            for descendant_reference in font.get("/DescendantFonts", []) or []:
                descendant = dereference(descendant_reference)
                descriptor = descendant.get("/FontDescriptor")
                if descriptor:
                    descriptors.append(dereference(descriptor))
            embedded = any(
                descriptor.get(key) is not None
                for descriptor in descriptors
                for key in ("/FontFile", "/FontFile2", "/FontFile3")
            )
            result[base_name] = result.get(base_name, False) or embedded
    return result


def image_count(reader: PdfReader) -> tuple[int, int]:
    placements = 0
    unique_ids = set()
    for page in reader.pages:
        resources = dereference(page.get("/Resources", {})) or {}
        xobjects = dereference(resources.get("/XObject", {})) or {}
        for reference in xobjects.values():
            obj = dereference(reference)
            if obj.get("/Subtype") == "/Image":
                placements += 1
                unique_ids.add(getattr(reference, "idnum", id(obj)))
    return placements, len(unique_ids)


def internal_link_count(reader: PdfReader) -> int:
    count = 0
    for page in reader.pages:
        width = float(page.mediabox.width)
        height = float(page.mediabox.height)
        for reference in page.get("/Annots", []) or []:
            annotation = dereference(reference)
            if annotation.get("/Subtype") != "/Link":
                continue
            action = dereference(annotation.get("/A", {})) or {}
            is_internal = annotation.get("/Dest") is not None or action.get("/S") == "/GoTo"
            if is_internal:
                count += 1
            rect = [float(value) for value in annotation.get("/Rect", [])]
            if len(rect) == 4 and not (0 <= rect[0] <= width and 0 <= rect[2] <= width and 0 <= rect[1] <= height and 0 <= rect[3] <= height):
                raise AssertionError(f"page annotation rectangle is outside the page: {rect}")
    return count


def validate(pdf_path: Path) -> dict[str, object]:
    assert pdf_path.is_file(), f"PDF is missing: {pdf_path}"
    assert pdf_path.stat().st_size > 100_000, "PDF is unexpectedly small"
    assert pdf_path.read_bytes()[:5] == b"%PDF-", "file does not have a PDF signature"

    reader = PdfReader(str(pdf_path))
    assert not reader.is_encrypted, "PDF must not be encrypted"
    assert len(reader.pages) >= 20, "guide has too few pages"

    a4_width = 595.275590551
    a4_height = 841.88976378
    page_texts = []
    for index, page in enumerate(reader.pages, start=1):
        width = float(page.mediabox.width)
        height = float(page.mediabox.height)
        assert abs(width - a4_width) < 1.0 and abs(height - a4_height) < 1.0, f"page {index} is not A4 portrait"
        assert width < height, f"page {index} is not portrait"
        assert int(page.get("/Rotate", 0) or 0) % 360 == 0, f"page {index} has unexpected rotation"
        text = page.extract_text() or ""
        assert len(text.strip()) >= 15, f"page {index} is empty or nearly empty"
        page_texts.append(text)

    metadata = reader.metadata
    assert metadata.title == "TraceDeck FE v1.0.0 사용방법"
    assert metadata.subject == "TraceDeck FE User Guide"
    assert metadata.creator == "TraceDeck FE Documentation"
    assert not (metadata.author or "").strip(), "author metadata must be blank"

    top_titles = outline_titles(reader)
    assert top_titles == EXPECTED_CHAPTERS, f"unexpected top-level outline: {top_titles}"

    full_text = "\n".join(page_texts)
    assert "목차" in "\n".join(page_texts[1:4]), "table of contents was not found near the front"
    for chapter in EXPECTED_CHAPTERS:
        assert chapter.split(". ", 1)[1] in full_text, f"missing chapter text: {chapter}"
    for term in REQUIRED_TERMS:
        assert term in full_text, f"missing required guide term: {term}"
    assert len(re.findall(r"[가-힣]", full_text)) >= 5_000, "guide does not contain enough Korean text"
    for forbidden in ("C:\\Users\\", "\\AppData\\", "\\Downloads\\", "\\Documents\\"):
        assert forbidden not in full_text, f"internal path leaked into PDF text: {forbidden}"

    for guide_page, text in enumerate(page_texts[1:], start=1):
        assert "TraceDeck FE v1.0.0" in text, f"missing footer on guide page {guide_page}"
        assert re.search(rf"(?:^|\s){guide_page}(?:\s|$)", text), f"missing page number {guide_page}"

    fonts = font_inventory(reader)
    for weight in ("Pretendard-Regular", "Pretendard-Medium", "Pretendard-SemiBold"):
        matches = [embedded for name, embedded in fonts.items() if weight in name]
        assert matches and all(matches), f"{weight} is missing or not embedded"

    link_count = internal_link_count(reader)
    assert link_count >= 16, "clickable internal TOC links were not found"
    image_placements, unique_images = image_count(reader)
    assert image_placements >= 1 and unique_images >= 1, "branding image is missing"

    return {
        "status": "PASS",
        "path": str(pdf_path.resolve()),
        "bytes": pdf_path.stat().st_size,
        "pages": len(reader.pages),
        "page_size": "A4 portrait",
        "top_level_bookmarks": len(top_titles),
        "internal_links": link_count,
        "font_names": sorted(fonts),
        "embedded_pretendard_weights": 3,
        "image_placements": image_placements,
        "unique_images": unique_images,
        "korean_characters": len(re.findall(r"[가-힣]", full_text)),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate the TraceDeck FE Korean user guide.")
    parser.add_argument("pdf", type=Path)
    parser.add_argument("--json-output", type=Path)
    args = parser.parse_args()
    try:
        report = validate(args.pdf)
    except Exception as error:
        print(json.dumps({"status": "FAIL", "error": str(error)}, ensure_ascii=True, indent=2))
        return 1
    payload = json.dumps(report, ensure_ascii=True, indent=2)
    print(payload)
    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
