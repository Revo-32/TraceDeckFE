from __future__ import annotations

import argparse
import html
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate,
    Flowable,
    Frame,
    Image,
    KeepTogether,
    ListFlowable,
    ListItem,
    NextPageTemplate,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
)
from reportlab.platypus.tableofcontents import TableOfContents


PAGE_WIDTH, PAGE_HEIGHT = A4
INK = colors.HexColor("#202020")
SECONDARY = colors.HexColor("#6B6B6B")
MUTED = colors.HexColor("#8A8A8A")
PAPER = colors.HexColor("#FAFAFA")
CARD = colors.HexColor("#F3F3F3")
BORDER = colors.HexColor("#DEDEDE")
DARK = colors.HexColor("#0D0D0D")
DARK_CARD = colors.HexColor("#202020")
ACCENT = colors.HexColor("#2B6CB0")
MAGENTA = colors.HexColor("#DC117C")


def esc(text: str) -> str:
    return html.escape(text).replace("\n", "<br/>")


class GuideDocTemplate(BaseDocTemplate):
    def __init__(self, filename: str, styles: dict[str, ParagraphStyle]):
        super().__init__(
            filename,
            pagesize=A4,
            leftMargin=22 * mm,
            rightMargin=22 * mm,
            topMargin=22 * mm,
            bottomMargin=20 * mm,
            title="TraceDeck FE v1.0.0 사용방법",
            author="",
            subject="TraceDeck FE User Guide",
            creator="TraceDeck FE Documentation",
        )
        self.styles = styles
        self.current_chapter = "사용자 가이드"
        cover_frame = Frame(0, 0, PAGE_WIDTH, PAGE_HEIGHT, leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0, id="cover")
        body_frame = Frame(
            self.leftMargin,
            self.bottomMargin,
            PAGE_WIDTH - self.leftMargin - self.rightMargin,
            PAGE_HEIGHT - self.topMargin - self.bottomMargin,
            leftPadding=0,
            rightPadding=0,
            topPadding=0,
            bottomPadding=0,
            id="body",
        )
        self.addPageTemplates(
            [
                PageTemplate(id="cover", frames=[cover_frame], onPage=self._cover_page),
                PageTemplate(id="body", frames=[body_frame], onPageEnd=self._body_page_end),
            ]
        )

    def _metadata(self, canvas):
        canvas.setTitle("TraceDeck FE v1.0.0 사용방법")
        canvas.setSubject("TraceDeck FE User Guide")
        canvas.setCreator("TraceDeck FE Documentation")
        canvas.setAuthor("")

    def _cover_page(self, canvas, _doc):
        self._metadata(canvas)
        canvas.saveState()
        canvas.setFillColor(DARK)
        canvas.rect(0, 0, PAGE_WIDTH, PAGE_HEIGHT, fill=1, stroke=0)
        canvas.restoreState()

    def _body_page_end(self, canvas, doc):
        self._metadata(canvas)
        canvas.saveState()
        canvas.setStrokeColor(colors.HexColor("#E5E5E5"))
        canvas.setLineWidth(0.5)
        canvas.line(doc.leftMargin, PAGE_HEIGHT - 14 * mm, PAGE_WIDTH - doc.rightMargin, PAGE_HEIGHT - 14 * mm)
        canvas.setFillColor(SECONDARY)
        canvas.setFont("Pretendard-Medium", 8)
        canvas.drawString(doc.leftMargin, PAGE_HEIGHT - 10.5 * mm, self.current_chapter)
        canvas.setFont("Pretendard-Regular", 8)
        canvas.drawString(doc.leftMargin, 10.5 * mm, "TraceDeck FE v1.0.0")
        canvas.drawRightString(PAGE_WIDTH - doc.rightMargin, 10.5 * mm, f"{doc.page - 1}")
        canvas.restoreState()

    def afterFlowable(self, flowable):
        if hasattr(flowable, "header_title"):
            self.current_chapter = flowable.header_title
        if not isinstance(flowable, Paragraph) or not hasattr(flowable, "bookmark_key"):
            return
        key = flowable.bookmark_key
        title = flowable.bookmark_title
        level = flowable.bookmark_level
        self.canv.bookmarkPage(key)
        self.canv.addOutlineEntry(title, key, level=level, closed=False)
        self.notify("TOCEntry", (level, title, self.page - 1, key))
        if level == 0:
            self.current_chapter = title


class ConceptFlow(Flowable):
    def __init__(self, labels: list[str], caption: str):
        super().__init__()
        self.labels = labels
        self.caption = caption
        self.width = 162 * mm
        self.height = 38 * mm

    def draw(self):
        c = self.canv
        box_w = 34 * mm
        gap = (self.width - box_w * len(self.labels)) / max(1, len(self.labels) - 1)
        y = 10 * mm
        for index, label in enumerate(self.labels):
            x = index * (box_w + gap)
            c.setFillColor(CARD)
            c.setStrokeColor(BORDER)
            c.roundRect(x, y, box_w, 16 * mm, 3 * mm, fill=1, stroke=1)
            c.setFillColor(INK)
            c.setFont("Pretendard-Medium", 8.5)
            lines = label.split("\n")
            for line_index, line in enumerate(lines):
                c.drawCentredString(x + box_w / 2, y + 10.5 * mm - line_index * 4 * mm, line)
            if index < len(self.labels) - 1:
                ax = x + box_w
                bx = x + box_w + gap
                mid = y + 8 * mm
                c.setStrokeColor(MUTED)
                c.line(ax + 2 * mm, mid, bx - 2 * mm, mid)
                c.line(bx - 4 * mm, mid + 1.5 * mm, bx - 2 * mm, mid)
                c.line(bx - 4 * mm, mid - 1.5 * mm, bx - 2 * mm, mid)
        c.setFillColor(SECONDARY)
        c.setFont("Pretendard-Regular", 7.5)
        c.drawString(0, 2 * mm, self.caption)


class ControllerMap(Flowable):
    def __init__(self):
        super().__init__()
        self.width = 162 * mm
        self.height = 95 * mm

    def draw(self):
        c = self.canv
        panel_w = 54 * mm
        panel_x = 5 * mm
        c.setFillColor(DARK)
        c.setStrokeColor(colors.HexColor("#353535"))
        c.roundRect(panel_x, 4 * mm, panel_w, 86 * mm, 4 * mm, fill=1, stroke=1)
        c.setFillColor(colors.white)
        c.setFont("Pretendard-SemiBold", 8)
        c.drawString(panel_x + 5 * mm, 83 * mm, "TRACEDECK FE")
        cards = ["프로젝트", "오버레이", "변형", "위치", "이미지 보조", "가이드", "색상", "팔레트", "초기화 / 고급"]
        y = 75 * mm
        for index, label in enumerate(cards, start=1):
            c.setFillColor(DARK_CARD)
            c.setStrokeColor(colors.HexColor("#3A3A3A"))
            c.roundRect(panel_x + 4 * mm, y, 46 * mm, 6.5 * mm, 1.5 * mm, fill=1, stroke=1)
            c.setFillColor(colors.HexColor("#F2F2F2"))
            c.setFont("Pretendard-Medium", 6.5)
            c.drawString(panel_x + 7 * mm, y + 2.2 * mm, label)
            c.setFillColor(MAGENTA if index in (2, 7) else colors.HexColor("#777777"))
            c.circle(panel_x + 46.5 * mm, y + 3.3 * mm, 1.7 * mm, fill=1, stroke=0)
            c.setFillColor(colors.white)
            c.setFont("Pretendard-SemiBold", 5.5)
            c.drawCentredString(panel_x + 46.5 * mm, y + 1.6 * mm, str(index))
            y -= 7.6 * mm
        notes = [
            (1, "프로젝트 파일과 실행 취소"),
            (2, "표시, 잠금, 불투명도"),
            (3, "크기, 회전, 반전"),
            (4, "정밀 이동과 중앙 정렬"),
            (5, "흑백과 대비"),
            (6, "격자와 중앙선"),
            (7, "원본 픽셀 색상 추출"),
            (8, "수동 / 자동 팔레트"),
            (9, "개별 또는 전체 초기화"),
        ]
        c.setFont("Pretendard-Regular", 8)
        x = 72 * mm
        y = 82 * mm
        for index, note in notes:
            c.setFillColor(MAGENTA if index in (2, 7) else colors.HexColor("#555555"))
            c.circle(x, y + 0.8 * mm, 2.3 * mm, fill=1, stroke=0)
            c.setFillColor(colors.white)
            c.setFont("Pretendard-SemiBold", 6.2)
            c.drawCentredString(x, y - 1 * mm, str(index))
            c.setFillColor(INK)
            c.setFont("Pretendard-Regular", 8)
            c.drawString(x + 5 * mm, y - 1 * mm, note)
            y -= 8.5 * mm
        c.setFillColor(SECONDARY)
        c.setFont("Pretendard-Regular", 7.5)
        c.drawString(5 * mm, 0, "구성 안내도 - 실제 화면 캡처가 아닌 카드 위치 설명용 도식입니다.")


