#!/usr/bin/env python3
"""Prepare opt-in Demo PNGs without drawing, recolouring or removing backgrounds.

Sources are immutable. Supported production operations are declared rectangular
cuts/retention regions, transparent-margin trimming (opt-in), proportional downscaling,
and byte-identical copying of explicitly allowed opaque backgrounds. Alpha
bounds above 24 are measurements only: no thresholded mask is written to PNGs.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


SCHEMA_VERSION = 1
PREVIEW_SIZE = (220, 240)
DEFAULT_MAX_SIZE = (512, 512)
MEASUREMENT_ALPHA = 24


class AssetError(ValueError):
    pass


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def pair(value, field: str, minimum: int = 1) -> tuple[int, int]:
    if not isinstance(value, (list, tuple)) or len(value) != 2:
        raise AssetError(f"{field} must be [width, height]")
    if any(isinstance(n, bool) or not isinstance(n, int) or n < minimum for n in value):
        raise AssetError(f"{field} values must be integers >= {minimum}")
    return tuple(value)


def integer(value, field: str, minimum: int = 0) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
        raise AssetError(f"{field} must be an integer >= {minimum}")
    return value


def source_box(asset: dict, size: tuple[int, int]) -> tuple[int, int, int, int]:
    """Return a Pillow box. Grid dimensions must divide exactly; never guess."""
    if asset.get("rect") is not None and asset.get("grid") is not None:
        raise AssetError(f"{asset['id']}: use rect or grid, not both")
    width, height = size
    if asset.get("rect") is not None:
        rect = asset["rect"]
        if not isinstance(rect, list) or len(rect) != 4:
            raise AssetError(f"{asset['id']}: rect must be [left, top, width, height]")
        x, y, w, h = [integer(n, "rect", 0 if i < 2 else 1) for i, n in enumerate(rect)]
        box = (x, y, x + w, y + h)
    elif asset.get("grid") is not None:
        grid = asset["grid"]
        cols = integer(grid.get("columns"), "grid.columns", 1)
        rows = integer(grid.get("rows"), "grid.rows", 1)
        margin = pair(grid.get("margin", [0, 0]), "grid.margin", 0)
        gutter = pair(grid.get("gutter", [0, 0]), "grid.gutter", 0)
        inner_w = width - 2 * margin[0] - (cols - 1) * gutter[0]
        inner_h = height - 2 * margin[1] - (rows - 1) * gutter[1]
        if inner_w < cols or inner_h < rows or inner_w % cols or inner_h % rows:
            raise AssetError(f"{asset['id']}: source size does not divide exactly into declared grid")
        if "cell" in grid:
            col, row = pair(grid["cell"], "grid.cell", 0)
            if col >= cols or row >= rows:
                raise AssetError(f"{asset['id']}: grid.cell outside grid")
        else:
            index = integer(grid.get("index"), "grid.index")
            if index >= cols * rows:
                raise AssetError(f"{asset['id']}: grid.index outside grid")
            col, row = index % cols, index // cols
        w, h = inner_w // cols, inner_h // rows
        x = margin[0] + col * (w + gutter[0])
        y = margin[1] + row * (h + gutter[1])
        box = (x, y, x + w, y + h)
    else:
        box = (0, 0, width, height)
    if box[0] < 0 or box[1] < 0 or box[2] > width or box[3] > height:
        raise AssetError(f"{asset['id']}: source rectangle outside {width}x{height}")
    return box


def alpha_metrics(image: Image.Image) -> dict:
    alpha = image.getchannel("A")
    hist = alpha.histogram()
    total = image.width * image.height
    measured = alpha.point(lambda a: 255 if a > MEASUREMENT_ALPHA else 0).getbbox()
    nonzero = alpha.getbbox()
    edge = (sum(alpha.crop((0, 0, image.width, 1)).histogram()[1:])
            + sum(alpha.crop((0, image.height - 1, image.width, image.height)).histogram()[1:])
            + sum(alpha.crop((0, 1, 1, max(1, image.height - 1))).histogram()[1:])
            + sum(alpha.crop((image.width - 1, 1, image.width, max(1, image.height - 1))).histogram()[1:]))
    visible_edge = (sum(alpha.crop((0, 0, image.width, 1)).histogram()[25:])
                    + sum(alpha.crop((0, image.height - 1, image.width, image.height)).histogram()[25:])
                    + sum(alpha.crop((0, 1, 1, max(1, image.height - 1))).histogram()[25:])
                    + sum(alpha.crop((image.width - 1, 1, image.width, max(1, image.height - 1))).histogram()[25:]))
    return {
        "bounds_alpha_gt_0": list(nonzero) if nonzero else None,
        "bounds_alpha_gt_24": list(measured) if measured else None,
        "bounds_alpha_gt_24_purpose": "measurement_only",
        "transparent_pixels": hist[0],
        "opaque_pixels": hist[255],
        "partial_alpha_pixels": sum(hist[1:255]),
        "transparent_fraction": round(hist[0] / total, 6),
        "partial_alpha_fraction": round(sum(hist[1:255]) / total, 6),
        "nonzero_border_pixels": edge,
        "border_pixels_alpha_gt_24": visible_edge,
    }


def fit_size(size: tuple[int, int], maximum: tuple[int, int], allow_upscale: bool = False) -> tuple[int, int]:
    scale = min(maximum[0] / size[0], maximum[1] / size[1])
    if not allow_upscale:
        scale = min(1, scale)
    return max(1, round(size[0] * scale)), max(1, round(size[1] * scale))


def preview(image: Image.Image) -> Image.Image:
    canvas = Image.new("RGBA", PREVIEW_SIZE, (0, 0, 0, 0))
    size = fit_size(image.size, (204, 218), True)
    fitted = image.resize(size, Image.Resampling.LANCZOS) if size != image.size else image
    canvas.alpha_composite(fitted, ((220 - size[0]) // 2, 228 - size[1]))
    return canvas


def checkerboard(size: tuple[int, int], tile: int = 10) -> Image.Image:
    image = Image.new("RGBA", size, (235, 237, 239, 255))
    draw = ImageDraw.Draw(image)
    for y in range(0, size[1], tile):
        for x in range(0, size[0], tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle((x, y, x + tile - 1, y + tile - 1), fill=(210, 214, 218, 255))
    return image


def contact_sheet(records: list[dict], images: list[Image.Image], destination: Path) -> None:
    columns = min(4, len(records))
    cell_w, cell_h = 252, 292
    sheet = Image.new("RGB", (columns * cell_w, math.ceil(len(records) / columns) * cell_h), "#222932")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=14)
    small = ImageFont.load_default(size=11)
    for index, (record, image) in enumerate(zip(records, images)):
        x, y = (index % columns) * cell_w, (index // columns) * cell_h
        cell = checkerboard(PREVIEW_SIZE)
        cell.alpha_composite(preview(image))
        sheet.paste(cell.convert("RGB"), (x + 16, y + 8))
        draw.text((x + 16, y + 252), record["id"], fill="#f2e8cb", font=font)
        draw.text((x + 16, y + 273), f"{image.width}x{image.height}  alpha: {record['output_alpha']['transparent_fraction']:.1%} clear", fill="#bdc6cf", font=small)
    sheet.save(destination, "PNG")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    inputs = parser.add_mutually_exclusive_group(required=True)
    inputs.add_argument("--manifest", type=Path, help="JSON manifest; relative sources resolve beside it")
    inputs.add_argument("--source", type=Path, help="single source image (use --name)")
    parser.add_argument("--name", help="single-image asset ID, e.g. hero-mortal")
    parser.add_argument("--kind", default="illustration", help="single-image semantic kind")
    parser.add_argument("--output-dir", type=Path, required=True, help="derived Demo runtime PNG directory")
    parser.add_argument("--qa-dir", type=Path, required=True, help="QA reports, 220x240 previews and contact sheet")
    parser.add_argument("--max-size", type=int, nargs=2, metavar=("WIDTH", "HEIGHT"), help="override manifest max_size")
    parser.add_argument("--trim-transparent", action="store_true", help="crop only fully transparent outer margin; preserve every alpha>0 pixel")
    parser.add_argument("--overwrite", action="store_true", help="replace derived outputs; source images are always protected")
    parser.add_argument("--dry-run", action="store_true", help="read and validate sources; do not create files")
    return parser


def run(args) -> dict:
    if args.manifest:
        manifest_path = args.manifest.resolve()
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        if manifest.get("version", SCHEMA_VERSION) != SCHEMA_VERSION:
            raise AssetError("unsupported manifest version")
        base = manifest_path.parent
        entries = manifest.get("assets")
        defaults = manifest.get("defaults", {})
    else:
        if not args.name:
            raise AssetError("--name is required with --source")
        base = Path.cwd()
        manifest_path = None
        entries = [{"id": args.name, "source": str(args.source.resolve()), "kind": args.kind}]
        defaults = {}
    if not isinstance(entries, list) or not entries:
        raise AssetError("manifest assets must be a nonempty list")
    output_dir, qa_dir = args.output_dir.resolve(), args.qa_dir.resolve()
    assets = []
    source_paths = set()
    outputs = set()
    for entry in entries:
        if not isinstance(entry, dict):
            raise AssetError("each manifest asset must be an object")
        asset = {**defaults, **entry}
        asset_id = asset.get("id", "")
        if not isinstance(asset_id, str) or not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", asset_id):
            raise AssetError(f"invalid asset id: {asset_id!r}")
        filename = asset.get("output", asset_id + ".png")
        if not isinstance(filename, str) or not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*\.png", filename):
            raise AssetError(f"{asset_id}: output must be a plain lowercase PNG filename")
        if filename in outputs or asset_id in {a["id"] for a in assets}:
            raise AssetError(f"duplicate asset ID or output: {asset_id}")
        outputs.add(filename)
        if not isinstance(asset.get("source"), str) or not asset["source"]:
            raise AssetError(f"{asset_id}: source is required")
        source = (base / asset["source"]).resolve()
        if not source.is_file():
            raise AssetError(f"{asset_id}: source does not exist: {source}")
        source_paths.add(source)
        asset.update({"id": asset_id, "output": filename, "source_path": source})
        assets.append(asset)

    destinations = [output_dir / a["output"] for a in assets]
    destinations += [qa_dir / "previews" / (a["id"] + "-220x240.png") for a in assets]
    destinations += [output_dir / "asset-manifest.json", qa_dir / "asset-report.json", qa_dir / "contact-sheet.png"]
    protected = source_paths | ({manifest_path} if manifest_path else set())
    for destination in destinations:
        if destination.resolve() in protected:
            raise AssetError(f"refusing to overwrite source or input manifest: {destination}")
        if destination.exists() and not args.overwrite:
            raise AssetError(f"derived output exists; use --overwrite: {destination}")
    if len(set(p.resolve() for p in destinations)) != len(destinations):
        raise AssetError("output and QA destinations collide")

    records, images = [], []
    for asset in assets:
        source = asset["source_path"]
        source_hash = sha256_file(source)
        with Image.open(source) as input_image:
            original_size, source_mode = input_image.size, input_image.mode
            has_alpha = "A" in input_image.getbands() or "transparency" in input_image.info
            if asset.get("require_alpha", True) and not asset.get("allow_opaque", False) and not has_alpha:
                raise AssetError(f"{asset['id']}: source has no alpha channel; no background removal is performed")
            box = source_box(asset, original_size)
            image = input_image.convert("RGBA").crop(box)
        original_rectangle_alpha = alpha_metrics(image)
        keep_rects = asset.get("keep_rects")
        if keep_rects is not None:
            if not isinstance(keep_rects, list) or not keep_rects:
                raise AssetError(f"{asset['id']}: keep_rects must be a nonempty list of absolute source rectangles")
            kept = Image.new("RGBA", image.size, (0, 0, 0, 0))
            for rect in keep_rects:
                retained = source_box({"id": asset["id"], "rect": rect}, original_size)
                if retained[0] < box[0] or retained[1] < box[1] or retained[2] > box[2] or retained[3] > box[3]:
                    raise AssetError(f"{asset['id']}: keep_rects must stay inside the source rect")
                relative = (retained[0] - box[0], retained[1] - box[1], retained[2] - box[0], retained[3] - box[1])
                # No mask/threshold/blend: copy every original RGBA byte in each declared rectangle.
                kept.paste(image.crop(relative), relative[:2])
            image = kept
        source_alpha = alpha_metrics(image)
        if source_alpha["bounds_alpha_gt_0"] is None:
            raise AssetError(f"{asset['id']}: source rectangle is fully transparent")
        if asset.get("require_transparency", True) and not asset.get("allow_opaque", False) and source_alpha["transparent_pixels"] == 0:
            raise AssetError(f"{asset['id']}: rectangle has no transparent pixels; inspect the original image")
        trimmed_box = (0, 0, image.width, image.height)
        trimmed = bool(args.trim_transparent or asset.get("trim_transparent", False))
        if trimmed:
            trimmed_box = tuple(source_alpha["bounds_alpha_gt_0"])
            image = image.crop(trimmed_box)
        maximum = pair(args.max_size or asset.get("max_size", DEFAULT_MAX_SIZE), "max_size")
        target = fit_size(image.size, maximum, bool(asset.get("allow_upscale", False)))
        if target != image.size:
            image = image.resize(target, Image.Resampling.LANCZOS)
        if asset.get("copy_source", False) and (box != (0, 0, *original_size) or trimmed or keep_rects or target != original_size):
            raise AssetError(f"{asset['id']}: copy_source requires the complete source, no trim and no resize")
        warnings = []
        if source_alpha["border_pixels_alpha_gt_24"] and not asset.get("allow_opaque", False):
            warnings.append("alpha>24 touches source rectangle border; inspect for clipped or neighbouring art (measurement only)")
        record = {
            "id": asset["id"], "kind": asset.get("kind", "illustration"), "file": asset["output"],
            "source": asset["source"], "source_absolute": str(source), "source_sha256": source_hash,
            "source_size": list(original_size), "source_mode": source_mode, "source_has_alpha": has_alpha,
            "source_rectangle_xywh": [box[0], box[1], box[2] - box[0], box[3] - box[1]],
            "source_rectangle_alpha": original_rectangle_alpha,
            "source_keep_rectangles_xywh": keep_rects,
            "retained_rectangle_alpha": source_alpha,
            "transparent_margin_trim": {"enabled": trimmed, "criterion": "alpha_gt_0", "box_ltrb": list(trimmed_box)},
            "operations": (["declared_rectangle_cut"] if box != (0, 0, *original_size) else [])
                          + (["declared_multiple_rectangle_retention_no_alpha_threshold"] if keep_rects else [])
                          + (["trim_fully_transparent_margin"] if trimmed else [])
                          + (["proportional_lanczos_resize"] if target != (trimmed_box[2] - trimmed_box[0], trimmed_box[3] - trimmed_box[1]) else []),
            "output_size": list(image.size), "output_mode": source_mode if asset.get("copy_source", False) else "RGBA", "output_alpha": alpha_metrics(image),
            "copy_source_bytes": bool(asset.get("copy_source", False)),
            "preview_size": list(PREVIEW_SIZE), "rig_status": "complete_illustration_not_skeletal_parts",
            "warnings": warnings,
        }
        if asset.get("note"):
            record["note"] = asset["note"]
        images.append(image)
        records.append(record)
    report = {
        "version": SCHEMA_VERSION,
        "created_utc": datetime.now(timezone.utc).isoformat(),
        "policy": {
            "source_files_immutable": True, "background_removal": False, "recolour": False, "redraw": False,
            "alpha_gt_24_bounds": "measurement_only", "alpha_edges": "original RGBA retained; resized only when declared",
            "preview": "220x240 transparent canvas; equal aspect ratio; bottom baseline 228; inspection only",
            "rig_status": "full illustration cuts do not constitute skeletal parts",
        },
        "asset_count": len(records), "assets": records,
    }
    if args.dry_run:
        return report
    output_dir.mkdir(parents=True, exist_ok=True)
    (qa_dir / "previews").mkdir(parents=True, exist_ok=True)
    for record, image in zip(records, images):
        destination = output_dir / record["file"]
        if record["copy_source_bytes"]:
            shutil.copy2(record["source_absolute"], destination)
        else:
            image.save(destination, "PNG", optimize=True)
        record["output_sha256"] = sha256_file(destination)
        preview_path = qa_dir / "previews" / (record["id"] + "-220x240.png")
        preview(image).save(preview_path, "PNG")
        record["preview_sha256"] = sha256_file(preview_path)
        if sha256_file(Path(record["source_absolute"])) != record["source_sha256"]:
            raise AssetError(f"source changed during processing: {record['source_absolute']}")
    contact_sheet(records, images, qa_dir / "contact-sheet.png")
    (qa_dir / "asset-report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    package = {**report, "assets": [{k: v for k, v in r.items() if k != "source_absolute"} for r in records]}
    (output_dir / "asset-manifest.json").write_text(json.dumps(package, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    try:
        report = run(args)
    except (AssetError, OSError, json.JSONDecodeError, Image.DecompressionBombError) as error:
        print(f"Asset preparation failed: {error}", file=sys.stderr)
        return 2
    print(json.dumps({"status": "validated" if args.dry_run else "prepared", "asset_count": report["asset_count"],
                      "warnings": sum(len(a["warnings"]) for a in report["assets"]),
                      "outputs": [{"id": a["id"], "size": a["output_size"], "file": a["file"]} for a in report["assets"]]}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
