"""Build a release-only platform wheel around an already published self-contained host."""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import zipfile


def wheel_version(value):
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", value):
        raise ValueError("Release version must have a supported PEP 440 equivalent")
    match = re.fullmatch(r"(\d+\.\d+\.\d+)-(alpha|beta|rc)[.-]?(\d+)", value)
    if match:
        return match[1] + {"alpha": "a", "beta": "b", "rc": "rc"}[match[2]] + match[3]
    if "-" in value:
        stable, prerelease = value.split("-", 1)
        return stable + ".dev0+" + re.sub(r"[-.]+", ".", prerelease.lower()).strip(".")
    return value


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument("--platform", choices=["win_amd64", "manylinux_2_35_x86_64"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--assets", type=Path, required=True, help="Host obj/project.assets.json from the publish")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    version = wheel_version(args.version)
    executable = "packata.exe" if args.platform == "win_amd64" else "packata"
    if not (args.host / executable).is_file() or not (args.host / "System.Private.CoreLib.dll").is_file():
        raise ValueError("Expected an ordinary self-contained publish including the runtime")
    staging = root / "build" / "wheel-source"
    staging.resolve().relative_to(root.resolve())
    if staging.exists():
        shutil.rmtree(staging)
    staging.mkdir(parents=True)
    shutil.copytree(root / "src" / "packata", staging / "src" / "packata", ignore=shutil.ignore_patterns("__pycache__", "_host"))
    host = staging / "src" / "packata" / "_host"
    shutil.copytree(args.host, host, ignore=shutil.ignore_patterns("*.pdb"))
    # NuGet runtime notices are not copied to the publish directory automatically.
    # Preserve supplied license/notice texts for every resolved third-party package.
    import json
    assets = json.loads(args.assets.read_text(encoding="utf-8-sig"))
    rid = "win-x64" if args.platform == "win_amd64" else "linux-x64"
    for dependency in assets["project"]["frameworks"]["net10.0"].get("downloadDependencies", []):
        name = dependency["name"].lower()
        if name.endswith("." + rid) and ".app.runtime." in name:
            runtime_version = dependency["version"].strip("[]").split(",")[0].strip()
            relative = name + "/" + runtime_version
            location = next((Path(folder) / relative for folder in assets["packageFolders"]
                             if (Path(folder) / relative).is_dir()), None)
            if location is None: raise ValueError(f"Missing runtime metadata {name}")
            assets["libraries"][name + "/" + runtime_version] = dict(type="package", path=relative,
                files=[item.name for item in location.iterdir() if item.is_file()])
    notices = host / "ThirdPartyNotices"
    notices.mkdir()
    identities = []
    notice_paths = {}
    for identity, library in assets["libraries"].items():
        if library["type"] != "package": continue
        identities.append(identity)
        notice_directory = f"p{len(identities):04d}"
        notice_paths[identity] = notice_directory
        location = next((Path(folder) / library["path"] for folder in assets["packageFolders"]
                         if (Path(folder) / library["path"]).is_dir()), None)
        if location is None: raise ValueError(f"Missing resolved package {identity}")
        for name in library.get("files", []):
            if "/" not in name and (re.match(r"(?i)(license|copying|notice|third.party)", name) or name.endswith(".nuspec")):
                destination = notices / notice_directory / ("package.nuspec" if name.endswith(".nuspec") else name)
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy(location / name, destination)
    (notices / "PACKAGES.txt").write_text("Resolved NuGet dependencies (license expressions and source URLs in accompanying nuspecs):\n" + "\n".join(sorted(identities)), encoding="utf-8")
    (notices / "PACKAGES.json").write_text(json.dumps(notice_paths, indent=2), encoding="utf-8")
    if not any("microsoft.netcore.app.runtime" in item.lower() for item in identities):
        raise ValueError("Runtime package metadata is missing")
    if os.name != "nt":
        (host / executable).chmod(0o755)
    shutil.copy(root.parent / "LICENSE", staging / "LICENSE")
    (staging / "pyproject.toml").write_text((root / "pyproject.toml").read_text().replace('version = "0.0.0"', f'version = "{version}"'))
    # Force a platform wheel even though the Python code has no CPython extension.
    (staging / "setup.py").write_text('from setuptools import setup\nfrom setuptools.dist import Distribution\nclass BinaryDistribution(Distribution):\n def has_ext_modules(self): return True\nsetup(distclass=BinaryDistribution)\n')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    subprocess.run([sys.executable, "setup.py", "bdist_wheel", "--python-tag", "py3", "--plat-name", args.platform,
                    "--dist-dir", str(output)], cwd=staging, check=True)
    # bdist_wheel uses a CPython ABI when has_ext_modules=True; private .NET hosts have no Python ABI.
    wheel = next(output.glob(f"packata-{version}-*.whl"))
    import base64
    import csv
    import hashlib
    import io
    target = output / f"packata-{version}-py3-none-{args.platform}.whl"
    temporary = target.with_suffix(".tmp")
    records = []
    with zipfile.ZipFile(wheel) as source, zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as destination:
        record_name = next(name for name in source.namelist() if name.endswith(".dist-info/RECORD"))
        for info in source.infolist():
            if info.filename == record_name: continue
            data = source.read(info)
            if info.filename.endswith(".dist-info/WHEEL"):
                data = re.sub(rb"Tag: .*", f"Tag: py3-none-{args.platform}".encode(), data)
            if info.filename.endswith("/_host/packata"):
                info.create_system = 3
                info.external_attr = 0o100755 << 16
            destination.writestr(info, data)
            digest = base64.urlsafe_b64encode(hashlib.sha256(data).digest()).rstrip(b"=").decode()
            records.append((info.filename, "sha256=" + digest, len(data)))
        rows = io.StringIO(newline="")
        csv.writer(rows).writerows(records + [(record_name, "", "")])
        destination.writestr(record_name, rows.getvalue())
    wheel.unlink()
    temporary.replace(target)
    with zipfile.ZipFile(target) as archive:
        names = archive.namelist()
        assert any(name.endswith("/_host/" + executable) for name in names)
        assert any(name.endswith("/LICENSE") for name in names)
        assert any("ThirdPartyNotices" in name for name in names), "Self-contained runtime notices missing"
    print(f"{target.name}: {target.stat().st_size} bytes")


if __name__ == "__main__":
    main()
