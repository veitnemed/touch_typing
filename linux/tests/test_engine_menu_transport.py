"""Use an owned subprocess to test reconnect semantics without a GUI."""
import os
from pathlib import Path
import signal
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

from platform_linux import Engine, EngineError


@unittest.skipUnless(sys.platform.startswith("linux"), "Linux process signals")
class MenuTransportTests(unittest.TestCase):
    def test_timeout_is_bounded_and_does_not_repeat_a_start(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            launcher = root / "engine"
            log = root / "requests"
            launcher.write_text(
                "#!" + sys.executable + "\n"
                "import json, os, signal, sys\n"
                "for line in sys.stdin:\n"
                "    command = json.loads(line)['command']\n"
                "    with open(" + repr(str(log)) + ", 'a') as audit: print(command, file=audit)\n"
                "    if command == 'lab-start': os.kill(os.getpid(), signal.SIGSTOP)\n"
                "    print(json.dumps({'ok': True, 'value': {'command': command}}), flush=True)\n",
                encoding="utf-8",
            )
            launcher.chmod(0o700)
            with patch.dict(os.environ, {"TOUCH_TYPING_ENGINE": str(launcher)}):
                engine = Engine()
                try:
                    self.assertEqual(engine.call("configure"), {"command": "configure"})
                    engine.menu_timeout = 0.4
                    start = time.monotonic()
                    with self.assertRaisesRegex(EngineError, "не ответил"):
                        engine.call("lab-start")
                    self.assertLess(time.monotonic() - start, 2)
                    self.assertTrue(engine.disconnected)
                    self.assertIsNone(engine.process.poll(), "simulates a live, unresponsive process")
                    with self.assertRaises(EngineError):
                        engine.call("lab-start")
                finally:
                    engine.close()
                self.assertIsNotNone(engine.process.poll())
                replacement = Engine()
                try:
                    self.assertEqual(replacement.call("configure"), {"command": "configure"})
                finally:
                    replacement.close()
            self.assertEqual(log.read_text().splitlines(), ["configure", "lab-start", "configure"])


if __name__ == "__main__":
    unittest.main()
