"""Headless bridge smoke check for the published Ubuntu binary."""
import os
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
app = root / 'build/app/bigbluefish'

def check(value, label):
    if not value:
        raise SystemExit('FAIL: ' + label)
    print('PASS: ' + label)

with tempfile.TemporaryDirectory(prefix='fish-bridge-probe-') as temporary:
    data = Path(temporary) / 'data'
    env = dict(os.environ, BIGBLUEFISH_DATA=str(data))
    missing = subprocess.run([str(app), '--mcp-bridge'], capture_output=True, text=True, timeout=10, env=env)
    check(missing.returncode == 1 and missing.stdout == '', 'bridge mode rejects a missing pipe without starting the UI')
    no_server = subprocess.run([str(app), '--mcp-bridge', '--pipe', 'BigBlueFish.Test.' + os.urandom(8).hex()], capture_output=True, text=True, timeout=10, env=env)
    check(no_server.returncode == 1 and no_server.stdout == '', 'bridge mode fails cleanly when the pet socket is absent')
    check(not data.exists(), 'bridge mode does not create or touch the application data directory')
