"""Package only the tested desktop executable and resource copies, never profiles."""
from pathlib import Path
import hashlib
import json
import zipfile

demo = Path(__file__).resolve().parents[1]
runtime = demo / "runtime-desktop"
exe = runtime / "桌面修仙-透明桌面Demo.exe"
usage = demo / "透明桌面使用说明.txt"
files = [exe, Path(str(exe) + ".config")]
for folder in ("Assets", "ArtDemoAssets"):
    files.extend(sorted(p for p in (runtime / folder).rglob("*") if p.is_file()))
assert all(p.is_file() for p in files) and usage.is_file()
assert len(list((runtime / "ArtDemoAssets").glob("*.png"))) == 27
entries = [(p, "桌面修仙-透明桌面Demo/" + p.relative_to(runtime).as_posix()) for p in files]
entries.append((usage, "桌面修仙-透明桌面Demo/使用说明.txt"))
archive = demo / "桌面修仙-透明桌面Demo.zip"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
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
    "date": "2026-10-18", "entry": "transparent desktop, no scene window",
    "branch": "feature/v6-art-foundation", "archive": archive.name,
    "archive_bytes": archive.stat().st_size, "archive_sha256": sha(archive),
    "executable": exe.name, "executable_sha256": sha(exe),
    "new_png_count": 27, "legacy_asset_files": len(list((runtime / "Assets").rglob("*.*"))),
    "archive_files": len(entries), "crc_and_byte_verification": "passed",
    "profiles_included": False, "raw_desktop_captures_included": False,
}
(demo / "desktop-delivery.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(record, ensure_ascii=False, indent=2))
