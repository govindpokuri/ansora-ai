"""Small source-hygiene gate; also run Gitleaks before publishing releases."""
from pathlib import Path
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
try:
    tracked = subprocess.check_output(['git', 'ls-files', '-z'], cwd=root).decode().split('\0')
    paths = [root / name for name in tracked if name]
except (OSError, subprocess.CalledProcessError):
    paths = []
if not paths:
    paths = [p for p in root.rglob('*') if p.is_file() and not any(
        part in {'.git', 'bin', 'obj', 'storage', 'vendor', 'node_modules', 'models', 'artifacts'}
        for part in p.relative_to(root).parts)]

failures = []
for p in paths:
    name = p.name.lower()
    if (name.startswith('.env') and name != '.env.example') or name in {
        'config.local.php', 'test-connection.json', 'commercial.mode'} or p.suffix in {
        '.dpapi', '.pfx', '.p12', '.pem', '.key', '.exe', '.zip', '.rar', '.sqlite', '.db'}:
        failures.append(f'{p.relative_to(root)}: private/generated file must not be tracked')
        continue
    if p.suffix in {'.png', '.ico', '.gif'}:
        continue
    try:
        text = p.read_text(encoding='utf-8-sig')
    except UnicodeError:
        failures.append(f'{p.relative_to(root)}: unexpected binary source file')
        continue
    # Report only location/type, never the matched credential.
    for pattern, kind in [
        (r'SG\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}', 'SendGrid key'),
        (r'gh[pousr]_[A-Za-z0-9]{30,}', 'GitHub credential'),
        (r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----', 'private key'),
    ]:
        if re.search(pattern, text):
            failures.append(f'{p.relative_to(root)}: possible {kind}')
if failures:
    print('\n'.join(failures))
    sys.exit(1)
print(f'Source hygiene passed for {len(paths)} files. Run Gitleaks for broader secret detection.')
