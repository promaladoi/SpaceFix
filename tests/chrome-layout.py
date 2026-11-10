import ctypes as c
from ctypes import wintypes as w
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import subprocess
import sys
from threading import Thread
from time import monotonic, sleep
from urllib.request import urlopen
from uuid import uuid4

sys.path.insert(0, str(Path(__file__).resolve().with_name("webdeps")))
from websocket import create_connection


class Page(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.end_headers()
        self.wfile.write(b"""<!doctype html><title>LayoutPilot Chrome test</title>
            <style>textarea,div{display:block;margin:20px;padding:10px;width:400px;border:1px solid #555;font:20px system-ui}</style>
            <textarea aria-label="Plain text" id="plain"></textarea>
            <div role="textbox" aria-label="Rich text" contenteditable="true" id="rich"></div>
            <div aria-label="Custom editor" contenteditable="true" id="custom"></div>""")

    def log_message(self, *_):
        pass


class Keyboard(c.Structure):
    _fields_ = [("vk", w.WORD), ("scan", w.WORD), ("flags", w.DWORD),
                ("time", w.DWORD), ("extra", c.c_size_t)]


class InputValue(c.Union):
    _fields_ = [("keyboard", Keyboard), ("padding", c.c_byte * 32)]


class Input(c.Structure):
    _fields_ = [("kind", w.DWORD), ("value", InputValue)]


class GuiInfo(c.Structure):
    _fields_ = [("size", w.DWORD), ("flags", w.DWORD), ("active", w.HWND), ("focus", w.HWND),
                ("capture", w.HWND), ("menu", w.HWND), ("move", w.HWND), ("caret", w.HWND), ("rect", w.RECT)]


api = c.WinDLL("user32", use_last_error=True)
api.GetForegroundWindow.restype = w.HWND
api.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
api.GetWindowThreadProcessId.restype = w.DWORD
api.GetKeyboardLayout.argtypes = [w.DWORD]
api.GetKeyboardLayout.restype = c.c_size_t
api.GetGUIThreadInfo.argtypes = [w.DWORD, c.POINTER(GuiInfo)]
api.PostMessageW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM]
api.MapVirtualKeyW.argtypes = [w.UINT, w.UINT]
api.SendInput.argtypes = [w.UINT, c.POINTER(Input), c.c_int]


def wait_until(predicate, seconds=4):
    stop = monotonic() + seconds
    while monotonic() < stop:
        if result := predicate():
            return result
        sleep(.04)
    raise TimeoutError("Expected condition did not arrive")


def focused_control(pid):
    hwnd = api.GetForegroundWindow()
    owner = w.DWORD()
    thread = api.GetWindowThreadProcessId(hwnd, c.byref(owner))
    if owner.value != pid:
        return None
    info = GuiInfo(size=c.sizeof(GuiInfo))
    if api.GetGUIThreadInfo(thread, c.byref(info)):
        return info.focus


def press(pid, key):
    assert focused_control(pid), "Chrome fixture lost focus"
    scan = api.MapVirtualKeyW(key, 0)
    pair = (Input * 2)(
        Input(1, InputValue(keyboard=Keyboard(key, scan, 0, 0, 0x5453544c))),
        Input(1, InputValue(keyboard=Keyboard(key, scan, 2, 0, 0x5453544c))),
    )
    assert api.SendInput(2, pair, c.sizeof(Input)) == 2


