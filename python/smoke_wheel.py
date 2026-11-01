"""Installed-wheel smoke: bundled host discovery with no SDK, CLI or network use."""
import json
import os
from pathlib import Path
import tempfile
from unittest.mock import patch
import packata

assert not os.environ.get("PACKATA_HOST"), "Smoke must exercise the bundled host"
host = Path(packata.__file__).parent / "_host" / ("packata.exe" if os.name == "nt" else "packata")
assert host.is_file()
assert os.access(host, os.X_OK)
with tempfile.TemporaryDirectory() as directory:
    fixture = Path(directory) / "datapackage.json"
    fixture.write_text(json.dumps(dict(name="fixture", resources=[dict(name="customers", data=[dict(id=i) for i in range(400)],
        schema=dict(fields=[dict(name="id", type="integer")]))])))
    # Keep only the private host location; runtime lookup cannot fall back to an SDK.
    with patch.dict(os.environ, {"PATH": "", "DOTNET_ROOT": directory, "DOTNET_MULTILEVEL_LOOKUP": "0",
                                 "HTTP_PROXY": "http://127.0.0.1:1", "HTTPS_PROXY": "http://127.0.0.1:1"}):
        with packata.open(fixture) as contract:
            with contract.asset("customers").batches() as batches:
                first = next(batches)
                assert 0 < first.num_rows < 400
            with contract.asset("customers").rows() as rows:
                assert sum(1 for row in rows if row["id"] >= 0) == 400
print("Installed wheel opened a local contract and reaped its private hosts without PATH/.NET/runtime downloads")
