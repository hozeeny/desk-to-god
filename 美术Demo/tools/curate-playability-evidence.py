"""Archive native desktop crops and separate offscreen layout evidence."""
from pathlib import Path
import hashlib
import json
import re
import shutil
from PIL import Image, ImageDraw

demo = Path(__file__).resolve().parents[1]
qa = demo / "qa"
out = qa / "playability-20261022"
out.mkdir(exist_ok=True)
runs = {
    "normal": "desktop-accept-playability-x1-20261022-r5",
    "breakthrough": "desktop-accept-playability-qishi-20261022-r6",
    "restart": "desktop-accept-playability-restart-20261022-r7",
    "foundation": "desktop-accept-playability-zhuji-20261022-r8",
}
reports = {}
provenance = []
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
for key, run in runs.items():
    folder = qa / run
    data = json.loads((folder / "desktop-acceptance.json").read_text(encoding="utf-8-sig"))
    assert data["passed"] and data["realArchivesUnchanged"], run
    reports[key] = data
    for source_name, target_name in [("desktop-acceptance.json", key + ".json"), ("desktop-playthrough.log", key + ".log")]:
        shutil.copy2(folder / source_name, out / target_name)
shutil.copy2(qa / runs["normal"] / "desktop-performance.csv", out / "desktop-performance.csv")


def crop(key, name, dest, box=None):
    source = qa / runs[key] / name
    with Image.open(source) as im:
        im = im.convert("RGB")
        box = box or (0, 0, im.width, im.height)
        im.crop(box).save(out / dest)
    provenance.append(dict(image=dest, source=str(source.relative_to(demo)),
                           source_sha256=sha(source), crop_box=list(box),
                           operation="crop only, native Windows pixels unchanged"))


crop("normal", "00-first-visible-welcome.png", "01-first-steps.png", (8, 0, 478, 731))
crop("normal", "00-multi-monitor-secondary.png", "02-mortal-desktop.png")
crop("breakthrough", "10-training-handbook.png", "03-training-handbook.png", (8, 0, 478, 731))
crop("breakthrough", "11-baiye-from-handbook.png", "04-baiye-entry.png", (8, 0, 712, 872))
crop("breakthrough", "41-lingyu-mirror-on-desktop.png", "05-mirror-battle.png")

normal = reports["normal"]
frames = [f for f in normal["nativeMovieFrames"] if "qishi-automatic-combat-" in f["image"]]
assert len(frames) >= 4
images = [Image.open(qa / runs["normal"] / f["image"]).convert("RGB") for f in frames]
width, height = max(im.width for im in images), max(im.height for im in images)
movie = []
for frame, im in zip(frames, images):
    page = Image.new("RGB", (width, height), "#e8e6e1")
    offset = ((width - im.width) // 2, (height - im.height) // 2)
    page.paste(im, offset); movie.append(page)
    provenance.append(dict(animation_source=str((qa / runs["normal"] / frame["image"]).relative_to(demo)),
                           elapsed_seconds=frame["elapsedSeconds"], outside_matte_offset=list(offset),
                           operation="complete native crop unchanged, matte outside frame only"))
durations = [max(100, min(1000, int((frames[i + 1]["elapsedSeconds"] - frames[i]["elapsedSeconds"]) * 1000))) for i in range(len(frames) - 1)]
durations.append(durations[-1])
movie[0].save(out / "normal-speed-auto.gif", save_all=True, append_images=movie[1:], duration=durations, loop=0, disposal=2, optimize=False)
sheet = Image.new("RGB", (1000, 300 * ((len(movie) + 3) // 4)), "white")
for i, im in enumerate(movie):
    thumb = im.copy(); thumb.thumbnail((245, 275))
    sheet.paste(thumb, ((i % 4) * 250, (i // 4) * 300 + 20))
    ImageDraw.Draw(sheet).text(((i % 4) * 250 + 4, (i // 4) * 300), str(i + 1), fill="black")
sheet.save(demo / "verification" / "playability-movie-contact.jpg")

samples = [p for p in normal["performance"] if p["elapsedSeconds"] >= normal["normalSpeedObservationStartedAt"] + 10]
summary = dict(passed=True, total_seconds=normal["elapsedSeconds"], native_clicks=normal["nativeMouseClicks"],
               clock="x1 throughout; setup probes separate from 0.75-second main combat pacing",
               normal_observation_seconds=normal["normalSpeedObservationSeconds"], steady_samples=len(samples),
               steady_cpu_machine_mean=sum(p["cpuMachinePercent"] for p in samples) / len(samples),
               private_mib_minmax=[min(p["privateMiB"] for p in samples), max(p["privateMiB"] for p in samples)],
               gdi_minmax=[min(p["gdi"] for p in samples), max(p["gdi"] for p in samples)],
               user_minmax=[min(p["user"] for p in samples), max(p["user"] for p in samples)],
               archives_unchanged=True, scope="short desktop observation, not a soak or player-fun verdict")
(out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
(out / "evidence-provenance.json").write_text(json.dumps(provenance, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
layout = out / "offscreen-layout"
layout.mkdir(exist_ok=True)
for path in (demo / "verification" / "panel-layout").iterdir():
    if path.is_file() and path.suffix in (".png", ".json", ".log"):
        shutil.copy2(path, layout / path.name)
print(json.dumps(summary, ensure_ascii=False, indent=2))
