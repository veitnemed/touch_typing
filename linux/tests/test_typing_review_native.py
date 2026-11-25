"""Corrective review in the extracted, unmodified GTK UI + engine, via native XTest key events.

Isolated Ubuntu Xvfb/profile, normal home startup in two processes. No synthetic
results, injected draft or test-only input hook. Test driver only observes widgets
and focuses named UI controls; navigation, text and confirmation use actual keys.
The process is an instrumentation host for the same production Studio class;
this is not a human or hardware keyboard test. See the separate normal-launch gate.
"""
from collections import Counter
import json
import os
from pathlib import Path
import subprocess
import sys
import time

import gi
gi.require_version("GdkX11", "4.0")
from gi.repository import GdkX11, Gtk
from test_typing_cycles_native import TypingCycles
from test_preview2_native import out, ptr, x


class ReviewWindow(TypingCycles):
    def exercise(self):
        self.focus_window()
        assert self.mode == "home"
        yield from self.chord("Alt+2")
        assert self.mode == "typing"
        checkpoint = out / "review-native-state.json"
        if os.environ["TT_REVIEW_PHASE"] == "continue":
            memo = json.loads(checkpoint.read_text(encoding="utf-8"))
            assert len(self.review_continuations) == 1
            continuation = self.review_continuations[0]
            assert continuation.get_mapped() and f'12/{memo["total"]}' in continuation.get_label()
            yield from self.landmark("review-04-reopened-catalog")
            continuation.grab_focus()
            yield from self.chord("Enter")
            for _ in range(40):
                if self.workout == "typing":
                    break
                yield
            assert self.workout == "typing" and self.state["isReview"]
            assert self.state["attemptId"] == memo["id"] and self.state["progressDone"] == 12
            assert self.state["after"] == memo["after"] and self.state["errors"] == 1
            counts = Counter(memo["counts"])
            while self.workout == "typing":
                char = self.state["after"][0]  # first unit is always an actual target, not a visual separator
                counts[char] += 1
                yield from self.chord(char.upper())
            assert self.result_kind == "typing" and self.correction_button is None
            rank = memo["rank"]
            assert set(counts) == set(rank) and counts[rank[0]] > counts[rank[1]] > counts[rank[2]]
            assert sum(counts.values()) == memo["total"]
            yield from self.landmark("review-05-finished-once")
            self.result_default.grab_focus()
            yield from self.chord("Space")
            assert self.mode == "typing" and not self.review_continuations
            # Repeat opening the SOURCE result now shows no new exercise.
            historic = self.call("typing-history-result", sourceId=memo["id"])
            assert not historic["state"]["reviewAvailable"]
            memo["observed"] = counts
            checkpoint.write_text(json.dumps(memo, ensure_ascii=False), encoding="utf-8")
            print("PASS: actual GTK finished one review, accepted exact frequencies", counts,
                  "prior per-source mistakes", memo["mistakes"], flush=True)
            return

        self.typing_rows["WordsEN"].grab_focus()
        yield from self.chord("Enter")
        for _ in range(45):
            if self.workout == "typing":
                break
            yield
        assert self.state["mode"] == "WordsEN" and self.state["progressDone"] == 0
        rank, mistakes = [], {}
        while self.workout == "typing" and len(rank) < 3:
            c = self.state["after"][0]
            if c != " " and c not in rank:
                misses = [15, 8, 5][len(rank)]
                for _ in range(misses):
                    yield from self.chord("1")
                rank.append(c)
                mistakes[c] = misses
            yield from self.chord("Space" if c == " " else c.upper())
        assert len(rank) == 3 and self.state["errors"] == sum(mistakes.values())
        total = self.state["progressTotal"]
        # Real individual XTest presses, not a paste. The driver understands only lowercase US words.
        if self.state["progressDone"] < total - 1:
            yield from self.type_words(total - self.state["progressDone"] - 1)
        final_char = self.state["after"][0]
        yield from self.chord("Space" if final_char == " " else final_char.upper())
        for _ in range(80):
            if self.result_kind == "typing":
                break
            yield
        assert self.result_kind == "typing" and self.correction_button.get_mapped()
        yield from self.landmark("review-01-source-result")
        self.correction_button.grab_focus()
        yield from self.chord("Enter")
        for _ in range(60):
            if self.workout == "typing":
                break
            yield
        assert self.workout == "typing" and self.state["isReview"]
        assert 150 <= self.state["progressTotal"] <= 200 and "Группа" in self.typing_tag.get_label()
        yield from self.landmark("review-02-generated-drill")
        counts = Counter()
        yield from self.chord("1")
        for i in range(12):
            c = self.state["after"][0]
            assert c in rank
            counts[c] += 1
            yield from self.chord(c.upper())
        assert self.state["progressDone"] == 12 and self.state["errors"] == 1
        memo = dict(id=self.state["attemptId"], total=self.state["progressTotal"], after=self.state["after"],
                    counts=counts, rank=rank, mistakes=mistakes)
        checkpoint.write_text(json.dumps(memo, ensure_ascii=False), encoding="utf-8")
        yield from self.landmark("review-03-before-restart")
        self.window.close()  # same window-manager close signal as the title-bar button (Xvfb has no WM)
        assert self.close_dialog is not None
        for _ in range(50):
            if self.close_dialog.get_mapped() and self.close_dialog.get_surface():
                break
            yield
        # Xvfb has no window manager to transfer keyboard focus to this new
        # top-level dialog. Target the owned transient, never unrelated windows.
        xid = GdkX11.X11Surface.get_xid(self.close_dialog.get_surface())
        x.XSetInputFocus(ptr, xid, 1, 0)
        x.XFlush(ptr)
        self.close_dialog.get_widget_for_response(Gtk.ResponseType.ACCEPT).grab_focus()
        yield from self.chord("Space")
        for _ in range(50):
            if self.allow_close:
                break
            yield
        assert self.allow_close
        print("PASS: original errors came from real keys; closed actual correction at 12 units", flush=True)


if __name__ == "__main__":
    if "TT_REVIEW_PHASE" not in os.environ:
        root = Path(os.environ["TOUCH_TYPING_QA_DATA_ROOT"]) / "gtk-review"
        root.mkdir(parents=True, exist_ok=False)
        for phase in ("start", "continue"):
            subprocess.run([sys.executable, __file__], env=dict(os.environ, TT_REVIEW_PHASE=phase,
                           XDG_DATA_HOME=str(root)), check=True)
    else:
        app = ReviewWindow()
        status = app.run([])
        raise SystemExit(1 if app.smoke_failure else status)