class LockCompare(Flowable):
    def __init__(self):
        super().__init__()
        self.width = 162 * mm
        self.height = 48 * mm

    def draw_panel(self, x, title, status, body, active):
        c = self.canv
        w = 76 * mm
        c.setFillColor(colors.white)
        c.setStrokeColor(BORDER)
        c.roundRect(x, 7 * mm, w, 35 * mm, 3 * mm, fill=1, stroke=1)
        c.setFillColor(ACCENT if active else colors.HexColor("#555555"))
        c.circle(x + 7 * mm, 34 * mm, 2.5 * mm, fill=1, stroke=0)
        c.setFillColor(INK)
        c.setFont("Pretendard-SemiBold", 10)
        c.drawString(x + 13 * mm, 31.5 * mm, title)
        c.setFillColor(SECONDARY)
        c.setFont("Pretendard-Medium", 8)
        c.drawString(x + 7 * mm, 24 * mm, status)
        c.setFont("Pretendard-Regular", 8)
        for i, line in enumerate(body):
            c.drawString(x + 7 * mm, 17 * mm - i * 5 * mm, line)

    def draw(self):
        self.draw_panel(1 * mm, "잠금 해제 (Lock OFF)", "레퍼런스 조정 단계", ["드래그로 위치 이동", "휠로 커서 중심 확대 / 축소"], False)
        self.draw_panel(85 * mm, "잠금 (Lock ON)", "Forza 작업 단계", ["마우스 입력이 Forza로 통과", "오버레이는 보이지만 조작을 막지 않음"], True)


class ColorSample(Flowable):
    def __init__(self):
        super().__init__()
        self.width = 162 * mm
        self.height = 42 * mm

    def draw(self):
        c = self.canv
        c.setFillColor(MAGENTA)
        c.setStrokeColor(BORDER)
        c.roundRect(4 * mm, 8 * mm, 32 * mm, 27 * mm, 3 * mm, fill=1, stroke=1)
        c.setFillColor(INK)
        c.setFont("Pretendard-SemiBold", 12)
        c.drawString(45 * mm, 29 * mm, "#DC117C")
        c.setFont("Pretendard-Medium", 9)
        c.drawString(45 * mm, 21 * mm, "RGB   220 / 17 / 124")
        c.drawString(45 * mm, 14 * mm, "FORZA HSB   0.912 / 0.923 / 0.863")
        c.setFillColor(SECONDARY)
        c.setFont("Pretendard-Regular", 7.5)
        c.drawString(4 * mm, 1 * mm, "Final RC 실제 검증에 사용한 원본 픽셀 예시입니다.")


class PortableTree(Flowable):
    def __init__(self):
        super().__init__()
        self.width = 162 * mm
        self.height = 60 * mm

    def draw(self):
        c = self.canv
        c.setFont("Pretendard-SemiBold", 9)
        c.setFillColor(INK)
        c.drawString(4 * mm, 51 * mm, "압축 해제 직후")
        c.setFont("Pretendard-Regular", 8.5)
        clean = ["TraceDeckFE.exe", "TraceDeck FE 사용방법.pdf", "licenses/"]
        for i, item in enumerate(clean):
            c.drawString(9 * mm, (43 - i * 7) * mm, ("└ " if i == len(clean) - 1 else "├ ") + item)
        c.setStrokeColor(BORDER)
        c.line(78 * mm, 6 * mm, 78 * mm, 53 * mm)
        c.setFont("Pretendard-SemiBold", 9)
        c.drawString(88 * mm, 51 * mm, "사용 후 생성될 수 있음")
        c.setFont("Pretendard-Regular", 8.5)
        used = ["data/settings.json", "data/logs/", "data/recovery/<Project-ID>/"]
        for i, item in enumerate(used):
            c.drawString(93 * mm, (43 - i * 7) * mm, ("└ " if i == len(used) - 1 else "├ ") + item)
        c.setFillColor(SECONDARY)
        c.setFont("Pretendard-Regular", 7.5)
        c.drawString(4 * mm, 2 * mm, "설정과 복구 데이터도 프로그램 폴더 아래에 남으므로 폴더 전체를 함께 옮길 수 있습니다.")


def build_styles() -> dict[str, ParagraphStyle]:
    sample = getSampleStyleSheet()
    return {
        "cover_title": ParagraphStyle("cover_title", fontName="Pretendard-SemiBold", fontSize=28, leading=34, textColor=colors.white, alignment=TA_CENTER, spaceAfter=8),
        "cover_version": ParagraphStyle("cover_version", fontName="Pretendard-Medium", fontSize=13, leading=18, textColor=colors.HexColor("#D5D5D5"), alignment=TA_CENTER),
        "cover_sub": ParagraphStyle("cover_sub", fontName="Pretendard-Regular", fontSize=9, leading=14, textColor=colors.HexColor("#9A9A9A"), alignment=TA_CENTER),
        "toc_title": ParagraphStyle("toc_title", fontName="Pretendard-SemiBold", fontSize=27, leading=33, textColor=INK, spaceAfter=10 * mm),
        "chapter": ParagraphStyle("chapter", fontName="Pretendard-SemiBold", fontSize=25, leading=32, textColor=INK, spaceBefore=0, spaceAfter=8 * mm),
        "section": ParagraphStyle("section", fontName="Pretendard-SemiBold", fontSize=15, leading=21, textColor=INK, spaceBefore=6 * mm, spaceAfter=3 * mm, keepWithNext=True),
        "subheading": ParagraphStyle("subheading", fontName="Pretendard-Medium", fontSize=11, leading=16, textColor=INK, spaceBefore=3 * mm, spaceAfter=1.5 * mm, keepWithNext=True),
        "body": ParagraphStyle("body", fontName="Pretendard-Regular", fontSize=10, leading=15.5, textColor=INK, spaceAfter=3.2 * mm, wordWrap="CJK"),
        "body_medium": ParagraphStyle("body_medium", fontName="Pretendard-Medium", fontSize=10, leading=15.5, textColor=INK, spaceAfter=3.2 * mm, wordWrap="CJK"),
        "caption": ParagraphStyle("caption", fontName="Pretendard-Regular", fontSize=8, leading=12, textColor=SECONDARY, spaceAfter=3 * mm, wordWrap="CJK"),
        "small": ParagraphStyle("small", fontName="Pretendard-Regular", fontSize=8.5, leading=13, textColor=INK, wordWrap="CJK"),
        "small_medium": ParagraphStyle("small_medium", fontName="Pretendard-Medium", fontSize=8.5, leading=13, textColor=INK, wordWrap="CJK"),
        "table_header": ParagraphStyle("table_header", fontName="Pretendard-SemiBold", fontSize=8.5, leading=12, textColor=INK, wordWrap="CJK"),
        "table_cell": ParagraphStyle("table_cell", fontName="Pretendard-Regular", fontSize=8.3, leading=12, textColor=INK, wordWrap="CJK"),
        "callout_label": ParagraphStyle("callout_label", fontName="Pretendard-SemiBold", fontSize=8, leading=11, textColor=ACCENT, alignment=TA_CENTER),
        "callout_body": ParagraphStyle("callout_body", fontName="Pretendard-Regular", fontSize=9, leading=14, textColor=INK, wordWrap="CJK"),
        "toc_l0": ParagraphStyle("toc_l0", fontName="Pretendard-Medium", fontSize=10, leading=18, textColor=INK, leftIndent=0, firstLineIndent=0, spaceBefore=1),
        "toc_l1": ParagraphStyle("toc_l1", fontName="Pretendard-Regular", fontSize=8, leading=13, textColor=SECONDARY, leftIndent=10 * mm, firstLineIndent=0),
    }


