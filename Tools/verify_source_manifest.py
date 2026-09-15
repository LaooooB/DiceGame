"""Verify authored source/assets after transport; ignores generated import/build files."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
manifest = json.loads((ROOT / 'source_manifest.json').read_text(encoding='utf-8'))
errors = []
for entry in manifest['files']:
    path = ROOT / entry['path']
    if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
        errors.append(entry['path'])
if errors:
    raise SystemExit('Source integrity failed: ' + ', '.join(errors))
print(f"Verified {len(manifest['files'])} authored files against SHA256 manifest.")
