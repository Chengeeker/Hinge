"""Sync Hinge's vendored navigation AGSL from a local RedNote checkout."""
import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--check", action="store_true")
parser.add_argument("--rednote", type=Path, default=ROOT.parent / "RedNote")
args = parser.parse_args()
source = args.rednote / "app/src/main/assets/glass/liquid_glass.agsl"
target = ROOT / "app/src/main/assets/glass/live_glass.agsl"
expected = source.read_bytes()
if args.check:
    if target.read_bytes() != expected:
        parser.exit(1, "Vendored AGSL differs from the local RedNote source.\n")
else:
    target.write_bytes(expected)
print("Verified RedNote navigation AGSL")