def p(text: str, styles, style="body") -> Paragraph:
    return Paragraph(esc(text), styles[style])


def rich(text: str, styles, style="body") -> Paragraph:
    return Paragraph(text, styles[style])


def chapter(title: str, number: int, styles) -> Paragraph:
    paragraph = Paragraph(f"<font color='#777777'>{number:02d}</font><br/>{esc(title)}", styles["chapter"])
    paragraph.bookmark_key = f"ch{number}"
    paragraph.bookmark_title = f"{number}. {title}"
    paragraph.bookmark_level = 0
    return paragraph


def section(number: str, title: str, styles) -> Paragraph:
    paragraph = Paragraph(f"{esc(number)}  {esc(title)}", styles["section"])
    paragraph.bookmark_key = "s" + number.replace(".", "_")
    paragraph.bookmark_title = f"{number} {title}"
    paragraph.bookmark_level = 1
    return paragraph


def bullets(items: list[str], styles) -> ListFlowable:
    return ListFlowable(
        [ListItem(p(item, styles, "body"), leftIndent=3 * mm) for item in items],
        bulletType="bullet",
        bulletFontName="Pretendard-Medium",
        bulletFontSize=6,
        bulletColor=colors.HexColor("#555555"),
        leftIndent=7 * mm,
        bulletIndent=1 * mm,
        spaceAfter=3 * mm,
    )


def table(rows: list[list[str]], widths: list[float], styles, header=True, compact=False) -> Table:
    converted = []
    for row_index, row in enumerate(rows):
        converted.append([p(cell, styles, "table_header" if header and row_index == 0 else "table_cell") for cell in row])
    result = Table(converted, colWidths=widths, repeatRows=1 if header else 0, hAlign="LEFT")
    vertical_padding = 1.2 * mm if compact else 2.3 * mm
    commands = [
        ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#EEEEEE")) if header else ("BACKGROUND", (0, 0), (-1, -1), colors.white),
        ("TEXTCOLOR", (0, 0), (-1, -1), INK),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 3 * mm),
        ("RIGHTPADDING", (0, 0), (-1, -1), 3 * mm),
        ("TOPPADDING", (0, 0), (-1, -1), vertical_padding),
        ("BOTTOMPADDING", (0, 0), (-1, -1), vertical_padding),
        ("LINEBELOW", (0, 0), (-1, -1), 0.4, BORDER),
    ]
    result.setStyle(TableStyle(commands))
    result.spaceAfter = 4 * mm
    return result


def callout(label: str, text: str, styles) -> Table:
    result = Table(
        [[Paragraph(esc(label), styles["callout_label"]), p(text, styles, "callout_body")]],
        colWidths=[22 * mm, 136 * mm],
        hAlign="LEFT",
    )
    result.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#F2F5F8")),
                ("BOX", (0, 0), (-1, -1), 0.5, colors.HexColor("#D8E0E8")),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("LEFTPADDING", (0, 0), (-1, -1), 3 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 3 * mm),
                ("TOPPADDING", (0, 0), (-1, -1), 3 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 3 * mm),
            ]
        )
    )
    result.spaceAfter = 4 * mm
    return result


def page_break(story):
    story.append(PageBreak())


