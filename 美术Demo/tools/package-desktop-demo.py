"""Package the tested desktop executable/resources, excluding player profiles."""
from pathlib import Path
import hashlib
import json
import zipfile
from datetime import date

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_art_assets(runtime_art, source_art):
    """Reject stale, missing, additional or altered art before writing a ZIP."""
    manifest_path = source_art / "asset-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    assets = manifest["assets"]
    if not assets or manifest["asset_count"] != len(assets):
        raise ValueError("Invalid controlled art manifest asset count")
    names = set()
    for asset in assets:
        name = asset["file"]
        if not isinstance(name, str) or Path(name).name != name or "/" in name or "\\" in name or not name.endswith(".png") or name in names:
            raise ValueError("Invalid or duplicate controlled art filename: " + str(name))
        names.add(name)
        if sha(source_art / name) != asset["output_sha256"]:
            raise ValueError("Source art differs from manifest: " + name)
    rig_names = {"hero-rig-" + stage + ".png" for stage in ("mortal", "qishi", "zhuji")}
    if names & rig_names:
        raise ValueError("Hero rigs must be separate from illustration manifest entries")
    expected_pngs = names | rig_names
    expected_files = expected_pngs | {"asset-manifest.json"}
    for name in rig_names:
        expected_files.update({str(Path(name).with_suffix(suffix)) for suffix in (".rects", ".anchors")})
    source_files = {p.relative_to(source_art).as_posix() for p in source_art.rglob("*") if p.is_file()}
    runtime_files = {p.relative_to(runtime_art).as_posix() for p in runtime_art.rglob("*") if p.is_file()}
    if source_files != expected_files:
        raise ValueError("Controlled art folder has unexpected/missing files: " + str(sorted(source_files ^ expected_files)))
    if runtime_files != expected_files:
        raise ValueError("Runtime art set differs from controlled source: " + str(sorted(runtime_files ^ expected_files)))
    for name in sorted(expected_files):
        if sha(runtime_art / name) != sha(source_art / name):
            raise ValueError("Runtime art differs from controlled source: " + name)
    return {"new_png_count": len(expected_pngs), "illustration_png_count": len(names), "hero_rig_png_count": len(rig_names)}


def main():
    demo = Path(__file__).resolve().parents[1]
    runtime = demo / "runtime-desktop"
    exe = runtime / "桌面修仙-透明桌面Demo.exe"
    usage = demo / "透明桌面使用说明.txt"
    art_counts = validate_art_assets(runtime / "ArtDemoAssets", demo.parent / "源代码" / "ArtDemoAssets")
    files = [exe, Path(str(exe) + ".config")]
    for folder in ("Assets", "ArtDemoAssets"):
        files.extend(sorted(p for p in (runtime / folder).rglob("*") if p.is_file()))
    if not all(p.is_file() for p in files) or not usage.is_file():
        raise FileNotFoundError("Missing executable, config, assets or usage instructions")
    entries = [(p, "桌面修仙-透明桌面Demo/" + p.relative_to(runtime).as_posix()) for p in files]
    entries.append((usage, "桌面修仙-透明桌面Demo/使用说明.txt"))
    archive = demo / "桌面修仙-透明桌面Demo.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=8) as pack:
        for path, name in entries:
            pack.write(path, name)
    with zipfile.ZipFile(archive) as pack:
        assert pack.testzip() is None
        assert len(pack.namelist()) == len(entries)
        assert not any("profiles/" in n or n.endswith(".log") or n.startswith("/") or ".." in n.split("/") for n in pack.namelist())
        for path, name in entries:
            assert hashlib.sha256(pack.read(name)).hexdigest() == sha(path), name
    record = {
        "date": date.today().isoformat(), "entry": "transparent desktop, no scene window",
        "branch": "feature/v6-art-foundation", "archive": archive.name,
        "archive_bytes": archive.stat().st_size, "archive_sha256": sha(archive),
        "executable": exe.name, "executable_sha256": sha(exe),
        **art_counts, "legacy_asset_files": len(list((runtime / "Assets").rglob("*.*"))),
        "archive_files": len(entries), "crc_and_byte_verification": "passed",
        "profiles_included": False, "raw_desktop_captures_included": False,
    }
    (demo / "desktop-delivery.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(record, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