def run(port, pid, url):
    def find_page():
        with urlopen(f"http://127.0.0.1:{port}/json/list", timeout=2) as response:
            return next((item for item in json.load(response)
                         if item["type"] == "page" and item["url"] == url), None)
    page = wait_until(find_page, 8)
    ws = create_connection(page["webSocketDebuggerUrl"], suppress_origin=True, timeout=5)
    request_id = 0

    def call(method, params=None):
        nonlocal request_id
        request_id += 1
        ws.send(json.dumps({"id": request_id, "method": method, "params": params or {}}))
        while True:
            message = json.loads(ws.recv())
            if message.get("id") == request_id:
                assert "error" not in message and "exceptionDetails" not in message["result"], message
                return message["result"]

    def evaluate(expression):
        return call("Runtime.evaluate", {"expression": expression, "returnByValue": True})["result"].get("value")

    def type_word(field_id, raw):
        call("Page.bringToFront")
        wait_until(lambda: focused_control(pid), seconds=10)
        evaluate(f"""(() => {{
            const field = document.getElementById('{field_id}');
            if (field instanceof HTMLTextAreaElement) field.value = ''; else field.textContent = '';
            field.focus();
        }})()""")
        wait_until(lambda: evaluate(f"document.hasFocus() && document.activeElement.id === '{field_id}'"))
        control = wait_until(lambda: focused_control(pid), seconds=10)
        assert api.PostMessageW(control, 0x50, 0, 0x04090409)
        wait_until(lambda: api.GetKeyboardLayout(api.GetWindowThreadProcessId(control, None)) & 0xFFFF == 0x0409)
        for letter in raw:
            press(pid, {"[": 0xDB, ",": 0xBC}.get(letter, ord(letter.upper())))
            sleep(.035)
        press(pid, 0x20)

    def read(field_id):
        return evaluate(f"""(() => {{
            const field = document.getElementById('{field_id}');
            return field.value ?? field.textContent;
        }})()""").replace("\xa0", " ")

    wait_until(lambda: evaluate("document.getElementById('plain') !== null"), 8)
    failures = []
    for field in ("plain", "rich", "custom"):
        try:
            type_word(field, "ghbdtn")
            wait_until(lambda: read(field) == "привет ", 2)
            print(f"PASS Chrome {field} exact")
        except TimeoutError:
            failures.append(f"{field} exact: {read(field)!r}")
            if focused_control(pid):
                subprocess.run([Path(__file__).resolve().with_name("ChromeFieldProbe.exe")], check=True,
                               creationflags=subprocess.CREATE_NO_WINDOW)

    for raw, expected in [("[hjvt", "хроме "), ("dct", "все "), ("ghbbtn", "привет "),
                          ("helllo", "hello "), ("hello", "hello "),
                          ("d [hjvt yt hf,jnftn", "в хроме не работает ")]:
        type_word("plain", raw)
        try:
            wait_until(lambda: read("plain") == expected, 2)
            print(f"PASS Chrome {raw} → {expected.strip()}")
        except TimeoutError:
            failures.append(f"{raw}: {read('plain')!r} expected {expected!r}")
    try:
        type_word("plain", "dct")
        wait_until(lambda: read("plain") == "все ", 2)
        press(pid, 0x79)
        wait_until(lambda: read("plain") == "dct ", 2)
        print("PASS Chrome F10 undo")
    except TimeoutError:
        failures.append(f"Undo result: {read('plain')!r}")
    ws.close()
    if failures:
        raise AssertionError(failures)


if __name__ == "__main__":
    profile = Path(__file__).resolve().with_name("chrome-fixture-" + uuid4().hex)
    server = ThreadingHTTPServer(("127.0.0.1", 0), Page)
    Thread(target=server.serve_forever, daemon=True).start()
    url = f"http://127.0.0.1:{server.server_port}/"
    chrome = subprocess.Popen([r"C:\Program Files\Google\Chrome\Application\chrome.exe",
                               "--user-data-dir=" + str(profile), "--no-first-run",
                               "--no-default-browser-check", "--disable-background-networking",
                               "--remote-debugging-port=0", "--app=" + url],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        port_file = profile / "DevToolsActivePort"
        wait_until(port_file.exists, 10)
        run(int(port_file.read_text().splitlines()[0]), chrome.pid, url)
    finally:
        chrome.terminate()
        chrome.wait(timeout=5)
        server.shutdown()