def build_story(repo: Path, styles):
    story = []
    logo = repo / "src" / "TraceDeckFE" / "Assets" / "TraceDeck_FE_Mini_logo.png"

    story += [
        Spacer(1, 72 * mm),
        Image(str(logo), width=82 * mm, height=82 * mm, hAlign="CENTER"),
        Spacer(1, 10 * mm),
        Paragraph("TRACEDECK FE", styles["cover_title"]),
        Paragraph("v1.0.0<br/>사용자 가이드", styles["cover_version"]),
        Spacer(1, 9 * mm),
        Paragraph("Forza Horizon 6<br/>Vinyl / Decal Tracing &amp; Color Assistant", styles["cover_sub"]),
        Spacer(1, 27 * mm),
        Paragraph("Unofficial Community Tool", styles["cover_sub"]),
        NextPageTemplate("body"),
        PageBreak(),
        p("원하는 장을 선택하면 해당 페이지로 이동합니다. PDF 뷰어의 책갈피 사이드바에서도 16개 장과 세부 항목을 바로 열 수 있습니다.", styles),
    ]
    toc_title = Paragraph("목차", styles["toc_title"])
    toc_title.header_title = "목차"
    story.insert(-1, toc_title)
    toc = TableOfContents()
    toc.levelStyles = [styles["toc_l0"], styles["toc_l1"]]
    story += [toc, PageBreak()]

    # 1
    story += [chapter("시작하기", 1, styles)]
    story += [section("1.1", "TraceDeck FE란?", styles)]
    story += [p("TraceDeck FE는 Forza Horizon 6의 비닐과 데칼을 직접 만들 때 참고 이미지를 게임 창 위에 겹쳐 보여 주는 포터블 작업 보조 도구입니다. 사용자는 반투명 레퍼런스를 보면서 Forza 편집기에서 도형을 직접 배치합니다.", styles)]
    story += [ConceptFlow(["레퍼런스\n이미지", "TraceDeck FE\n오버레이", "Forza\n비닐 편집기", "사용자의\n수동 트레이싱"], "작업 개념도 - TraceDeck FE는 자동 제작기가 아니라 수동 작업을 돕는 레퍼런스 도구입니다."), Spacer(1, 4 * mm)]
    story += [section("1.2", "하는 일과 하지 않는 일", styles)]
    story += [bullets(["이미지의 위치, 크기, 회전, 반전과 불투명도를 조절해 Forza 위에 표시합니다.", "원본 이미지에서 색상을 한 번씩 선택하고 HEX, RGB, Forza HSB로 보여 줍니다.", "팔레트와 .TDFE 프로젝트, 실행 취소, 복구 기능으로 작업을 정리합니다."], styles)]
    story += [callout("IMPORTANT", "게임 화면을 캡처하거나 게임 메모리를 수정하지 않습니다. 도형을 자동 생성하거나 Forza에 주입하지 않으며, 최종 비닐은 사용자가 Forza 안에서 직접 만듭니다.", styles)]
    story += [section("1.3", "시스템 요구사항과 권장 실행 환경", styles)]
    story += [table([["항목", "요구 / 권장"], ["운영체제", "Windows 10 또는 Windows 11, x64"], ["설치", "필요 없음. ZIP 전체를 쓰기 가능한 폴더에 압축 해제"], ["Forza 화면 모드", "창 모드 또는 테두리 없는 창 모드 권장"], ["전체 화면", "독점 전체 화면에서는 일반 데스크톱 오버레이가 보이지 않을 수 있음"]], [43 * mm, 115 * mm], styles)]
    story += [section("1.4", "5분 빠른 시작", styles)]
    quick = [["01", "압축 해제", "ZIP 전체를 한 폴더에 풉니다."], ["02", "실행", "TraceDeckFE.exe를 실행합니다."], ["03", "연결", "Forza를 실행하고 자동 연결을 기다리거나 창 선택을 사용합니다."], ["04", "이미지", "레퍼런스 열기, 드래그 앤 드롭 또는 Ctrl + V로 이미지를 넣습니다."], ["05", "배치", "잠금을 끄고 드래그 / 휠로 위치와 크기를 맞춥니다."], ["06", "작업", "불투명도를 조절한 뒤 잠금을 켜고 Forza에서 도형을 배치합니다."], ["07", "색상", "필요하면 색상 추출 후 HSB 값을 Forza에 입력합니다."], ["08", "저장", "Ctrl + S로 .TDFE 프로젝트를 저장합니다."]]
    story += [table([["단계", "무엇을", "어떻게"]] + quick, [16 * mm, 32 * mm, 110 * mm], styles)]
    story += [section("1.5", "첫 실행과 Forza Horizon 6 연결", styles)]
    story += [p("Forza Horizon 6가 이미 실행 중이면 TraceDeck FE가 자동으로 찾습니다. 찾지 못하면 상단의 창 선택을 눌러 목록에서 Forza를 고른 뒤 연결합니다. 다시 연결은 대상 창을 새로 찾을 때 사용합니다.", styles), callout("NOTE", "Forza를 종료해도 TraceDeck FE와 현재 프로젝트는 유지됩니다. Forza를 다시 실행한 뒤 다시 연결할 수 있습니다.", styles)]

    # 2
    page_break(story)
    story += [chapter("화면 구성 알아보기", 2, styles), p("메인 컨트롤러는 접을 수 있는 기능 카드로 구성됩니다. 이 장에서는 위치만 빠르게 익히고, 세부 사용법은 뒤의 장에서 확인합니다.", styles), ControllerMap()]
    overview_rows = [["카드", "주요 역할"], ["프로젝트", "새 프로젝트, 열기, 저장, 실행 취소 / 다시 실행, 레퍼런스 열기"], ["오버레이", "표시, 잠금 / 클릭 통과, 불투명도"], ["변형", "배율, 회전, 좌우 / 상하 반전"], ["위치", "방향 이동과 중앙 정렬"], ["이미지 보조", "흑백 표시와 대비"], ["가이드", "격자, 간격, 중앙선, 가이드 불투명도"], ["색상", "색상 추출, 확대경, HEX / RGB / HSB, 팔레트 추가"], ["팔레트", "색상 이름, 선택, 삭제, 순서, 자동 생성"], ["초기화 / 고급", "위치, 변형, 효과, 가이드, 전체 상태 초기화와 화면 맞추기"]]
    story += [table(overview_rows, [38 * mm, 120 * mm], styles, compact=True)]
    story += [section("2.10", "설정", styles), p("좌상단 로고 옆 상단의 톱니바퀴 버튼에서 언어, 화면 구성, 레퍼런스 조작, 색상 표시 정밀도, 단축키, 자동 저장과 복구를 설정합니다. 언어 변경은 다음 실행부터 적용됩니다.", styles)]

    # 3
    page_break(story)
    story += [chapter("레퍼런스 이미지로 작업하기", 3, styles)]
    story += [section("3.1", "이미지 불러오기", styles), bullets(["레퍼런스 열기: 파일 선택 창에서 지원 이미지를 엽니다.", "드래그 앤 드롭: 지원 이미지 파일을 컨트롤러 위에 놓습니다.", "Ctrl + V: 클립보드 이미지 또는 클립보드에 복사된 지원 이미지 파일을 붙여 넣습니다."], styles), callout("NOTE", "이미 레퍼런스가 있을 때 교체 확인이 켜져 있으면 확인 창이 나타납니다. 교체하면 새 원본에 맞춰 변형과 효과가 초기화될 수 있습니다.", styles)]
    story += [section("3.2", "표시와 불투명도", styles), p("표시는 레퍼런스 이미지 자체를 켜거나 끕니다. 불투명도는 게임 위에서 레퍼런스가 얼마나 진하게 보일지 조절합니다. 컨트롤러 창의 투명도에는 영향을 주지 않습니다.", styles)]
    story += [section("3.3", "오버레이 잠금", styles), LockCompare(), Spacer(1, 3 * mm), p("배치할 때는 잠금 해제, Forza에서 도형을 조작할 때는 잠금을 켜는 흐름이 가장 단순합니다. 잠금 상태에서도 표시와 불투명도는 그대로 유지됩니다.", styles)]
    story += [section("3.4", "드래그와 휠 확대 / 축소", styles), p("잠금을 해제한 뒤 레퍼런스를 드래그하면 위치가 바뀝니다. 마우스 휠은 기본적으로 커서 아래의 원본 지점을 유지하면서 확대하거나 축소합니다. 휠 변화량과 커서 중심 동작은 설정에서 바꿀 수 있습니다.", styles)]
    story += [section("3.5", "화면에 맞추기와 100%", styles), table([["기능", "동작"], ["화면에 맞추기 (Fit Reference)", "현재 대상 창 안에 레퍼런스를 맞추고 중앙에 배치"], ["실제 크기 100%", "원본 픽셀 크기를 기준으로 표시"], ["중앙", "현재 크기는 유지하고 대상 창 중앙으로 이동"]], [55 * mm, 103 * mm], styles)]
    story += [section("3.6", "정밀 위치", styles), p("위치 카드의 방향 버튼이나 방향키로 조금씩 이동합니다. 기본값은 방향키 1 px, Shift + 방향키 10 px이며 설정에서 각각 1~100 px 범위로 바꿀 수 있습니다.", styles)]
    story += [section("3.7", "배율, 회전과 반전", styles), p("변형 카드에서 배율을 조절하고 이미지 중심을 기준으로 5도씩 회전할 수 있습니다. 좌우 반전과 상하 반전은 원본 파일을 바꾸지 않는 표시 변형입니다. 변형 초기화는 위치는 유지하고 배율, 회전, 반전을 초기 상태로 되돌립니다.", styles)]
    story += [section("3.8", "창 크기를 바꿀 때", styles), p("Forza 창의 크기가 달라지면 레퍼런스는 대상 화면에 대한 상대 위치와 상대 크기를 유지합니다. 큰 창에서 작은 창으로 바꾼 뒤 다시 돌아와도 같은 작업 구도를 최대한 보존하며, 이 자동 보정은 실행 취소 이력에 들어가지 않습니다.", styles), callout("TIP", "창을 옮기기만 하고 크기가 같다면 레퍼런스 변형은 바뀌지 않고 오버레이만 대상 창을 따라갑니다.", styles)]
    story += [section("3.9", "초기화 선택", styles), table([["버튼", "유지 / 초기화"], ["위치 초기화", "중앙 위치로 이동"], ["변형 초기화", "위치는 유지, 배율 / 회전 / 반전 초기화"], ["이미지 효과 초기화", "원본 유지, 흑백 / 대비 초기화"], ["가이드 초기화", "격자 / 중앙선과 가이드 값 초기화"], ["전체 초기화", "원본 이미지와 팔레트는 유지하고 나머지 작업 상태 초기화"]], [45 * mm, 113 * mm], styles)]

    # 4
    page_break(story)
    story += [chapter("이미지를 더 쉽게 따라 그리기", 4, styles)]
    story += [section("4.1", "흑백", styles), p("흑백은 색상 정보에 방해받지 않고 외곽선과 밝기 차이를 볼 때 유용합니다. 표시만 바꾸며 원본 레퍼런스의 색상과 파일 데이터는 그대로 유지합니다.", styles)]
    story += [section("4.2", "대비", styles), p("대비는 -100부터 100까지 조절합니다. 흑백을 켠 경우 흑백 변환 뒤에 대비가 적용됩니다. 경계가 약한 이미지의 형태를 구분하는 데 도움이 되지만 원본을 수정하지 않습니다.", styles)]
    story += [section("4.3", "격자", styles), p("격자는 레퍼런스가 아니라 Forza 대상 화면을 기준으로 표시됩니다. 간격은 16~400 px 범위에서 조절하며, 큰 덩어리의 비율과 반복 배치를 확인할 때 유용합니다.", styles)]
    story += [section("4.4", "중앙선과 가이드 불투명도", styles), p("가로 중앙선과 세로 중앙선을 각각 켤 수 있습니다. 로고나 문양을 정확히 중심에 맞출 때 사용하세요. 가이드 불투명도는 이미지 불투명도와 별개입니다.", styles)]
    story += [section("4.5", "상황별 추천", styles), table([["상황", "추천"], ["실루엣부터 만들기", "흑백 ON, 대비를 조금 높여 큰 외곽선 확인"], ["대칭 로고", "가로 / 세로 중앙선으로 중심 확인"], ["반복 패턴", "격자 간격을 도형 크기에 맞춤"], ["색상 추출", "효과 상태와 무관하게 그대로 추출 가능"]], [55 * mm, 103 * mm], styles), callout("IMPORTANT", "흑백과 대비는 화면 표시 효과입니다. 색상 추출은 언제나 원본 레퍼런스를 읽으므로 결과에 영향을 주지 않습니다.", styles)]

    # 5
    page_break(story)
    story += [chapter("색상 정확하게 가져오기", 5, styles)]
    story += [section("5.1", "색상 추출 (Pick Color)", styles), p("색상 카드에서 색상 추출을 누르면 한 번의 선택 작업이 시작됩니다. Forza 위에 보이는 레퍼런스의 원하는 지점을 클릭하세요. Esc를 누르면 현재 선택을 취소하고 이전 색상은 유지합니다.", styles)]
    story += [section("5.2", "확대경", styles), p("확대경을 켜면 색상을 선택하는 동안 원본 픽셀 주변을 확대해서 보여 줍니다. 가장자리나 작은 색상 영역을 정확히 고를 때 유용하며, 선택 작업이 끝나면 계속 렌더링하지 않습니다.", styles)]
    story += [section("5.3", "원본 레퍼런스 샘플링", styles), p("TraceDeck FE는 화면 캡처 색상이 아니라 보관된 원본 레퍼런스의 픽셀을 읽습니다. 위치, 배율, 회전과 반전을 역으로 계산해 사용자가 클릭한 지점을 원본 좌표로 찾습니다.", styles), ConceptFlow(["화면에서\n클릭", "변형을\n역계산", "원본 픽셀\n좌표", "HEX / RGB\nHSB"], "색상 추출 원리 도식 - 게임 화면이나 표시 효과를 캡처하지 않습니다."), Spacer(1, 3 * mm)]
    story += [callout("IMPORTANT", "오버레이 불투명도, 흑백, 대비와 Forza 배경이 섞인 화면색을 읽지 않습니다. 따라서 눈에 보이는 합성색과 값이 다르게 느껴질 수 있으며 이는 정상입니다.", styles)]
    story += [section("5.4", "HEX와 RGB", styles), p("HEX는 웹과 그래픽 도구에서 흔히 쓰는 16진수 색상 표기이며, RGB는 빨강, 초록, 파랑의 0~255 값을 보여 줍니다. 투명 픽셀도 유효한 결과이며 알파 정보가 함께 표시됩니다.", styles)]
    story += [section("5.5", "Forza HSB 이해", styles), table([["값", "뜻", "범위"], ["H - Hue", "색상 계열", "0.000~1.000"], ["S - Saturation", "채도", "0.000~1.000"], ["B - Brightness", "밝기", "0.000~1.000"]], [30 * mm, 70 * mm, 58 * mm], styles), ColorSample(), Spacer(1, 3 * mm)]
    story += [section("5.6", "값 복사", styles), p("H, S, B 옆 복사 버튼으로 한 값만 복사하거나 HSB 복사로 세 값을 함께 복사할 수 있습니다. 설정에서 표시 / 복사 자릿수를 2자리 또는 3자리로 선택해도 저장된 원본 색상 정밀도는 바뀌지 않습니다.", styles)]

    # 6
    page_break(story)
    story += [chapter("팔레트 활용하기", 6, styles)]
    story += [section("6.1", "현재 색상 추가", styles), p("색상을 추출한 뒤 + 팔레트를 누르면 현재 색상이 목록에 추가됩니다. 같은 색을 여러 번 추가할 수 있으므로 용도별 이름을 붙여도 됩니다.", styles)]
    story += [section("6.2", "이름, 선택, 삭제와 순서", styles), bullets(["이름: 색상 이름 입력란을 편집한 뒤 포커스를 이동하면 반영됩니다.", "선택: 팔레트 항목을 클릭하면 그 색상이 현재 색상으로 다시 불러와집니다.", "삭제: 항목 오른쪽의 × 버튼을 누릅니다. 현재 세션에서는 실행 취소로 복원할 수 있습니다.", "순서: 이름 입력란이나 삭제 버튼이 아닌 항목 영역을 드래그해 재배치합니다."], styles)]
    story += [section("6.3", "자동 팔레트", styles), p("자동 색상 수를 2~12개로 정한 뒤 팔레트 생성을 누르면 원본 레퍼런스에서 대표 색상을 찾습니다. 생성된 색상은 기존 수동 색상을 지우지 않고 뒤에 추가됩니다. 완전히 투명한 픽셀은 자동 팔레트 대상에서 제외됩니다.", styles)]
    story += [section("6.4", "실제 구성 팁", styles), table([["이름 예", "용도"], ["Main Red", "가장 넓은 주 색상"], ["Outline", "외곽선"], ["White", "하이라이트와 밝은 면"], ["Shadow", "그림자와 어두운 면"]], [45 * mm, 113 * mm], styles), callout("TIP", "색상 이름은 화면에서 보이는 위치보다 역할을 기준으로 붙이면 긴 작업에서 다시 찾기 쉽습니다.", styles)]

    # 7
    page_break(story)
    story += [chapter("처음부터 끝까지 - 실제 작업 예제", 7, styles), p("아래 흐름은 간단한 로고를 따라 만드는 예입니다. 특정 불투명도나 도형 수는 정답이 아니라 작업을 시작하기 위한 권장 예입니다.", styles)]
    workflow = [["단계", "작업", "확인할 점"], ["01", "레퍼런스 불러오기", "가능하면 투명 배경 PNG 또는 선명한 원본 사용"], ["02", "위치 / 크기 맞추기", "잠금 OFF, 드래그와 휠 사용"], ["03", "불투명도 조절", "약 40~60%에서 Forza 도형과 원본을 함께 보기"], ["04", "잠금 ON", "마우스 입력을 Forza로 통과"], ["05", "큰 도형 배치", "외곽 형태와 전체 비율부터 맞추기"], ["06", "미세 조정", "필요하면 잠금 OFF 후 방향키로 조정"], ["07", "색상 추출", "원본 픽셀 선택, HSB를 Forza에 입력"], ["08", "팔레트 정리", "Main, Outline, Shadow처럼 이름 붙이기"], ["09", "세부 도형", "격자 / 중앙선을 필요할 때만 켜기"], ["10", ".TDFE 저장", "중요한 시점마다 Ctrl + S"]]
    story += [table(workflow, [16 * mm, 42 * mm, 100 * mm], styles)]
    story += [section("7.1", "큰 형태부터 시작", styles), p("처음부터 작은 디테일에 들어가면 전체 비율을 다시 맞추기 어렵습니다. 레퍼런스를 중앙에 맞추고 가장 큰 실루엣과 기준선을 먼저 만든 뒤 안쪽 형태를 추가하세요.", styles)]
    story += [section("7.2", "잠금 전환을 작업 리듬으로 사용", styles), p("레퍼런스가 어긋났다고 느껴질 때만 잠금을 끄고 조정한 뒤 곧바로 다시 켭니다. 이 단순한 전환을 반복하면 Forza 조작을 오버레이가 가로막는 일을 줄일 수 있습니다.", styles), LockCompare(), Spacer(1, 3 * mm)]
    story += [section("7.3", "색상과 팔레트", styles), p("주 색상을 먼저 추출하고 Forza HSB를 입력합니다. 이후 외곽선과 그림자 색을 추가해 팔레트에 이름을 붙이면 여러 작업 세션에서도 같은 색을 빠르게 찾을 수 있습니다.", styles)]
    story += [section("7.4", "중간 저장", styles), p("큰 외곽선 완료, 색상 완료, 세부 작업 완료처럼 의미 있는 지점마다 Ctrl + S로 저장하세요. 자동 저장은 복구용이며 정식 프로젝트 파일을 대신하지 않습니다.", styles)]

    # 8
    page_break(story)
    story += [chapter("프로젝트 저장과 관리", 8, styles)]
    story += [section("8.1", ".TDFE란?", styles), p(".TDFE는 TraceDeck FE v1 형식의 자체 포함 프로젝트입니다. 현재 편집 상태와 팔레트, 가이드, 표시 상태와 함께 원본 레퍼런스 바이트를 프로젝트 안에 저장합니다.", styles)]
    story += [section("8.2", "저장되는 정보", styles), table([["저장됨", "저장되지 않음"], ["원본 레퍼런스, 위치 / 크기 / 회전 / 반전, 흑백 / 대비, 표시 / 잠금 / 불투명도, 가이드, 현재 색상, 팔레트", "실행 취소 / 다시 실행 이력, 현재 연결된 창, HWND / 프로세스 ID, 임시 렌더 캐시, 외부 원본 경로"]], [79 * mm, 79 * mm], styles)]
    story += [section("8.3", "원본 이미지 포함", styles), p("프로젝트를 정상 저장했다면 원래 이미지 파일이 이동되거나 삭제되어도 .TDFE 안의 원본을 이용해 다시 열 수 있습니다. 그래도 원본 파일 자체가 다른 용도에 필요하다면 별도 보관하는 것이 좋습니다.", styles)]
    story += [section("8.4", "새 프로젝트와 열기", styles), p("새 프로젝트는 저장하지 않은 변경 내용을 먼저 확인한 뒤 빈 작업을 시작합니다. 프로젝트 열기는 .TDFE 전체 구조와 원본 무결성을 검증한 후에만 현재 작업을 교체하므로, 손상된 파일을 열지 못해도 기존 작업은 유지됩니다.", styles)]
    story += [section("8.5", "저장과 다른 이름 저장", styles), table([["기능", "사용 시점"], ["저장 (Save)", "현재 프로젝트 경로에 안전하게 갱신. 처음 저장이면 경로 선택"], ["다른 이름 저장 (Save As)", "새 파일 경로에 같은 프로젝트의 복사본 저장"], ["다른 PC로 이동", ".TDFE 파일을 복사한 뒤 TraceDeck FE에서 프로젝트 열기"]], [50 * mm, 108 * mm], styles), callout("NOTE", "수동 저장은 같은 폴더의 임시 파일을 완성하고 다시 읽어 검증한 뒤 기존 파일을 교체합니다. 저장 중 새 편집이 생기면 그 변경은 계속 저장되지 않은 상태로 남습니다.", styles)]

    # 9
    page_break(story)
    story += [chapter("작업을 되돌리고 보호하기", 9, styles)]
    story += [section("9.1", "실행 취소와 다시 실행", styles), p("현재 세션에서 최근 100개의 논리적 편집을 실행 취소하거나 다시 실행할 수 있습니다. 새 프로젝트, 프로젝트 열기 또는 복구를 적용하면 이전 이력은 비워집니다.", styles)]
    story += [section("9.2", "동작 묶음", styles), p("한 번의 드래그나 슬라이더 조절은 하나의 편집으로 묶입니다. 휠 확대와 방향키 반복은 짧은 연속 입력을 한 작업으로 묶으므로 여러 번 눌렀다고 이력이 지나치게 잘게 나뉘지 않습니다.", styles)]
    story += [section("9.3", "수동 저장, 자동 저장과 복구", styles), table([["구분", "무엇을 남기나", "언제 사용하나"], ["수동 저장", "정식 .TDFE 프로젝트", "사용자가 저장 / Ctrl + S"], ["자동 저장", "data/recovery 아래 복구 스냅샷", "변경된 작업을 설정 간격으로 확인"], ["복구", "비정상 종료 전 상태를 저장되지 않은 작업으로 복원", "다음 실행 시 복구 선택 화면"]], [30 * mm, 73 * mm, 55 * mm], styles)]
    story += [callout("IMPORTANT", "자동 저장은 .TDFE를 덮어쓰지 않습니다. 중요한 작업은 자동 저장에만 맡기지 말고 Ctrl + S로 정식 저장하세요.", styles)]
    story += [section("9.4", "자동 저장 동작", styles), p("기본 간격은 5분이며 10초, 30초, 1분, 5분, 10분 중 선택할 수 있습니다. 변경이 없거나 편집 동작 중이거나 프로젝트 작업이 진행 중이면 불필요한 파일을 쓰지 않습니다. 프로젝트별 최근 3개 스냅샷을 유지하고 변하지 않은 원본 데이터는 공유합니다.", styles)]
    story += [section("9.5", "비정상 종료 후 복구", styles), p("복구 가능한 작업이 있으면 이전 세션 프로젝트를 열기 전에 선택 화면이 나타납니다. 작업 복구는 그 상태를 저장되지 않은 작업으로 열며 실행 취소 이력은 비어 있습니다. 마지막 저장 열기는 정상 저장된 .TDFE를 열고 이전 복구 항목을 정리합니다.", styles)]
    story += [bullets(["작업 복구: 복구 상태를 연 뒤 반드시 저장하여 정식 프로젝트를 갱신합니다.", "마지막 저장 열기: 마지막 수동 저장본으로 돌아갑니다.", "버리기: 복구 항목을 무시하며 수동 저장 프로젝트는 삭제하지 않습니다."], styles)]

    # 10
    page_break(story)
    story += [chapter("설정 사용자 지정", 10, styles), p("상단 톱니바퀴에서 설정을 엽니다. 설정은 프로젝트와 별도인 data/settings.json에 저장되고 실행 취소 대상이 아닙니다.", styles)]
    settings_rows = [["구역", "항목"], ["일반", "언어, 레이아웃, 마지막 프로젝트 기억, 이전 세션 복원, Forza 자동 감지"], ["화면", "리모컨 폭, 레이아웃별 폭 기억, 카드 상태 기억, 화면 밀도, 애니메이션"], ["레퍼런스", "확대 / 축소 단위, 커서 중심 확대, 방향키 이동량, Shift 이동량, 교체 전 확인"], ["색상 추출", "확대경, HSB 소수 자릿수 2 / 3"], ["단축키", "모든 작업 키 변경, 기본값 복원"], ["자동 저장 및 복구", "복구용 자동 저장, 간격"], ["고급", "포터블 경로, 실행 취소 범위, 안전 동작 안내"]]
    story += [table(settings_rows, [40 * mm, 118 * mm], styles)]
    story += [section("10.1", "레이아웃", styles), p("자동, 16:9 컴팩트, 21:9 와이드 중 선택합니다. 모든 레이아웃에서 기능은 같으며 배치만 달라집니다. 리모컨 폭은 280~520 DIP이고, 오른쪽 테두리를 끌어 조절할 수 있습니다.", styles)]
    story += [section("10.2", "화면 밀도와 애니메이션", styles), p("화면 밀도는 기능을 숨기지 않고 카드 여백을 자동, 여유롭게, 촘촘하게 중에서 조절합니다. 애니메이션은 보통, 줄이기, 끄기를 선택하며 카드의 짧은 동작에만 영향을 줍니다.", styles)]
    story += [section("10.3", "언어", styles), p("시스템 기본값, English, 한국어 중 선택합니다. 언어 변경은 TraceDeck FE를 다시 실행할 때 적용되므로 먼저 프로젝트를 저장하세요. HSB 숫자와 프로젝트 직렬화 방식은 언어에 따라 바뀌지 않습니다.", styles)]
    story += [section("10.4", "레퍼런스 입력", styles), p("확대 / 축소 단위는 1~50%, 방향키와 Shift + 방향키 이동량은 각각 1~100 px입니다. 커서 중심 확대를 끄면 확대 기준이 달라질 수 있습니다. 레퍼런스 교체 전 확인은 실수로 현재 이미지를 바꾸는 일을 줄입니다.", styles)]
    story += [section("10.5", "단축키와 복구", styles), p("단축키 입력란을 누른 뒤 새 조합을 입력합니다. 중복, 시스템 예약 조합과 Ctrl + V는 사용할 수 없습니다. 전역 작업 단축키는 Ctrl 또는 Alt를 포함해야 하며 컨트롤러나 Forza가 앞에 있을 때만 예약됩니다.", styles)]

    # 11
    page_break(story)
    story += [chapter("단축키", 11, styles), p("아래 표는 v1.0.0 기본값입니다. 설정 - 단축키에서 바꿀 수 있으며 기본값 복원으로 되돌릴 수 있습니다.", styles)]
    shortcut_items = [["새 프로젝트", "Ctrl + N"], ["레퍼런스 열기", "Ctrl + O"], ["프로젝트 열기", "Ctrl + Shift + O"], ["저장", "Ctrl + S"], ["다른 이름 저장", "Ctrl + Shift + S"], ["실행 취소", "Ctrl + Z"], ["다시 실행", "Ctrl + Y / Ctrl + Shift + Z"], ["화면에 맞추기", "Ctrl + 0"], ["실제 크기 100%", "Ctrl + 1"], ["1 px 이동", "방향키"], ["10 px 이동", "Shift + 방향키"], ["동작 취소", "Esc"], ["레퍼런스 표시", "Ctrl + Alt + V"], ["잠금", "Ctrl + Alt + L"], ["색상 추출", "Ctrl + Alt + I"], ["불투명도 -5%", "Ctrl + Alt + ["], ["불투명도 +5%", "Ctrl + Alt + ]"], ["격자", "Ctrl + Alt + G"], ["중앙선", "Ctrl + Alt + C"]]
    shortcuts = [["기능", "기본 키", "기능", "기본 키"]]
    for index in range(0, len(shortcut_items), 2):
        left = shortcut_items[index]
        right = shortcut_items[index + 1] if index + 1 < len(shortcut_items) else ["", ""]
        shortcuts.append(left + right)
    story += [table(shortcuts, [39 * mm, 41 * mm, 39 * mm, 39 * mm], styles, compact=True)]
    story += [section("11.1", "애플리케이션 단축키", styles), p("새 프로젝트, 열기, 저장, 실행 취소, 이동과 같은 단축키는 TraceDeck FE 컨트롤러에서 처리됩니다. 방향키 이동량은 설정값을 따릅니다.", styles)]
    story += [section("11.2", "작업 단축키", styles), p("표시, 잠금, 색상 추출, 불투명도, 격자와 중앙선은 컨트롤러 또는 연결된 Forza가 앞에 있을 때 Windows 작업 단축키로 동작합니다. 다른 앱을 사용할 때는 예약을 해제해 일반 작업을 방해하지 않습니다.", styles), callout("NOTE", "등록에 실패한 사용자 지정 조합이 있으면 그 조합만 비활성화되고 컨트롤러 버튼은 계속 사용할 수 있습니다.", styles)]

    # 12
    page_break(story)
    story += [chapter("문제가 생겼을 때", 12, styles)]
    troubleshooting = [
        ("12.1", "Forza가 자동 연결되지 않아요", "Forza가 창 모드 또는 테두리 없는 창 모드로 실행되어 있는지 확인하고 창 선택에서 직접 고르세요. 목록이 오래되었다면 창 선택을 다시 열거나 다시 연결을 누릅니다."),
        ("12.2", "오버레이가 보이지 않아요", "표시가 켜져 있는지, 레퍼런스가 불러와졌는지, Forza가 최소화되지 않았는지 확인하세요. 다른 앱이 앞에 있으면 그 앱이 오버레이를 자연스럽게 가립니다."),
        ("12.3", "레퍼런스를 움직일 수 없어요", "잠금 / 클릭 통과가 켜져 있으면 마우스가 Forza로 전달됩니다. 잠금을 끈 뒤 드래그하거나 위치 버튼 / 방향키를 사용하세요."),
        ("12.4", "Forza를 마우스로 조작할 수 없어요", "레퍼런스를 배치한 뒤 잠금을 켜세요. 잠금 ON에서는 오버레이가 보여도 클릭이 Forza로 통과합니다."),
        ("12.5", "색상 추출이 작동하지 않아요", "레퍼런스가 있고 표시 가능한 영역인지 확인한 뒤 색상 추출을 다시 시작하세요. 선택 중에는 레퍼런스 픽셀을 한 번 클릭하며 Esc는 취소입니다."),
        ("12.6", "전체 화면에서 오버레이가 보이지 않아요", "독점 전체 화면은 일반 데스크톱 오버레이를 가릴 수 있습니다. Forza를 창 모드 또는 테두리 없는 창 모드로 바꾸세요."),
        ("12.7", "프로젝트가 열리지 않아요", "파일이 .TDFE인지, 복사나 다운로드가 끝났는지 확인하세요. 손상되었거나 더 새로운 형식이면 현재 작업을 유지한 채 열기를 중단합니다."),
        ("12.8", "복구 화면이 나타났어요", "이전 실행이 정상적으로 끝나지 않았거나 저장되지 않은 변경이 발견된 경우입니다. 작업 복구 후 저장하거나 마지막 저장본을 여세요."),
        ("12.9", "Windows 실행 경고가 나타나요", "v1.0.0 포터블 EXE는 코드 서명 범위에 포함되지 않아 Windows 평판 보호가 경고할 수 있습니다. 출처와 ZIP 해시를 확인하고 조직의 보안 정책을 따르세요. 보안 기능을 무조건 우회하지 마세요."),
    ]
    for number, title_text, body_text in troubleshooting:
        story += [section(number, title_text, styles), p(body_text, styles)]
    story += [section("12.10", "오류가 반복돼요", styles), p("현재 프로젝트를 먼저 저장한 뒤 TraceDeck FE를 다시 실행하세요. 진단 정보는 프로그램 폴더의 data/logs 아래에 기록될 수 있습니다. 로그에는 환경 정보가 포함될 수 있으므로 공유하기 전에 내용을 확인하세요.", styles)]

    # 13
    page_break(story)
    story += [chapter("포터블 버전 사용과 데이터 관리", 13, styles)]
    story += [section("13.1", "설치가 필요 없는 구조", styles), p("TraceDeck FE는 Windows x64 self-contained single-file 포터블 앱입니다. 별도 .NET 설치나 Windows Installer가 필요하지 않으며 레지스트리에 글꼴을 설치하지 않습니다.", styles)]
    story += [section("13.2", "처음 압축 해제했을 때", styles), PortableTree(), Spacer(1, 3 * mm)]
    story += [section("13.3", "사용 후 생성될 수 있는 데이터", styles), table([["경로", "내용"], ["data/settings.json", "언어, 레이아웃, 입력, 단축키와 복구 설정"], ["data/logs/", "날짜별 진단 로그"], ["data/recovery/<Project-ID>/", "최근 복구 스냅샷과 공유 원본 데이터"]], [63 * mm, 95 * mm], styles)]
    story += [section("13.4", "설정과 복구까지 함께 옮기기", styles), p("TraceDeck FE를 완전히 종료한 뒤 포터블 폴더 전체를 복사하면 EXE와 data를 함께 옮길 수 있습니다. .TDFE 프로젝트를 다른 위치에 저장했다면 그 파일도 별도로 복사하세요.", styles)]
    story += [section("13.5", "초기 상태로 시작하기", styles), p("프로그램을 종료한 뒤 data 폴더를 안전한 곳에 백업하고 이름을 바꾸면 다음 실행에서 기본 설정으로 시작할 수 있습니다. 복구가 필요할 수 있으므로 내용을 확인하지 않고 삭제하지 마세요. single-file 실행 중 .NET과 Magick.NET의 네이티브 구성 요소는 공식 런타임 방식으로 임시 추출될 수 있지만, 사용자 설정과 복구 프로젝트는 계속 EXE 옆의 data 아래에 저장됩니다.", styles)]

    # 14
    page_break(story)
    story += [chapter("지원 이미지 형식", 14, styles)]
    formats = [["형식", "사용자에게 필요한 설명"], ["PNG", "투명 배경과 알파 지원. 로고 레퍼런스에 적합"], ["JPG / JPEG", "사진과 일반 이미지. 투명 배경 없음"], ["WebP", "정적 이미지 지원. 애니메이션은 첫 프레임을 정지 이미지로 사용"], ["BMP", "비압축 / 단순 래스터 이미지"], ["TIFF / TIF", "첫 페이지를 정지 레퍼런스로 사용"], ["SVG", "벡터 원본을 보관하고 표시 크기에 맞춰 고해상도로 렌더링"], ["ICO", "포함된 프레임 중 적절한 고품질 프레임 선택"], ["AVIF", "고효율 정적 이미지"], ["GIF", "애니메이션 재생 없이 첫 프레임을 정지 레퍼런스로 사용"]]
    story += [table(formats, [38 * mm, 120 * mm], styles)]
    story += [section("14.1", "원본 품질", styles), p("TraceDeck FE는 프로젝트에 원본 바이트를 보관하고 표시용 이미지와 분리합니다. 임의 축소나 자동 선명화, AI 보정은 하지 않습니다. 큰 이미지와 SVG는 필요한 표시 작업을 변경 시점에만 처리합니다.", styles)]
    story += [section("14.2", "애니메이션과 여러 페이지", styles), p("GIF와 애니메이션 WebP는 재생하지 않고 첫 프레임을 사용합니다. TIFF는 첫 페이지를 사용합니다. 애니메이션 재생은 v1.0.0 범위가 아닙니다.", styles)]

    # 15
    page_break(story)
    story += [chapter("자주 묻는 질문과 작업 팁", 15, styles)]
    faq = [
        (".TDFE를 저장했다면 원본 이미지를 삭제해도 되나요?", "정상 저장된 .TDFE에는 원본 레퍼런스가 포함되므로 프로젝트를 다시 열 수 있습니다. 원본을 다른 도구에서도 쓴다면 별도 보관하세요."),
        ("TraceDeck FE가 자동으로 데칼을 만들어주나요?", "아니요. 레퍼런스, 색상과 정렬을 보조하며 도형은 사용자가 Forza에서 직접 배치합니다."),
        ("불투명도를 바꾸면 색상 추출 값도 바뀌나요?", "바뀌지 않습니다. 색상 추출은 표시 불투명도가 아니라 원본 레퍼런스를 읽습니다."),
        ("추출 값이 화면색과 다르게 보일 수 있나요?", "그럴 수 있습니다. 화면에는 불투명도, 효과와 Forza 배경이 합성되지만 추출 값은 원본 픽셀입니다."),
        ("어떤 불투명도가 좋나요?", "이미지와 Forza 도형을 함께 볼 수 있는 약 40~60%에서 시작해 작업에 맞게 조절하세요. 권장 예일 뿐 정답은 아닙니다."),
        ("복잡한 로고는 어디서 시작하나요?", "전체 외곽과 대칭 기준을 먼저 만들고, 큰 색상 면, 안쪽 형태, 작은 디테일 순으로 진행하세요."),
        ("다른 앱을 클릭하면 왜 레퍼런스가 가려지나요?", "오버레이는 다른 앱 위에 강제로 남는 TopMost 창이 아닙니다. Forza로 돌아오면 별도 표시 토글 없이 Forza 위에 다시 나타납니다."),
    ]
    for index, (question, answer) in enumerate(faq, start=1):
        story += [rich(f"<b>Q{index}. {esc(question)}</b><br/>{esc(answer)}", styles)]
    story += [section("15.8", "실전 작업 팁", styles), bullets(["중요한 단계마다 Ctrl + S로 정식 저장합니다.", "팔레트에는 Main, Outline, Shadow처럼 의미 있는 이름을 사용합니다.", "방향키와 Shift + 방향키로 마지막 위치를 정밀하게 맞춥니다.", "Forza 조작 중에는 잠금을 켜고, 레퍼런스 조정 때만 잠금을 끕니다.", "대칭과 반복 구조에는 격자와 중앙선을 필요할 때만 사용합니다."], styles)]

    # 16
    page_break(story)
    story += [chapter("부록", 16, styles)]
    version_rows = [["항목", "값", "항목", "값"], ["제품", "TraceDeck FE", "버전", "1.0.0"], ["실행 파일", "TraceDeckFE.exe", "프로젝트", ".TDFE v1"], ["대상", "Windows 10 / 11 x64", "배포", "Single-file portable"], ["UI 언어", "시스템 / English / 한국어", "사용설명서", "한국어, A4 세로"]]
    story += [section("16.1", "버전 정보", styles), table(version_rows, [22 * mm, 50 * mm, 22 * mm, 64 * mm], styles, compact=True)]
    story += [section("16.2", "비공식 도구 안내", styles), p("TraceDeck FE는 Microsoft, Xbox, Playground Games 또는 Forza 프랜차이즈의 공식 제품이 아니며 제휴, 후원 또는 승인을 받은 제품이 아닙니다. Forza 작업을 돕기 위한 비공식 커뮤니티 도구입니다.", styles)]
    story += [section("16.3", "Pretendard", styles), p("애플리케이션과 이 PDF는 Pretendard 1.3.9 Static Regular, Medium, SemiBold를 사용합니다. Pretendard는 SIL Open Font License 1.1에 따라 제공됩니다. 전체 라이선스는 배포 폴더의 licenses/Pretendard-OFL.txt에 있습니다.", styles)]
    story += [section("16.4", "Magick.NET, ImageMagick과 .NET", styles), p("이미지 형식 처리를 위해 Magick.NET Q8과 ImageMagick 관련 구성 요소를 사용하며, self-contained 실행을 위해 Microsoft .NET 8 Windows Desktop Runtime 구성 요소를 포함합니다. 전체 라이선스와 제3자 고지는 배포 폴더의 licenses 안에 있습니다.", styles)]
    story += [p("licenses/ 폴더에는 Pretendard-OFL.txt, Magick.NET-Apache-2.0.txt, Magick.NET-NOTICE.txt, dotnet-runtime-LICENSE.txt, dotnet-runtime-THIRD-PARTY-NOTICES.txt가 포함됩니다.", styles, "small"), Spacer(1, 2 * mm)]
    story += [section("16.5", "배포 무결성", styles), p("공유받은 ZIP은 제공자가 안내한 SHA-256과 비교할 수 있습니다. 해시는 ZIP 내용이 조금만 달라져도 바뀌므로 다른 버전의 값과 혼동하지 마세요. 이 문서의 배포본에는 EXE, PDF와 licenses 폴더 외에 사용자 프로젝트나 복구 데이터가 포함되지 않습니다.", styles)]
    story += [Spacer(1, 6 * mm), callout("END", "기본 작업은 1장의 5분 빠른 시작에서 다시 확인할 수 있습니다. 기능 이름을 찾을 때는 목차나 PDF 책갈피를 사용하세요.", styles)]
    return story


def register_fonts(repo: Path):
    font_dir = repo / "docs" / "assets" / "fonts"
    pdfmetrics.registerFont(TTFont("Pretendard-Regular", str(font_dir / "Pretendard-Regular.ttf")))
    pdfmetrics.registerFont(TTFont("Pretendard-Medium", str(font_dir / "Pretendard-Medium.ttf")))
    pdfmetrics.registerFont(TTFont("Pretendard-SemiBold", str(font_dir / "Pretendard-SemiBold.ttf")))
    pdfmetrics.registerFontFamily(
        "Pretendard",
        normal="Pretendard-Regular",
        bold="Pretendard-SemiBold",
        italic="Pretendard-Regular",
        boldItalic="Pretendard-SemiBold",
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    output = args.output or repo / "output" / "pdf" / "TraceDeck FE 사용방법.pdf"
    output.parent.mkdir(parents=True, exist_ok=True)
    register_fonts(repo)
    styles = build_styles()
    doc = GuideDocTemplate(str(output), styles)
    doc.multiBuild(build_story(repo, styles))
    print(output.resolve())


if __name__ == "__main__":
    main()
