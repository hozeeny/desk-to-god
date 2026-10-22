"""Record the lifetime of one newly launched, isolated soak process; never stop it."""
from pathlib import Path
import datetime
import hashlib
import json
import os
import subprocess
import sys


def utc_now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def write_record(path, data):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def main():
    spec_path, witness = (Path(value).resolve() for value in sys.argv[1:])
    demo = Path(__file__).resolve().parents[1]
    verification = (demo / "verification").resolve()
    spec = json.loads(spec_path.read_text(encoding="utf-8"))
    command = spec["command"]
    executable = Path(command[0]).resolve()
    profile, output = Path(command[4]).resolve(), Path(command[5]).resolve()
    if len(command) != 7 or command[1] != "--desktop-demo-soak" or not 8 <= float(command[6]) <= 168:
        raise ValueError("Only the explicit eight-hour-or-longer soak entry is accepted")
    if executable.name != "桌面修仙-透明桌面Demo.exe" or verification not in executable.parents:
        raise ValueError("Require the separately built project verification executable")
    if verification not in profile.parents or not (profile / "cultivation-v2.txt").is_file():
        raise ValueError("Require the existing private natural-cultivation profile")
    if output.parent != (demo / "qa").resolve() or not output.name.startswith("desktop-accept-natural-soak-"):
        raise ValueError("Require the gitignored native soak evidence directory")
    if output.exists() and any(output.iterdir()):
        raise FileExistsError("Never overwrite existing evidence")
    if hashlib.sha256(executable.read_bytes()).hexdigest() != spec["executable_sha256"]:
        raise ValueError("Executable differs from the recorded build")
    if verification not in witness.parents or witness.exists():
        raise FileExistsError("Require a new verification witness record")
    witness.parent.mkdir(parents=True, exist_ok=True)
    record = dict(supervisor_pid=os.getpid(), state="starting", started_utc=utc_now(),
                  executable=str(executable), executable_sha256=spec["executable_sha256"],
                  profile=str(profile), output=str(output), command=command)
    write_record(witness, record)
    process = subprocess.Popen(command, cwd=executable.parent,
                               stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                               stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
    record.update(state="running", process_id=process.pid)
    write_record(witness, record)
    code = process.wait()
    record.update(state="exited", exited_utc=utc_now(), exit_code=code,
                  exit_code_unsigned=code & 0xFFFFFFFF)
    # A native exit code alone never proves gameplay or long-soak success.
    for name in ("natural-soak-terminal-failure.json", "natural-soak.json"):
        try:
            result = json.loads((output / name).read_text(encoding="utf-8-sig"))
        except (OSError, ValueError):
            continue
        record.update(report=name, report_passed=result.get("passed"),
                      report_running=result.get("running"))
        break
    write_record(witness, record)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
