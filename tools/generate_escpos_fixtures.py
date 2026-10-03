"""Generate real ESC/POS streams with python-escpos for the MiniPrinter.Escpos tests.

Usage (from an isolated venv):

    python -m venv .venv && .venv/Scripts/pip install python-escpos pillow
    .venv/Scripts/python tools/generate_escpos_fixtures.py

It writes, into tests/MiniPrinter.Escpos.Tests/Fixtures/, one <name>.bin per ticket: the exact bytes
python-escpos' Dummy printer would send to a 58 mm ESC/POS printer. The reference rasters
(<name>.png) are NOT produced here: they are the interpreter's own output, reviewed by eye and
regenerated with `UPDATE_GOLDEN=1 dotnet test tests/MiniPrinter.Escpos.Tests`.

The tickets are 32 columns wide (58 mm paper, font A), like the 384-dot head.
"""
from __future__ import annotations

import sys
from pathlib import Path

from escpos.printer import Dummy
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "tests" / "MiniPrinter.Escpos.Tests" / "Fixtures"
WIDTH = 32


def row(left: str, right: str) -> str:
    return left + " " * (WIDTH - len(left) - len(right)) + right + "\n"


def logo() -> Image.Image:
    """A 256 x 96 black and white logo: a ring, a bar and a diagonal."""
    image = Image.new("1", (256, 96), 1)
    draw = ImageDraw.Draw(image)
    draw.ellipse((8, 8, 88, 88), outline=0, width=6)
    draw.rectangle((110, 20, 240, 40), fill=0)
    draw.line((110, 90, 240, 50), fill=0, width=5)
    return image


def receipt() -> bytes:
    p = Dummy()
    p.set(align="center", bold=True, double_height=True, double_width=True)
    p.text("CAFE MINI\n")
    p.set(align="center", bold=False, normal_textsize=True)
    p.text("C/ Mayor 12 - Madrid\n")
    p.text("-" * WIDTH + "\n")
    p.set(align="left")
    p.text(row("2 x Cafe con leche", "3,60"))
    p.text(row("1 x Tostada", "2,20"))
    p.text(row("1 x Zumo", "2,50"))
    p.text("-" * WIDTH + "\n")
    p.set(bold=True, double_height=True)
    p.text(row("TOTAL", "8,30"))
    p.set(bold=False, normal_textsize=True, align="center")
    p.text("\nGracias por su visita\n")
    p.qr("https://example.com/ticket/0042", native=True, size=5)
    p.cut()
    return p.output


def styles() -> bytes:
    p = Dummy()
    p.set(bold=True)
    p.text("Negrita\n")
    p.set(bold=False, underline=1)
    p.text("Subrayado fino\n")
    p.set(underline=2)
    p.text("Subrayado grueso\n")
    p.set(underline=0, double_width=True)
    p.text("Doble ancho\n")
    p.set(double_width=False, double_height=True)
    p.text("Doble alto\n")
    p.set(double_height=False, invert=True)
    p.text(" Inverso \n")
    p.set(invert=False, font="b")
    p.text("Fuente B: " + "x" * 32 + "\n")
    p.set(font="a", align="center")
    p.text("Centrado\n")
    p.set(align="right")
    p.text("Derecha\n")
    p.set(align="left", normal_textsize=True)
    p.text("Fin\n")
    p.cut()
    return p.output


def charset() -> bytes:
    p = Dummy()
    p.text("Cafe: ñandú, año, € 12,50\n")
    p.text("Acentos: áéíóú ÁÉÍÓÚ ü ¿¡\n")
    p.text("Cajas: ┌──┐ │ok│ └──┘\n")
    p.cut()
    return p.output


def logo_raster() -> bytes:
    p = Dummy()
    p.set(align="center")
    p.image(logo(), impl="bitImageRaster")
    p.text("Logo raster\n")
    p.cut()
    return p.output


def logo_columns() -> bytes:
    p = Dummy()
    p.image(logo().resize((128, 48)), impl="bitImageColumn")
    p.text("Logo en columnas\n")
    p.cut()
    return p.output


def barcodes() -> bytes:
    p = Dummy()
    p.barcode("5901234123457", "EAN13", height=64, width=2, pos="BELOW", function_type="B")
    p.text("\n")
    p.barcode("{BMINI-0042", "CODE128", height=64, width=2, pos="BELOW", function_type="B")
    p.text("\n")
    p.barcode("HOLA-12", "CODE39", height=48, width=2, pos="OFF", function_type="B")
    p.text("\n")
    p.cut()
    return p.output


def qr_image() -> bytes:
    """The QR drawn by python-escpos itself and sent as a raster image (native=False)."""
    p = Dummy()
    p.set(align="center")
    p.qr("https://example.com", native=False, size=6)
    p.cut()
    return p.output


def two_tickets() -> bytes:
    p = Dummy()
    p.text("Ticket uno\n")
    p.cut()
    p.text("Ticket dos\n")
    p.text("Segunda linea\n")
    p.cut()
    return p.output



def cjk_utf8() -> bytes:
    """Japanese, Chinese, Korean, emoji and Hebrew sent as UTF-8 (python-escpos does no CJK itself: raw bytes)."""
    p = Dummy()
    p._raw(b"\x1b@")
    for line in ("JA: こんにちは世界 日本語テスト", "ZH: 你好，世界 简体中文", "ZH-TW: 繁體中文",
                 "KO: 안녕하세요 한국어", "Emoji: 😀 🚀 ☕", "HE: שלום עולם", "Ancho completo: ＡＢＣ１２３ ｱｲｳ"):
        p._raw(line.encode("utf-8") + b"\n")
    p.cut()
    return p.output


def cjk_kanji() -> bytes:
    """Shift-JIS: half-width katakana through table 1 (CP932) and kanji mode (FS & ... FS .)."""
    p = Dummy()
    p._raw(b"\x1b@")
    p._raw(b"\x1b\x74\x01" + "ｱｲｳｴｵ ｶﾀｶﾅ ﾊﾝｶｸ".encode("shift_jis") + b"\n\x1b\x74\x00")
    p._raw(b"\x1c&" + "日本語テキスト".encode("shift_jis") + b"\n" + "漢字モード".encode("shift_jis") + b"\n\x1c.")
    p._raw(b"Fin\n")
    p.cut()
    return p.output



def arabic_thai() -> bytes:
    """Arabic (contextual forms, lam-alef) and Thai (marks) as UTF-8, mixed with Latin text."""
    p = Dummy()
    p._raw(b"\x1b@")
    for line in ("AR: مرحبا بالعالم", "AR: لا إله إلا الله", "AR: السلام عليكم ورحمة الله وبركاته 123",
                 "TH: สวัสดีชาวโลก ภาษาไทย", "TH: ที่ นั่น ผู้ชาย กรุงเทพ", "HE: שלום עולם"):
        p._raw(line.encode("utf-8") + b"\n")
    p.cut()
    return p.output


CASES = {
    "receipt": receipt,
    "styles": styles,
    "charset": charset,
    "logo-raster": logo_raster,
    "logo-columns": logo_columns,
    "barcodes": barcodes,
    "qr-image": qr_image,
    "two-tickets": two_tickets,
    "cjk-utf8": cjk_utf8,
    "cjk-kanji": cjk_kanji,
    "arabic-thai": arabic_thai,
}


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for name, build in CASES.items():
        data = build()
        (OUT / f"{name}.bin").write_bytes(data)
        print(f"{name}.bin: {len(data)} bytes")


if __name__ == "__main__":
    sys.exit(main())
