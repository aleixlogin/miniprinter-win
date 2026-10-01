"""Generate golden protocol fixtures with TiMini-Print's diagnostic tool.

Usage (from an isolated venv with TiMini-Print's requirements installed):

    python tools/generate_timini_fixtures.py <path-to-TiMini-Print-checkout>

For every case it writes, into tests/MiniPrinter.Protocol.Tests/Fixtures/:
  <name>.json  expected job bytes as produced by TiMini for the "X5h-E07A" name
  <name>.pbm   (image cases) the exact 1-bit raster fed to the printer, P4 format

Image cases use pure black/white PNGs at the native 384 px width with the
"threshold" image mode and no trimming, so TiMini's raster equals our input.
"""
from __future__ import annotations

import json
import random
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "tests" / "MiniPrinter.Protocol.Tests" / "Fixtures"
PRINTER_NAME = "X5h-E07A"
WIDTH = 384


def pattern(height: int, seed: int) -> list[list[int]]:
    """Mix of rows that exercise every encoding path (1 = black)."""
    rng = random.Random(seed)
    rows = []
    for y in range(height):
        kind = y % 6
        if kind == 0:
            row = [0] * WIDTH                                   # blank -> BF 7F 7F 7F 03
        elif kind == 1:
            row = [1 if (x // 16 + y) % 2 else 0 for x in range(WIDTH)]  # long runs -> BF
        elif kind == 2:
            row = [rng.randint(0, 1) for _ in range(WIDTH)]     # noise -> A2
        elif kind == 3:
            row = [1] * WIDTH                                   # solid black -> BF FF FF FF 83
        elif kind == 4:
            row = [1 if x in (0, 7, 8, 383) else 0 for x in range(WIDTH)]  # bit order probes
        else:
            row = [1 if x % 2 else 0 for x in range(WIDTH)]     # checkerboard -> A2
        rows.append(row)
    return rows


def write_png(rows: list[list[int]], path: Path) -> None:
    img = Image.new("L", (WIDTH, len(rows)), 255)
    img.putdata([0 if p else 255 for row in rows for p in row])
    img.save(path)


def write_pbm(rows: list[list[int]], path: Path) -> None:
    data = bytearray()
    for row in rows:
        for i in range(0, WIDTH, 8):
            byte = 0
            for bit, p in enumerate(row[i:i + 8]):
                if p:
                    byte |= 0x80 >> bit                         # PBM is MSB-first
            data.append(byte)
    path.write_bytes(f"P4\n{WIDTH} {len(rows)}\n".encode() + bytes(data))


def run_tool(timini: Path, out_json: Path, extra: list[str]) -> dict:
    cmd = [sys.executable, str(timini / "tools" / "debug_protocol_job.py"),
           "--bluetooth-name", PRINTER_NAME, "--out", str(out_json), *extra]
    subprocess.run(cmd, check=True, cwd=timini, capture_output=True)
    return json.loads(out_json.read_text(encoding="utf-8"))


def save(name: str, dump: dict, *, is_text: bool, darkness: int, commit: str) -> None:
    assert dump["device"]["profile_key"] == "d1", dump["device"]
    fixture = {
        "name": name,
        "source": f"TiMini-Print {commit} tools/debug_protocol_job.py --bluetooth-name {PRINTER_NAME}",
        "profile": dump["device"]["profile_key"],
        "isText": is_text,
        "darkness": darkness,
        "packets": len(dump["packets"]),
        "payloadHex": dump["payload_hex"],
    }
    (OUT / f"{name}.json").write_text(json.dumps(fixture, indent=1) + "\n", encoding="utf-8")


def main() -> None:
    timini = Path(sys.argv[1]).resolve()
    commit = subprocess.run(["git", "-C", str(timini), "log", "-1", "--format=%h"],
                            capture_output=True, text=True, check=True).stdout.strip()
    OUT.mkdir(parents=True, exist_ok=True)
    image_flags = ["--image-mode", "threshold", "--force-image-mode",
                   "--no-trim-side-margins", "--no-trim-top-bottom-margins"]
    cases = [
        ("image_pattern", pattern(48, seed=1), 3),
        ("image_tall", pattern(450, seed=2), 3),
        ("image_blank", [[0] * WIDTH for _ in range(8)], 3),
        ("image_darkness5", pattern(12, seed=3), 5),
    ]
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        for name, rows, darkness in cases:
            png = tmp_dir / f"{name}.png"
            write_png(rows, png)
            dump = run_tool(timini, tmp_dir / f"{name}.json",
                            [*image_flags, "--darkness", str(darkness), str(png)])
            save(name, dump, is_text=False, darkness=darkness, commit=commit)
            write_pbm(rows, OUT / f"{name}.pbm")
        dump = run_tool(timini, tmp_dir / "text.json", ["--text", "Hola X5h"])
        save("text_hola", dump, is_text=True, darkness=3, commit=commit)
    print(f"fixtures written to {OUT}")


if __name__ == "__main__":
    main()
