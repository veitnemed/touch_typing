"""Visible VS Code Explorer backed by the same virtual paths and journal as keyboard actions."""
from lab_notebook import NB_EDITOR, NB_CANVAS, NB_ERROR, NB_FG, NB_MUTED, NB_SELECTION, NB_BLUE, contains

EXPLORER_PREFIX = "explorer:"
EXPLORER_MORE = "explorer-ui:menu"


class ExplorerSurface:
    def setup_explorer_surface(self):
        self.explorer_command = None
        self._explorer_menu = False
        self._explorer_menu_index = self._explorer_menu_top = 0
        self._explorer_choices = []
        self._explorer_selected_path = ""
        self.start_explorer_frame()

    def start_explorer_frame(self):
        self._explorer_menu_visible = self._explorer_focused = self._explorer_can_open_menu = False
        self._explorer_menu_bounds = (0,0,0,0)

    def explorer_menu_wheel(self, dx, dy):
        point = self._notebook_pointer_pos
        return (bool(dy) and point is not None and self._explorer_menu_visible and
                contains(self._explorer_menu_bounds,*point) and self.explorer_viewport_key("Up" if dy<0 else "Down", False))

    def explorer_hotspot(self, action, path):
        if action == EXPLORER_MORE:
            self._explorer_menu = not self._explorer_menu
            self._explorer_menu_index = self._explorer_menu_top = 0
            self._notebook_menu = ""
            self.queue_draw()
            return True
        if self.explorer_command and action.startswith(EXPLORER_PREFIX):
            self._explorer_menu = False
            self.explorer_command(action[len(EXPLORER_PREFIX):],path)
            self.queue_draw()
            return True
        return False

    def explorer_viewport_key(self,key,shift):
        if key=="F10" and shift and self._explorer_focused and self._explorer_can_open_menu:
            self._explorer_menu = True
            self._explorer_menu_index = self._explorer_menu_top = 0
            self._notebook_menu = ""
            self.queue_draw()
            return True
        if not (self._explorer_menu and self._explorer_menu_visible):
            return False
        if key=="Esc":
            self._explorer_menu = False
            self.queue_draw()
            return True
        if key in ("Up","Down"):
            delta = -1 if key=="Up" else 1
            index = self._explorer_menu_index + delta
            while 0 <= index < len(self._explorer_choices):
                if self._explorer_choices[index][2]:
                    self._explorer_menu_index = index
                    self.queue_draw()
                    break
                index += delta
            return True
        if key=="Enter" and self.explorer_command and self._explorer_choices:
            cmd,_name,enabled = self._explorer_choices[self._explorer_menu_index]
            if enabled:
                self._explorer_menu = False
                self.explorer_command(cmd,self._explorer_selected_path)
                self.queue_draw()
            return True
        return False

    @staticmethod
    def explorer_description(window):
        suffix = window.get("status","")
        if window.get("focus")=="file-name":
            label = "Новая папка" if (window.get("renamePath") or "").endswith("/") else "Имя файла"
            return f"{label}. Введено: {window.get('input','')}. {suffix}"
        rows, index = window.get("explorerEntries",()),window.get("item",-1)
        selected = ("Выбрано: "+rows[index]["title"]+". ") if 0<=index<len(rows) else ""
        return f"Проводник {window.get('projectName','')}. {selected}{suffix} Ctrl+P — поиск файла; Shift+F10 — действия."

    def working_explorer(self, window,x,y,w,h):
        focus = window.get("focus")
        name_field = focus=="file-name"
        folder_name = name_field and (window.get("renamePath") or "").endswith("/")
        tree = focus=="explorer" or name_field
        can_act = bool(self.explorer_command and window.get("active") and self.notebook_interaction_clear() and
                       (focus=="body" or tree))
        self._explorer_focused = tree and window.get("active")
        self.snapshot.push_clip(self.rect(x+1,y+31,max(1,w-2),max(1,h-50)))
        try:
            self.label("⌄  "+window.get("projectName","ПРОЕКТ").upper(),x+8,y+35,10,NB_FG,
                       bold=True,width=w-(69 if can_act else 13))
            if can_act:
                if name_field:
                    self._explorer_button(x+w-31,y+29,24,"×","cancel","Отменить ввод имени (Esc)")
                else:
                    self._explorer_button(x+w-58,y+29,25,"▱+","new-folder","Создать папку")
                    if h>=137:
                        self._explorer_button(x+w-30,y+29,25,"···",EXPLORER_MORE,"Действия проводника · Shift+F10")
                        self._explorer_can_open_menu = True
            row = y+55
            if folder_name:
                self.label("▱",x+8,row+3,12,"#d3ae78",width=19)
                self.fill(NB_BLUE,x+28,row+1,max(1,w-34),21,2)
                self.fill(NB_CANVAS,x+29,row+2,max(1,w-36),19,2)
                self.field(window.get("input",""),window,x+32,row+3,max(1,w-41),12,NB_FG,
                           mono=False,caret_color=NB_BLUE)
                row += 25
            status = window.get("status","") if tree else ""
            painted_status = bool(status and (name_field or any(s in status.lower() for s in ("существует","конфликт","отмен", "уже"))))
            limit = y+h-(65 if painted_status else 20)
            selected = window.get("item",-1) if tree else -1
            entries = window.get("explorerEntries",())
            current = window.get("url","")
            chosen_path = entries[selected]["path"] if 0 <= selected < len(entries) else ""
            if self._explorer_focused:
                self._explorer_selected_path = chosen_path
            expanded = set(window.get("expandedFolders",()))
            root = window.get("projectRootDepth",3)
            count = max(1,int((limit-row)/22))
            first = max(0, selected-count+1)
            for i, entry in enumerate(entries[first:first+count],first):
                if row>=limit:
                    break
                path, caption = entry.get("path",""),entry.get("title","")
                folder = entry.get("kind")=="folder"
                left = x+8+12*min(6,max(0,path.count("/")-root-1))
                chosen = i==selected or (not tree and path==current)
                if chosen:
                    self.fill(NB_SELECTION if tree else "#2d3239",x+1,row,w-3,22)
                if folder:
                    self.label("⌄" if path in expanded else "›",left,row+2,12,NB_MUTED,width=12)
                    self.label("▱",left+12,row+2,12,"#d3ae78",width=14)
                    left += 11
                elif caption.lower().endswith((".py",".ipynb")):
                    self.code_file_icon(caption,left,row+3)
                else:
                    self.label("▪",left+2,row,13,NB_MUTED,width=14)
                left += 16
                width = max(1,x+w-left-6)
                if i==selected and name_field and not folder_name:
                    self.fill(NB_BLUE,left-2,row+1,width+2,20,2)
                    self.fill(NB_CANVAS,left-1,row+2,width,18,2)
                    self.field(window.get("input",""),window,left+1,row+3,width-3,12,NB_FG,mono=False,caret_color=NB_BLUE)
                else:
                    self.label(caption,left,row+2,12,NB_FG if chosen else NB_MUTED,width=width)
                    if can_act and not name_field:
                        hit = (x+1,row,w-3,22)
                        self._nb_hit(hit,EXPLORER_PREFIX+"select","Выбрать "+caption+" · F2 — имя файла",path=path)
                        self._nb_hit(hit,EXPLORER_PREFIX+"open",
                                     ("Раскрыть или свернуть " if folder else "Открыть ")+caption+
                                     (" (двойной щелчок)" if not folder else ""), clicks=2,path=path)
                        if folder:
                            self._nb_hit((left-27,row,25,22),EXPLORER_PREFIX+"open","Раскрыть или свернуть "+caption,path=path)
                row += 22
            if painted_status:
                self.fill(NB_EDITOR,x+1,limit+1,w-2,41)
                self.label(status,x+8,limit+6,11,NB_ERROR if any(t in status.lower() for t in ("существует", "занято")) else NB_FG,width=w-16)
            if self._explorer_menu and can_act and not name_field:
                self._draw_explorer_menu(window,x+4,y+51,w-8,h-79,chosen_path)
        finally:
            self.snapshot.pop()

    def _explorer_button(self,x,y,w,title,cmd,tip):
        self.label(title,x+4,y+3,13,NB_FG,width=w-4)
        self._nb_hit((x,y,w,23),cmd if cmd==EXPLORER_MORE else EXPLORER_PREFIX+cmd,tip)

    def _draw_explorer_menu(self,window,x,y,w,h,chosen):
        file_selected = any(entry.get("path")==chosen and entry.get("kind")=="file"
                            for entry in window.get("explorerEntries",()))
        options = (
            ("new-folder","Новая папка",True), ("quick-open","Найти файл · Ctrl+P",True),
            ("rename","Переименовать · F2",file_selected), ("cut","Вырезать · Ctrl+X",bool(chosen)),
            ("copy","Копировать · Ctrl+C",bool(chosen)), ("paste","Вставить · Ctrl+V",window.get("explorerCanPaste",False)),
            ("undo","Отменить перенос",window.get("canUndoExplorer",False)),
        )
        visible = min(len(options),max(0,int((h-21)/24)))
        if not visible:
            return
        self._explorer_menu_visible = True
        self._explorer_choices = options
        self._explorer_selected_path = chosen
        if not options[self._explorer_menu_index][2]:
            self._explorer_menu_index = 0
        index = self._explorer_menu_index
        first = self._explorer_menu_top = max(index-visible+1,min(index,self._explorer_menu_top))
        menu_height = 24*visible+21
        self.fill(NB_EDITOR,x,y,w,menu_height,4)
        self._explorer_menu_bounds = (x,y,w,menu_height)
        self._nb_hit(self._explorer_menu_bounds,EXPLORER_MORE,"Закрыть меню проводника")
        for i in range(first,min(len(options),first+visible)):
            cmd,title,enabled = options[i]
            row = y+4+(i-first)*24
            if i==index:
                self.fill(NB_SELECTION,x+2,row,w-4,23,3)
            self.label(title,x+7,row+3,11,NB_FG if enabled else NB_MUTED,width=w-13)
            if enabled:
                self._nb_hit((x,row,w,24),EXPLORER_PREFIX+cmd,title,path=chosen)
        self.label("↑↓   Enter   Esc",x+7,y+menu_height-15,9,NB_MUTED,width=w-14)
