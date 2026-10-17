"""Create review thumbnails only; never redraw or modify source PNGs."""
from pathlib import Path
from PIL import Image
import hashlib
import json

BASE = Path(__file__).resolve().parent
SOURCE_VERSION = "v4"
SIZE = (220, 240)
FIT = (168, 202)
FOOT_BASELINE = 222
HORIZONTAL_CENTER = 104
BACKGROUNDS = {"light": "#F2F0E8", "dark": "#151820"}


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    destination = BASE / "previews"
    destination.mkdir(exist_ok=True)
    report = []
    for candidate in ("A1", "A2", "A3"):
        source = BASE / "raw" / f"{candidate}-{SOURCE_VERSION}.png"
        original_hash = sha256(source)
        with Image.open(source) as opened:
            image = opened.convert("RGBA")
        alpha = image.getchannel("A")
        bounds = alpha.getbbox()  # Includes every nonzero alpha pixel, even edge noise.
        if bounds is None:
            raise ValueError(f"Empty alpha: {source}")
        cropped = image.crop(bounds)
        scale = min(FIT[0] / cropped.width, FIT[1] / cropped.height, 1)
        resized_size = tuple(max(1, round(value * scale)) for value in cropped.size)
        # Associated-alpha resizing avoids using hidden RGB as an opaque matte.
        resized = cropped.convert("RGBa").resize(resized_size, Image.Resampling.LANCZOS).convert("RGBA")
        position = (HORIZONTAL_CENTER - resized.width // 2, FOOT_BASELINE - resized.height)
        thumbnail = Image.new("RGBA", SIZE, (0, 0, 0, 0))
        thumbnail.alpha_composite(resized, position)
        thumbnail.save(destination / f"{candidate}-220x240.png")
        for name, color in BACKGROUNDS.items():
            background = Image.new("RGBA", SIZE, color)
            background.alpha_composite(thumbnail)
            background.convert("RGB").save(destination / f"{candidate}-220x240-{name}.png")
        histogram = alpha.histogram()
        report.append({
            "candidate": candidate,
            "source": source.relative_to(BASE).as_posix(),
            "source_sha256": original_hash,
            "source_size": list(image.size),
            "source_mode": image.mode,
            "alpha_extrema": list(alpha.getextrema()),
            "alpha_zero_percent": round(100 * histogram[0] / sum(histogram), 4),
            "all_nonzero_alpha_bounds": list(bounds),
            "preview_size": list(SIZE),
            "scale": scale,
            "resized_size": list(resized_size),
            "position": list(position),
            "preview": f"previews/{candidate}-220x240.png",
            "light_preview": f"previews/{candidate}-220x240-light.png",
            "dark_preview": f"previews/{candidate}-220x240-dark.png",
        })
        if sha256(source) != original_hash:
            raise RuntimeError(f"Source changed during preview creation: {source}")
    output = {"source_version": SOURCE_VERSION, "method": "Proportional Lanczos resize, all nonzero alpha bounds, no drawing or source changes", "candidates": report}
    (BASE / "preview-report.json").write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(output, ensure_ascii=False))


if __name__ == "__main__":
    main()
