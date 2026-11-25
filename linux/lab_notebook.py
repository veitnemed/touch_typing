"""VS Code workbench surface for the engine-backed teaching notebook."""
from dataclasses import dataclass

from gi.repository import Gdk, Gtk, Pango

NB_CANVAS = "#0d1117"
NB_INSET = "#010409"
NB_EDITOR = "#161b22"
NB_BORDER = "#30363d"
NB_FG = "#e6edf3"
NB_MUTED = "#9da7b3"
NB_BLUE = "#2f81f7"
NB_SELECTION = "#264f78"
NB_ERROR = "#ff9b92"
NB_GREEN = "#7ee787"
NB_LINE = 20
NB_MORE = "notebook-ui:more"
NB_KERNEL = "notebook-ui:kernel"
NB_SCROLL_UP = "notebook-ui:scroll-up"
NB_SCROLL_DOWN = "notebook-ui:scroll-down"
NB_A11Y = ("Блокнот: Enter — редактировать, Esc — выбор ячейки, Ctrl+Enter — выполнить, "
           "Ctrl+Shift+P — команды, PageUp/PageDown — прокрутка.")


def contains(rect, x, y):
    return rect[0] <= x < rect[0] + rect[2] and rect[1] <= y < rect[1] + rect[3]


def overlap(a, b):
    x, y = max(a[0], b[0]), max(a[1], b[1])
    return x, y, max(0, min(a[0]+a[2], b[0]+b[2])-x), max(0, min(a[1]+a[3], b[1]+b[3])-y)


@dataclass
class NotebookViewport:
    stamp: tuple = ()
    vertical: float = 0
    max_vertical: float = 0
    horizontal: float = 0
    max_horizontal: float = 0
    height: float = 0
    bounds: tuple = (0, 0, 0, 0)
    active: bool = False


