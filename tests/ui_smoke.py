"""Run the published application on a private X server and close it via WM_DELETE_WINDOW."""
import ctypes as C
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time

root = Path(__file__).resolve().parents[1]
x = C.CDLL('libX11.so.6')
x.XOpenDisplay.argtypes = [C.c_char_p]; x.XOpenDisplay.restype = C.c_void_p
x.XInternAtom.argtypes = [C.c_void_p, C.c_char_p, C.c_int]; x.XInternAtom.restype = C.c_ulong
x.XSendEvent.argtypes = [C.c_void_p, C.c_ulong, C.c_int, C.c_long, C.c_void_p]
x.XFlush.argtypes = [C.c_void_p]
x.XCloseDisplay.argtypes = [C.c_void_p]
class ClientData(C.Union):
    _fields_ = [('b', C.c_char * 20), ('s', C.c_short * 10), ('l', C.c_long * 5)]
class Client(C.Structure):
    _fields_ = [('type', C.c_int), ('serial', C.c_ulong), ('send_event', C.c_int), ('display', C.c_void_p), ('window', C.c_ulong), ('message_type', C.c_ulong), ('format', C.c_int), ('data', ClientData)]
class Event(C.Union):
    _fields_ = [('client', Client), ('pad', C.c_long * 24)]
with tempfile.TemporaryDirectory(prefix='bigbluefish-ui-') as temp:
    server = subprocess.Popen(['Xvfb', '-displayfd', '1', '-screen', '0', '1280x800x24', '-nolisten', 'tcp'], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    app = None
    display = None
    try:
        number = server.stdout.readline().strip()
        assert number.isdigit(), 'Xvfb failed to start'
        env = dict(os.environ, DISPLAY=':' + number, BIGBLUEFISH_DATA=temp)
        with open(Path(temp) / 'app.log', 'w+') as log:
            app = subprocess.Popen([os.environ.get('BIGBLUEFISH_EXECUTABLE', str(root / 'build/app/bigbluefish'))], env=env, stdout=log, stderr=log)
            def windows():
                if app.poll() is not None:
                    log.seek(0)
                    raise AssertionError('Application exited early: ' + log.read())
                return subprocess.check_output(['xwininfo', '-root', '-tree'], env=env).decode()
            deadline = time.monotonic() + 20
            window = None
            while time.monotonic() < deadline:
                match = re.search(r'(0x[0-9a-f]+) "蓝色大肥鱼"', windows())
                if match:
                    window = int(match[1], 16)
                    break
                time.sleep(.1)
            assert window, 'Pet window missing'
            time.sleep(3)
            tree = windows()
            bubble = re.search(r'"蓝色大肥鱼 · 对话"[^\n]*? (\d+)x(\d+)', tree)
            assert bubble and int(bubble[1]) > 150, 'Greeting text did not receive a usable layout width'
            out = Path(os.environ.get('BIGBLUEFISH_ARTIFACT_DIR', str(root / 'build'))) / 'ui-smoke.png'
            out.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run(['import', '-display', ':' + number, '-window', 'root', str(out)], check=True)
            # Send the standard WM close message. The pet must play its farewell and exit.
            display = x.XOpenDisplay((':' + number).encode())
            event = Event()
            event.client = Client(type=33, display=display, window=window, message_type=x.XInternAtom(display, b'WM_PROTOCOLS', 0), format=32)
            event.client.data.l[0] = x.XInternAtom(display, b'WM_DELETE_WINDOW', 0)
            x.XSendEvent(display, window, 0, 0, C.byref(event)); x.XFlush(display)
            time.sleep(.3)
            # Clicking the character during farewell must not cancel shutdown.
            xt = C.CDLL('libXtst.so.6')
            xt.XTestFakeMotionEvent.argtypes = [C.c_void_p, C.c_int, C.c_int, C.c_int, C.c_ulong]
            xt.XTestFakeButtonEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
            geometry = subprocess.check_output(['xwininfo', '-id', hex(window)], env=env).decode()
            def dimension(label):
                return int(re.search(label + r':\s*(-?\d+)', geometry)[1])
            px = dimension('Absolute upper-left X') + dimension('Width') // 2
            py = dimension('Absolute upper-left Y') + dimension('Height') * 3 // 4
            xt.XTestFakeMotionEvent(display, -1, px, py, 0)
            xt.XTestFakeButtonEvent(display, 1, 1, 0); xt.XTestFakeButtonEvent(display, 1, 0, 0)
            x.XFlush(display)
            assert app.wait(timeout=20) == 0, 'Application close failed'
            log.seek(0)
            errors = log.read()
            assert not errors.strip(), errors
            assert (Path(temp) / 'preferences.json').is_file(), 'Settings not persisted'
            print('PASS: window, greeting, clean farewell, persisted settings; screenshot build/ui-smoke.png')
    finally:
        if app and app.poll() is None:
            app.terminate(); app.wait(timeout=5)
        if display: x.XCloseDisplay(display)
        server.terminate(); server.wait(timeout=5)
