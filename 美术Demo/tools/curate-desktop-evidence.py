"""Crop native desktop evidence; never paint actors or replace their background.

Raw screens remain local because other open applications may contain private data.
The GIF adds only outside-frame matte for the camera's varying crop dimensions.
"""
from pathlib import Path
import hashlib
import json
import re
import shutil
from PIL import Image

demo = Path(__file__).resolve().parents[1]
qa = demo / "qa"
out = qa / "desktop-delivery"
out.mkdir(exist_ok=True)
records = []
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def cropped(run, name, dest, box=None):
    source = qa / run / name
    with Image.open(source) as im:
        im = im.convert("RGB")
        if box is None:
            box = (0, 0, im.width, im.height)
        assert 0 <= box[0] < box[2] <= im.width
        assert 0 <= box[1] < box[3] <= im.height
        im.crop(box).save(out / dest)
    records.append(dict(image=dest, source=str(source.relative_to(demo)),
                        source_sha256=sha(source), crop_box=list(box),
                        operation="native pixels, crop only, no resizing or repaint",
                        output_sha256=sha(out / dest)))


def actor_crop(run, name, dest):
    data = json.loads((qa / run / "desktop-acceptance.json").read_text(encoding="utf-8-sig"))
    capture = [c for c in data["captures"] if c["image"] == name][-1]
    rect = lambda s: list(map(int, re.findall(r"-?\d+", s)))
    x, y, w, h = rect(capture["bounds"])
    bounds = [rect(a["bounds"]) for a in capture["actors"]]
    box = (max(0, min(a[0] for a in bounds) - x - 12),
           max(0, min(a[1] for a in bounds) - y - 12),
           min(w, max(a[0] + a[2] for a in bounds) - x + 12),
           min(h, max(a[1] + a[3] for a in bounds) - y + 12))
    cropped(run, name, dest, box)


cropped("desktop-accept-mortal-r8", "00-multi-monitor-secondary.png", "01-mortal-desktop.png")
cropped("desktop-accept-mortal-r8", "05-ten-kills-awaken-panel.png",
        "02-mortal-ten-kills.png", (19, 12, 461, 430))
actor_crop("desktop-accept-mortal-r8", "06-qishi-desktop-automatic.png", "03-qishi-automatic.png")
cropped("desktop-accept-qishi-full-r12", "30-dongtian-panel-on-real-desktop.png",
        "04-dongtian-panel.png", (19, 12, 725, 566))
cropped("desktop-accept-qishi-full-r12", "41-lingyu-mirror-on-desktop.png", "05-zhuji-mirror-battle.png")

movie = qa / "desktop-accept-mortal-restart-r9"
data = json.loads((movie / "desktop-acceptance.json").read_text(encoding="utf-8-sig"))
raw = data["nativeMovieFrames"]
frames = [f for f in raw if "desktop-idle-walk-" in f["image"]]
assert len(frames) == 10, "Keep the inspected ten-frame sequence only"
sizes = [Image.open(movie / f["image"]).size for f in frames]
width, height = max(w for w, h in sizes), max(h for w, h in sizes)
gif_frames = []
for index, frame in enumerate(frames):
    source = movie / frame["image"]
    with Image.open(source) as im:
        im = im.convert("RGB")
        matte = Image.new("RGB", (width, height), "#e8e6e1")
        offset = ((width - im.width) // 2, (height - im.height) // 2)
        matte.paste(im, offset)
        gif_frames.append(matte)
    records.append(dict(animation_frame=index, source=str(source.relative_to(demo)),
                        source_sha256=sha(source), offset=list(offset),
                        capture_time_seconds=frame["elapsedSeconds"],
                        operation="complete native crop, unchanged; external matte only"))
gif_frames[0].save(out / "desktop-auto-combat.gif", save_all=True,
                   append_images=gif_frames[1:], duration=360, loop=0, disposal=2, optimize=False)

runs = ["mortal-r8", "mortal-restart-r9", "qishi-full-r12", "qishi-restart-r12",
        "zhuji-r12", "zhuji-restart-r12"]
matrix = []
for run in runs:
    folder = qa / ("desktop-accept-" + run)
    report = json.loads((folder / "desktop-acceptance.json").read_text(encoding="utf-8-sig"))
    assert report["passed"] and report["realArchivesUnchanged"], run
    shutil.copy2(folder / "desktop-acceptance.json", out / (run + ".json"))
    shutil.copy2(folder / "desktop-playthrough.log", out / (run + ".log"))
    matrix.append(dict(run=run, passed=True, processId=report["processId"],
                       seconds=report["elapsedSeconds"], nativeClicks=report["nativeMouseClicks"],
                       restart=report["restartVerification"], realArchivesUnchanged=True))
shutil.copy2(qa / "desktop-accept-mortal-r8" / "desktop-performance.csv", out / "desktop-performance.csv")
shutil.copy2(qa / "desktop-regression" / "normal-performance-summary.json", out / "normal-performance-summary.json")
(out / "evidence-provenance.json").write_text(json.dumps(dict(
    capture="real Windows native pixels, no synthetic scene or actor overlay",
    movie="10 chronological frames, following native crop; outside matte only; 360ms per frame",
    raw_policy="raw screens retained locally, curated crops exclude unrelated private messages",
    records=records), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
(out / "acceptance-matrix.json").write_text(json.dumps(matrix, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(matrix, ensure_ascii=False, indent=2))