class NotebookSurface:
    """Workbench drawing, hit-testing and keyboard/wheel scrolling. The engine owns document state."""

    def setup_notebook_surface(self):
        self.notebook_command = None
        self._notebook_hits = []
        self._notebook_views = {}
        self._notebook_view = None
        self._notebook_menu = self._notebook_menu_path = ""
        self._notebook_menu_index = self._notebook_menu_top = 0
        self._notebook_menu_visible = False
        self._notebook_menu_states = []
        self._notebook_menu_cell = -1
        self._notebook_pointer_pos = None
        click = Gtk.GestureClick()
        click.set_button(Gdk.BUTTON_PRIMARY)
        click.connect("pressed", self._on_nb_pointer_press)
        self.add_controller(click)
        motion = Gtk.EventControllerMotion()
        motion.connect("enter", self._on_nb_pointer_move)
        motion.connect("motion", self._on_nb_pointer_move)
        motion.connect("leave", self._on_nb_pointer_leave)
        self.add_controller(motion)
        scroll = Gtk.EventControllerScroll.new(Gtk.EventControllerScrollFlags.BOTH_AXES)
        scroll.connect("scroll", self._on_nb_scroll)
        self.add_controller(scroll)

    def start_notebook_frame(self):
        self._notebook_hits = []
        self._notebook_view = None
        self._notebook_menu_visible = False

    def notebook_interaction_clear(self):
        return not any(self.scene.get(field) for field in ("overview", "switcher", "screenshot", "showDesktop", "desktopOverlay"))

    def _on_nb_pointer_move(self, _controller, x, y):
        self._notebook_pointer_pos = (x, y)
        hit = next((part for part in reversed(self._notebook_hits) if contains(part[0], x, y)), None)
        self.set_cursor_from_name(("text" if hit[1] == "edit" else "pointer") if hit else "default")
        tip = hit[2] if hit else None
        if self.get_tooltip_text() != tip:
            self.set_tooltip_text(tip)

    def _on_nb_pointer_leave(self, *_):
        self._notebook_pointer_pos = None
        self.set_cursor_from_name("default")
        self.set_tooltip_text(None)

    def _on_nb_pointer_press(self, gesture, count, x, y):
        for box, action, _label, cell, clicks, offset, path in reversed(self._notebook_hits):
            if count < clicks or not contains(box, x, y):
                continue
            self.grab_focus()
            if self.explorer_hotspot(action,path):
                gesture.set_state(Gtk.EventSequenceState.CLAIMED)
                return
            self._explorer_menu = False
            if action in (NB_MORE, NB_KERNEL):
                self._notebook_menu = "" if self._notebook_menu == action else action
                self._notebook_menu_index = self._notebook_menu_top = 0
            elif action in (NB_SCROLL_UP, NB_SCROLL_DOWN):
                self.scroll_notebook(-1 if action == NB_SCROLL_UP else 1)
            elif self.notebook_command:
                self._notebook_menu = ""
                self.notebook_command(action, cell, offset(x,y) if offset else -1)
            gesture.set_state(Gtk.EventSequenceState.CLAIMED)
            self.queue_draw()
            return
        if self._notebook_menu or self._explorer_menu:
            self._notebook_menu = ""
            self._explorer_menu = False
            self.queue_draw()

    def _on_nb_scroll(self, controller, dx, dy):
        if self.explorer_menu_wheel(dx,dy):
            return True
        if not self._notebook_view or (self._notebook_pointer_pos is not None and
                not contains(self._notebook_view.bounds, *self._notebook_pointer_pos)):
            return False
        if self._notebook_menu and self._notebook_menu_visible and dy:
            self._nb_move_menu(-1 if dy < 0 else 1)
            return True
        if dx or controller.get_current_event_state() & Gdk.ModifierType.SHIFT_MASK:
            return self.scroll_notebook(dx or dy, horizontal=True)
        return self.scroll_notebook(dy)

    def notebook_viewport_key(self, key, shift=False):
        if self.explorer_viewport_key(key,shift):
            return True
        if key == "F10" and shift and self._notebook_view and self._notebook_view.active and any(
                part[1] == NB_MORE for part in self._notebook_hits):
            self._notebook_menu = NB_MORE
            self._notebook_menu_index = self._notebook_menu_top = 0
            self.queue_draw()
            return True
        if self._notebook_menu and self._notebook_menu_visible:
            if key == "Esc":
                self._notebook_menu = ""
                self.queue_draw()
                return True
            if key in ("Up","Down"):
                self._nb_move_menu(-1 if key=="Up" else 1)
                return True
            if key == "Enter" and self.notebook_command and self._notebook_menu_index < len(self._notebook_menu_states):
                action, _, enabled = self._notebook_menu_states[self._notebook_menu_index]
                if enabled:
                    self._notebook_menu = ""
                    self.notebook_command(action, self._nb_menu_cell(action, self._notebook_menu_cell), -1)
                    self.queue_draw()
                return True
        if key in ("PageUp", "PageDown"):
            return self.scroll_notebook(-1 if key == "PageUp" else 1, horizontal=shift)
        return False

    def _nb_move_menu(self, delta):
        index = self._notebook_menu_index + delta
        while 0 <= index < len(self._notebook_menu_states):
            if self._notebook_menu_states[index][2]:
                self._notebook_menu_index = index
                self.queue_draw()
                return
            index += delta

    def scroll_notebook(self, count, horizontal=False):
        view = self._notebook_view
        if not count or view is None or not view.active or not self.notebook_interaction_clear():
            return False
        limit = view.max_horizontal if horizontal else view.max_vertical
        if limit < .01:
            return False
        attr = "horizontal" if horizontal else "vertical"
        page = 105 if horizontal else max(42, view.height * .76)
        before = getattr(view, attr)
        after = max(0, min(limit, before + count * page))
        if abs(before - after) > .01:
            setattr(view, attr, after)
            self.queue_draw()
        return True

    def _nb_hit(self, box, action, title, cell=-1, clicks=1, offset=None, path=""):
        if not (self.notebook_command or self.explorer_command) or not self.notebook_interaction_clear():
            return
        box = overlap(box, (0, 0, self.get_width(), self.get_height()))
        if min(box[2:]) >= 1:
            self._notebook_hits.append((box, action, title, cell, clicks, offset, path))

    def _nb_paint_tool(self, caption, action, tip, cell, x, y, width, enabled, clip=None):
        if width < 17:
            return
        self.label(caption, x+5, y+3, 12, NB_FG if enabled else NB_MUTED, width=width-7)
        if enabled:
            bounds = (x, y, width, 24)
            self._nb_hit(bounds if clip is None else overlap(bounds, clip), action, tip, cell)

    @staticmethod
    def _nb_width(line):
        return (len(line) + 7 * line.count("\t") + sum(ord(c)>=0x2e80 for c in line)) * 8.45

    def _nb_viewport(self, identity):
        if identity not in self._notebook_views:
            if len(self._notebook_views) >= 20:
                self._notebook_views.pop(next(iter(self._notebook_views)))
            self._notebook_views[identity] = NotebookViewport()
        return self._notebook_views[identity]

    def notebook_page(self, window, nb, path, x, y, w, h, primary):
        if w < 105 or h < 48:
            return
        cells = nb.get("cells", [])
        selected = nb.get("selected", 0)
        if not 0 <= selected < len(cells):
            return
        kernel = nb.get("kernel") or {}
        busy = kernel.get("state") == "busy"
        body = primary and window.get("active") and window.get("focus") == "body"
        interactive = primary and window.get("active") and window.get("focus") in ("body", "explorer") and self.notebook_interaction_clear()
        toolbar_h = 49 if w < 290 else 32
        top, height = y+toolbar_h, max(1, h-toolbar_h)
        pad, gutter = (8, 36) if w < 385 else (17, 43)
        start, end = x+pad+gutter+10, x+w-pad-10
        width = max(14, end-start-1)
        identity = (window.get("id"), path)
        view = self._nb_viewport(identity)
        view.height, view.active = height, body
        view.bounds = (x, top, w, height)
        if interactive:
            self._notebook_view = view
        prepared, stack, longest = [], 8, 0
        for i, c in enumerate(cells):
            source = c.get("source", "").split("\n")
            code = c["kind"] == "code"
            editing = i == selected and nb["editing"]
            output = c.get("traceback") or c.get("output") or ""
            output = output.replace("\r\n", "\n").rstrip("\n").split("\n") if output else []
            if code or editing:
                longest = max(longest, max(map(self._nb_width, source), default=0))
            longest = max(longest, max(map(self._nb_width, output), default=0))
            source_h = 14 + (len(source)*NB_LINE if code or editing else sum(map(self._nb_md_height,source))) + (19 if code else 3)
            output_h = len(output) * NB_LINE + 31 if output else 0
            prepared.append((c, code, editing, source, output, stack, source_h, output_h))
            stack += source_h + output_h + 14
        view.max_vertical, view.max_horizontal = max(0, stack-height), max(0, longest-width+9)
        c, _, editing, lines, _, row, source_height, out_h = prepared[selected]
        stamp = (selected, c.get("id"), nb.get("editing"), nb.get("caret"),
                 c.get("runs"), kernel.get("generation"), body)
        if view.stamp != stamp:
            view.stamp = stamp
            caret_row = caret_column = 0
            if editing and body:
                before = c["source"].encode("utf-16-le")[:max(0, nb["caret"])*2].decode("utf-16-le","ignore")
                caret_row = before.count("\n")
                caret_column = len(before.rsplit("\n", 1)[-1])
            anchor_y = row + (7 + caret_row * NB_LINE if editing and body else 0)
            amount = NB_LINE+16 if editing and body else min(source_height+out_h, height-2)
            if anchor_y < view.vertical:
                view.vertical = anchor_y
            elif anchor_y + amount > view.vertical + height:
                view.vertical = max(0, anchor_y + amount - height)
            if editing and body and caret_column:
                shape = self.text_layout(lines[min(caret_row, len(lines)-1)],14,mono=True)
                edge = len(lines[min(caret_row,len(lines)-1)][:caret_column].encode("utf-8"))
                caret_x = shape.get_cursor_pos(edge)[0].x/Pango.SCALE
                view.max_horizontal = max(view.max_horizontal, caret_x-width+13)
                if caret_x > view.horizontal + width - 12:
                    view.horizontal = caret_x-width+12
                elif caret_x < view.horizontal:
                    view.horizontal = max(0, caret_x-12)
            else:
                view.horizontal = 0
        view.vertical = max(0, min(view.max_vertical, view.vertical))
        view.horizontal = max(0, min(view.max_horizontal, view.horizontal))

        self.fill(NB_CANVAS, x, y, w, h)
        self.snapshot.push_clip(self.rect(x, y, w, h))
        self._nb_toolbar(x, y, w, toolbar_h, h, kernel, nb, selected, interactive)
        clip = (x+1, top, max(1, w-2), height)
        self.snapshot.push_clip(self.rect(*clip))
        for i, (cell, code, editing, source, output, offset, source_h, output_h) in enumerate(prepared):
            row = top + offset - view.vertical
            cell_h = source_h+output_h+14
            if row + cell_h <= top or row >= top + height:
                continue
            current = i == selected
            if interactive and kernel:
                self._nb_hit(overlap((x+pad,row,max(1,w-pad*2-5),cell_h-10),clip),
                             "select", f"Выбрать ячейку {i+1}", i)
                shape_hit = self._nb_offset_picker(source, start, row+8, view.horizontal) if code or editing else None
                self._nb_hit(overlap((start-8,row,width+12,source_h),clip), "edit",
                             f"Редактировать {'код' if code else 'Markdown'} ячейки {i+1}", i, 1 if code else 2,shape_hit)
            caret, anchor = (nb["caret"], nb["anchor"]) if current else (0, 0)
            self._nb_draw_cell(cell, i, current, body, interactive and bool(kernel), busy, code, editing, caret, anchor,
                               clip, x+pad, start, end, row, source, source_h, output, output_h, width, view.horizontal)
            if current and interactive and kernel and not busy and end - (x+pad+gutter) >= 210:
                self._nb_cell_tools(end, row, i, len(cells), code, clip)
        if view.max_vertical > .01:
            track = max(1, height-8)
            thumb = max(18, track*height/stack)
            thumb_y = top+4+max(0, track-thumb)*view.vertical/view.max_vertical
            self.fill("#535b65", x+w-6, thumb_y, 4, min(track, thumb), 2)
            if interactive and view.vertical > .01:
                self._nb_hit((x+w-10, top, 10, max(0,thumb_y-top)), NB_SCROLL_UP, "Прокрутить к предыдущим ячейкам")
            if interactive and view.vertical < view.max_vertical:
                self._nb_hit((x+w-10,thumb_y+thumb,10,max(0,top+height-thumb_y-thumb)), NB_SCROLL_DOWN,
                             "Прокрутить к следующим ячейкам")
        self.snapshot.pop()
        if interactive and kernel and self._notebook_menu:
            if self._notebook_menu_path != identity:
                self._notebook_menu = ""
            else:
                self._nb_menu(x, y, w, h, toolbar_h, kernel, selected, nb)
        if interactive:
            self._notebook_menu_path = identity
        self.snapshot.pop()

    def _nb_offset_picker(self, source, start, top, hscroll):
        def clicked(x,y):
            number = max(0,min(len(source)-1,int((y-top)//NB_LINE)))
            line = source[number].rstrip("\r")
            prefix = sum(self.units(previous)+1 for previous in source[:number])
            dx = x-start+hscroll
            if dx <= 0:
                return prefix
            shape = self.text_layout(line,14,mono=True)
            if dx >= shape.get_size()[0]/Pango.SCALE:
                return prefix+self.units(line)
            _within, byte, trailing = shape.xy_to_index(round(dx*Pango.SCALE),Pango.SCALE)
            before = line.encode("utf-8")[:byte].decode("utf-8","ignore")
            return prefix+self.units(line[:len(before)+trailing])
        return clicked

    def _nb_toolbar(self, x, y, w, h, total_h, kernel, nb, selected, interactive):
        self.fill(NB_CANVAS, x,y,w,h)
        self.fill(NB_BORDER, x,y+h-1,w,1)
        compact, cramped = w < 540, w < 290
        state = kernel.get("state", "idle")
        busy = state == "busy"
        px = x+7
        can_insert = len(nb["cells"])<128
        can_run = any(c["kind"]=="code" for c in nb["cells"])
        if kernel:
            add, md, run = (30,30,30) if compact else (70,111,106)
            self._nb_paint_tool("+ ▤" if compact else "+ Код", "add-below", "Добавить ячейку кода ниже",
                                selected, px,y+3,add,interactive and not busy and can_insert); px += add
            self._nb_paint_tool("+ M" if compact else "+ Markdown", "add-markdown", "Добавить Markdown ниже",
                                selected, px,y+3,md,interactive and not busy and can_insert); px += md
            self.fill(NB_BORDER, px+3,y+8,1,15); px += 9
            caption = ("■" if compact else "■  Прервать") if busy else ("▷▷" if compact else "▷▷  Выполнить всё")
            self._nb_paint_tool(caption, "interrupt" if busy else "run-all",
                                "Прервать выполнение" if busy else "Выполнить все ячейки по порядку",
                                -1, px,y+3,run,interactive and (busy or can_run)); px += run
            if not compact and px+104 < x+w-150:
                self._nb_paint_tool("↻  Перезапуск", "restart", "Перезапустить ядро и сбросить переменные",
                                    -1, px,y+3,101,interactive); px += 101
            if total_h-h >= 54:
                self._nb_paint_tool("···", NB_MORE, "Команды блокнота · Shift+F10", -1, px,y+3,29,interactive)
        name = {"busy":"Выполняется", "interrupted":"Прервано"}.get(state, "Готово")
        status_x = x+13 if cramped else max(px+32, x+w-(106 if compact else 162))
        status_y = y+25 if cramped else y+7
        available = x+w-status_x-9
        if available > 38:
            self.fill("#e3b341" if busy else NB_ERROR if state=="interrupted" else NB_GREEN,
                      status_x,status_y+7,6,6,3)
            label = ("Python 3" if compact else "Python 3  ·  "+name) if kernel else "Учебное ядро Python"
            self.label(label, status_x+11,status_y,11, NB_FG,width=available-9)
            if kernel and interactive and total_h-h >= 54:
                self._nb_hit((status_x-3,status_y-4,available+3,24), NB_KERNEL,
                             kernel.get("label", "Python 3 · учебное ядро")+" — "+name)

    def _nb_cell_tools(self, right, row, cell, total, code, clip):
        unit, top = 26, row-11
        x = right-unit*5-5
        self.fill(NB_INSET,x,top,unit*5+3,23,4)
        for icon, action, tip, enabled in (
            ("↑","move-up","Переместить ячейку выше",cell>0),
            ("↓","move-down","Переместить ячейку ниже",cell+1<total),
            ("⧉","copy-cell","Скопировать ячейку",True),
            ("M" if code else "<>","markdown" if code else "code",
             "Преобразовать ячейку в Markdown" if code else "Преобразовать ячейку в код",True),
            ("×","delete-cell","Удалить выбранную ячейку",True)):
            self._nb_paint_tool(icon,action,tip,cell,x,top,unit,enabled,clip)
            x += unit

    @staticmethod
    def _nb_menu_cell(action, selected):
        return selected if action in ("run-above","run-below","add-above","move-up","move-down","copy-cell",
                                       "duplicate-cell","markdown","code","delete-cell") else -1

    def _nb_menu(self, x, y, w, h, toolbar_h, kernel, selected, nb):
        busy = kernel.get("state") == "busy"
        count = len(nb["cells"])
        code = nb["cells"][selected]["kind"] == "code"
        if self._notebook_menu == NB_KERNEL:
            actions = (("", kernel.get("label","Python 3 · учебное ядро")),("restart","Перезапустить ядро"),("interrupt","Прервать выполнение"))
        else:
            actions = (("run-above","Выполнить ячейки выше"),("run-below","Выполнить ячейки ниже"),
                       ("restart","Перезапустить ядро"),("clear","Очистить вывод"),("add-above","Добавить ячейку выше"),
                       ("paste-cell","Вставить скопированную ячейку"),("move-up","Переместить выше"),
                       ("move-down","Переместить ниже"),("copy-cell","Скопировать ячейку"),("duplicate-cell","Дублировать ячейку"),
                       ("markdown","Сделать Markdown") if code else ("code","Сделать кодом"),
                       ("delete-cell","Удалить ячейку"),("undo","Восстановить / отменить"),("redo","Повторить изменение"))
        panel, top = min(w-14,267), y+toolbar_h
        visible = min(len(actions), max(0,int((h-toolbar_h-24)/22)))
        if visible < 1:
            self._notebook_menu = ""
            return
        def allowed(cmd):
            return (bool(cmd) and (cmd=="interrupt" if busy else cmd!="interrupt")
                    and (cmd!="undo" or nb.get("canUndo")) and (cmd!="redo" or nb.get("canRedo"))
                    and (cmd!="move-up" or selected>0) and (cmd!="move-down" or selected+1<count)
                    and (cmd!="delete-cell" or count>1))
        states = [(cmd, title, allowed(cmd)) for cmd,title in actions]
        self._notebook_menu_visible = True
        self._notebook_menu_states = states
        self._notebook_menu_cell = selected
        if not states[self._notebook_menu_index][2]:
            self._notebook_menu_index = next((i for i,state in enumerate(states) if state[2]),0)
        index = self._notebook_menu_index
        first = self._notebook_menu_top = max(index-visible+1,min(index,self._notebook_menu_top))
        left, menu_h = x+w-panel-7, visible*22+23
        self._nb_hit((left,top,panel,menu_h),self._notebook_menu,"Закрыть меню")
        self.fill(NB_EDITOR,left,top,panel,menu_h,5)
        for i in range(first,min(len(states),first+visible)):
            cmd, title, enabled = states[i]
            row = top+4+(i-first)*22
            if i==index and enabled:
                self.fill(NB_SELECTION,left+3,row,panel-6,21,3)
            self.label(title,left+10,row+2,12,NB_FG if enabled else NB_MUTED,width=panel-18)
            if enabled:
                self._nb_hit((left+4,row,panel-8,22),cmd,title,self._nb_menu_cell(cmd,selected))
        self.label(f"↑↓   Enter   Esc  ·  {index+1}/{len(states)}",left+10,top+menu_h-18,10,NB_MUTED,width=panel-18)

    def _nb_draw_cell(self, cell, index, chosen, body, interactive, busy, code, editing, caret, anchor,
                      clip, left, start, end, row, source, source_h, output, output_h, width, hscroll):
        content_x, content_width = start-7, max(1,end-start+13)
        editing_active = editing and body
        if chosen:
            self.fill(NB_BLUE if body else NB_BORDER,left,row+2,3,source_h+output_h-2,1.5)
        if code or editing:
            self.fill(NB_BLUE if editing_active else NB_BORDER,content_x,row+1,content_width,source_h-1,4)
            self.fill(NB_EDITOR,content_x+1,row+2,max(1,content_width-2),source_h-3,3)
        if code:
            runx = left+9
            self.label("▷",runx,row+4,15,NB_MUTED if busy else NB_FG,width=22)
            if interactive and not busy:
                self._nb_hit(overlap((runx-1,row+2,content_x-runx,26),clip),"run",f"Выполнить ячейку {index+1}",index)
            self.label(f"[{cell.get('runs') or ' '}]",left+8,row+source_h-20,11,NB_MUTED,width=content_x-left-9,mono=True)
        elif editing:
            self.label("M",left+10,row+7,12,NB_MUTED,width=20)
        code_top = row+8
        if not code and not editing:
            ly = code_top
            fenced = False
            for raw in source:
                height = self._nb_md_height(raw)
                if ly+height >= clip[1] and ly < clip[1]+clip[3]:
                    fenced = self._nb_markdown(raw,start,ly,width,fenced)
                elif raw.lstrip().startswith(chr(96)*3):
                    fenced = not fenced
                ly += height
        else:
            skip = min(len(source),max(0,int((clip[1]-code_top-NB_LINE)/NB_LINE)))
            stop = min(len(source),1+max(0,int((clip[1]+clip[3]-code_top)/NB_LINE)))
            offset = sum(self.units(line)+1 for line in source[:skip])
            for i in range(skip,stop):
                line, ly = source[i].rstrip("\r"),code_top+i*NB_LINE
                self.field(line,{"cursor":caret,"anchor":anchor},start,ly,width,14,NB_FG,
                           offset=offset,focused=editing_active,line_break=int(i+1<len(source)),
                           syntax=cell.get("syntax",()),caret_color=NB_BLUE,selection_color=NB_SELECTION,
                           manual_scroll=hscroll)
                offset += self.units(source[i])+1
            self.label("Python" if code else "Markdown",end-85,row+source_h-17,10,NB_MUTED,width=84)
        if output:
            top = row+source_h+5
            failed = cell.get("failed")
            self.fill("#25191d" if failed else "#11161d",content_x,top,content_width,output_h-2,3)
            kind = cell.get("outputLabel") or "Вывод"
            self.label(kind + (" · ошибка" if failed else ""),start,top+3,11,NB_ERROR if failed else NB_MUTED,width=width)
            first = max(0,int((clip[1]-top-39)/NB_LINE))
            for i in range(first,len(output)):
                ly = top+21+i*NB_LINE
                if ly >= clip[1]+clip[3]:
                    break
                self.snapshot.push_clip(self.rect(start,ly,width,NB_LINE))
                self.label(output[i],start-hscroll,ly,13,NB_ERROR if failed else NB_FG,mono=True)
                self.snapshot.pop()

    @staticmethod
    def _nb_md_level(value):
        value = value.lstrip()
        marks = len(value)-len(value.lstrip("#"))
        return marks if 0 < marks <= 6 and value[marks:].startswith(" ") else 0

    @classmethod
    def _nb_md_height(cls, value):
        level = cls._nb_md_level(value)
        return 43 if level==1 else 35 if level==2 else 27 if level else NB_LINE

    def _nb_markdown(self, raw, x,y,w, fenced):
        value = raw.lstrip()
        if value.startswith(chr(96)*3):
            return not fenced
        if fenced:
            self.label(raw,x+7,y,13,"#a5d6ff",width=w-7,mono=True)
        else:
            marks = self._nb_md_level(value)
            if marks:
                self.label(value[marks+1:],x,y-1,31 if marks==1 else 25 if marks==2 else 18,NB_FG,width=w,bold=True)
            elif value.startswith("> "):
                self.fill(NB_BORDER,x,y+2,2,NB_LINE-3)
                self.label(value[2:],x+9,y,13,NB_MUTED,width=w-10)
            else:
                if value.startswith(("- ","* ")):
                    value = "•  "+value[2:]
                self.label(value,x,y,13,NB_FG,width=w)
        return fenced
