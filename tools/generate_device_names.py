"""Generate offline name buckets from Google's supported_devices.csv snapshot.
Source: https://storage.googleapis.com/play_public/supported_devices.csv
Run: python tools/generate_device_names.py path/to/supported_devices.csv
"""
import csv
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
from pathlib import Path
import sys

source = Path(sys.argv[1]).read_bytes()
rows = csv.reader(io.StringIO(source.decode("utf-16" if source.startswith(b"\xff\xfe") else "utf-8-sig")))
assert next(rows) == ["Retail Branding", "Marketing Name", "Device", "Model"]
buckets = {format(index, "x"): set() for index in range(16)}
brands = set()
for brand, name, device, model in rows:
    brand, name, device, model = (value.strip().replace("\\'", "'") for value in (brand, name, device, model))
    if not brand or not name or not model or any(char in name for char in "\ufffd\t\r\n"):
        continue
    fields = (brand.lower(), model.lower(), device.lower(), name)
    if any("\t" in value or "\n" in value or "\r" in value for value in fields):
        continue
    bucket = hashlib.sha256(fields[0].encode()).hexdigest()[0]
    buckets[bucket].add("\t".join(fields))
    brands.add(fields[0])
assert len(brands) > 1000 and sum(map(len, buckets.values())) > 40000
output = Path(__file__).resolve().parents[1] / "app/src/main/assets/device-names"
output.mkdir(parents=True, exist_ok=True)
for key, entries in buckets.items():
    (output / (key + ".tsv")).write_text("\n".join(sorted(entries)) + "\n", encoding="utf-8")
metadata = dict(source="https://storage.googleapis.com/play_public/supported_devices.csv",
    snapshotDate=datetime.now(timezone(timedelta(hours=8))).date().isoformat(), sourceSha256=hashlib.sha256(source).hexdigest(),
    brands=len(brands), records=sum(map(len, buckets.values())))
(output / "source.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
print(json.dumps(metadata))
