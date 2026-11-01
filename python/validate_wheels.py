"""Validate the immutable wheel set before release; no installation or downloads."""

import argparse
import base64
import csv
from email.parser import BytesParser
import hashlib
import io
import json
from pathlib import Path
import zipfile
from build_wheel import wheel_version


def validate(path, version, platform):
    assert path.name == f"packata-{version}-py3-none-{platform}.whl", path
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        assert len(names) == len(set(names)), "Duplicate wheel members"
        assert all(
            not name.startswith("/") and ".." not in name.split("/") for name in names
        )
        records = list(
            csv.reader(
                io.StringIO(
                    archive.read(f"packata-{version}.dist-info/RECORD").decode()
                )
            )
        )
        assert {row[0] for row in records} == set(names), "Incomplete RECORD"
        assert len(records) == len(names)
        for name, digest, size in records:
            if name.endswith(".dist-info/RECORD"):
                continue
            data = archive.read(name)
            expected = (
                "sha256="
                + base64.urlsafe_b64encode(hashlib.sha256(data).digest())
                .rstrip(b"=")
                .decode()
            )
            assert digest == expected and int(size) == len(data), name
        info = archive.read(f"packata-{version}.dist-info/WHEEL").decode()
        assert f"Tag: py3-none-{platform}" in info and "Root-Is-Purelib: false" in info
        metadata = BytesParser().parsebytes(
            archive.read(f"packata-{version}.dist-info/METADATA")
        )
        assert metadata["Name"] == "packata" and metadata["Version"] == version
        assert any(
            requirement.startswith("pyarrow")
            for requirement in metadata.get_all("Requires-Dist")
        )
        executable = "packata.exe" if platform == "win_amd64" else "packata"
        host_name = next(
            name
            for name in names
            if name.endswith("/packata/_host/" + executable)
            or name == "packata/_host/" + executable
        )
        if platform != "win_amd64":
            assert archive.getinfo(host_name).external_attr >> 16 & 0o111, (
                "Host is not executable"
            )
        assert any(name.endswith("/_host/System.Private.CoreLib.dll") for name in names)
        assert any(name.endswith("/LICENSE") for name in names)
        manifest_name = next(
            name for name in names if name.endswith("/ThirdPartyNotices/PACKAGES.json")
        )
        notices = json.loads(archive.read(manifest_name))
        runtime_directory = next(
            directory
            for identity, directory in notices.items()
            if identity.startswith("microsoft.netcore.app.runtime.")
        )
        assert any(
            "/ThirdPartyNotices/" + runtime_directory + "/" in name
            and "NOTICES" in name.upper()
            for name in names
        )
    print(f"Validated {path.name}: {path.stat().st_size} bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument(
        "--platform", action="append", choices=["win_amd64", "manylinux_2_35_x86_64"]
    )
    args = parser.parse_args()
    version = wheel_version(args.version)
    platforms = args.platform or ["win_amd64", "manylinux_2_35_x86_64"]
    wheels = list(args.directory.glob("packata-*.whl"))
    assert len(wheels) == len(platforms), "Missing or duplicate platform wheels"
    for platform in platforms:
        validate(
            args.directory / f"packata-{version}-py3-none-{platform}.whl",
            version,
            platform,
        )
