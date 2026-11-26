using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView
    {
        private const string ChromeHistoryPrefix = "browser-history:";
        private const string ChromeStrap = "#3D3D3D", ChromeTray = "#272727", ChromePill = "#32332F";
        private const string ChromeText = "#E2E2E2", ChromeIcon = "#C4C7C5", ChromeFocus = "#A7C6FA";
        private const string ChromeHistoryBg = "#252629", ChromeHistoryCard = "#2D2E31", ChromeHistoryMuted = "#BDC1C6";
        private const string ChromeSelection = "#415C84";

        private const string ChromeSurfacePrefix = "browser-surface:";
        internal event Action<string,string,int> ChromeSurfaceCommand;
        internal event Action<string, string> BrowserHistoryCommand;

        private bool OnChromeSurfaceHotspot(NotebookHit hit)
        {
            if (ChromeSurfaceCommand == null || !hit.Action.StartsWith(ChromeSurfacePrefix,StringComparison.Ordinal)) return false;
            ChromeSurfaceCommand(hit.Action.Substring(ChromeSurfacePrefix.Length),hit.Path,hit.Cell);
            return true;
        }

        private void AddChromeSurfaceHit(Data window, Rect target, string action, string label, int slot = -1)
        {
            if (ChromeSurfaceCommand != null && window.B("active") && window.S("id").Length != 0 && window.S("focus") != "tab-search")
                AddNotebookHit(target,ChromeSurfacePrefix+action,label,slot,path:window.S("id"));
        }

        private bool OnBrowserHistoryHotspot(NotebookHit hit)
        {
            if (BrowserHistoryCommand == null || !hit.Action.StartsWith(ChromeHistoryPrefix, StringComparison.Ordinal))
                return false;
            BrowserHistoryCommand(hit.Action.Substring(ChromeHistoryPrefix.Length), hit.Path);
            return true;
        }

        private void DrawChromeMark(string kind, double x, double y, double size, string tint, string background = ChromeStrap)
        {
            double scale = size/16;
            var fill = Ui.Brush(tint);
            var pen = new Pen(fill, 1.5*scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            Func<double,double,Point> position = (px, py) => new Point(x+px*scale, y+py*scale);
            Action<double,double,double,Brush> disk = (px,py,r,brush) => context.DrawEllipse(brush,null,position(px,py),r*scale,r*scale);
            Action<double,double,double> ring = (px,py,r) => {
                disk(px,py,r,fill); disk(px,py,r-1.5,Ui.Brush(background));
            };
            double[][] contours = new double[0][];
            switch (kind)
            {
                case "back": contours = new[] { new double[] {8,3,3,8,8,13}, new double[] {3,8,13,8} }; break;
                case "forward": contours = new[] { new double[] {8,3,13,8,8,13}, new double[] {13,8,3,8} }; break;
                case "down": contours = new[] { new double[] {4,6,8,10,12,6} }; break;
                case "home": contours = new[] { new double[] {2,8,8,3,14,8}, new double[] {4,7,4,14,12,14,12,7},
                    new double[] {7,14,7,10,9,10,9,14} }; break;
                case "min": contours = new[] { new double[] {3,12,13,12} }; break;
                case "max": contours = new[] { new double[] {3,3,13,3,13,13,3,13,3,3} }; break;
                case "restore": contours = new[] { new double[] {7,2,14,2,14,9,11,9}, new double[] {3,6,11,6,11,14,3,14,3,6} }; break;
                case "close": contours = new[] { new double[] {4,4,12,12}, new double[] {12,4,4,12} }; break;
                case "plus": contours = new[] { new double[] {3,8,13,8}, new double[] {8,3,8,13} }; break;
                case "download": contours = new[] { new double[] {8,2,8,10}, new double[] {4,7,8,11,12,7}, new double[] {3,13,13,13} }; break;
                case "search": ring(6.5,6.5,5); contours = new[] { new double[] {10.5,10.5,14,14} }; break;
                case "refresh":
                    ring(8,8,6.3); Rectangle(x+10.6*scale,y+scale,5.4*scale,4.7*scale,background);
                    contours = new[] { new double[] {11,2,14,5,10,5} }; break;
                case "history":
                    ring(8,8,6.3); Rectangle(x,y+scale,5*scale,6*scale,background);
                    contours = new[] { new double[] {8,4.5,8,8,11,9.5}, new double[] {1,3,1,7,5,7} }; break;
                case "globe":
                    ring(8,8,6.3);
                    contours = new[] { new double[] {2.5,8,13.5,8}, new double[] {8,2,6,6,6,10,8,14}, new double[] {8,2,10,6,10,10,8,14} }; break;
                case "chrome":
                    ring(8,8,6.3); ring(8,8,3.7);
                    contours = new[] { new double[] {4.5,4.5,10,4.5}, new double[] {10.5,7.5,8.4,12}, new double[] {6.5,10.1,3.5,5.6} }; break;
                case "sliders":
                    contours = new[] { new double[] {2,5,14,5}, new double[] {2,11,14,11} }; break;
                case "folder": contours = new[] { new double[] {2,5,2,13,14,13,14,5,8,5,6,3,2,3,2,5} }; break;
                case "star": contours = new[] { new double[] {8,1.5,10,6,15,6,11,9,12.5,14,8,11.3,3.5,14,5,9,1,6,6,6,8,1.5} }; break;
                case "more":
                    foreach (double cy in new[] {3.8,8,12.2}) disk(8,cy,1.15,fill);
                    break;
                case "apps":
                    foreach (int ox in new[] {2,9})
                        foreach (int oy in new[] {2,9})
                        {
                            Rounded(x+ox*scale,y+oy*scale,5*scale,5*scale,tint,scale);
                            Rounded(x+(ox+1.3)*scale,y+(oy+1.3)*scale,2.4*scale,2.4*scale,background,.35*scale);
                        }
                    break;
            }
            foreach (var path in contours)
                for (int index = 2; index < path.Length; index += 2)
                    context.DrawLine(pen,position(path[index-2],path[index-1]),position(path[index],path[index+1]));
            if (kind == "sliders")
            {
                disk(6,5,2.3,fill); disk(6,5,1,Ui.Brush(background));
                disk(11,11,2.3,fill); disk(11,11,1,Ui.Brush(background));
            }
        }

        private void DrawChromeSiteMark(string url, double x, double y, double size, string tint, string background)
        {
            string kind = url == "chrome://history" ? "history" : url == "chrome://bookmarks" ? "star" :
                url == "chrome://downloads" ? "download" : "globe";
            DrawChromeMark(kind,x,y,size,tint,background);
        }

        private double ChromeDensity(double width)
        {
            double device = Math.Max(1,VisualTreeHelper.GetDpi(this).DpiScaleX);
            double reference = 1+.25*Math.Max(0,Math.Min(1,(width-1100)/740));
            return Math.Min(reference,Math.Max(1,1.25/device));
        }

        private double DrawChromeTop(Data window, double w, Data[] tabs)
        {
            double unit = w < 710 ? .81 : ChromeDensity(w), tabsHeight = 40*unit, navHeight = 42*unit, booksHeight = 38*unit;
            Rectangle(0,0,w,tabsHeight,ChromeTray);
            if (w >= 240)
            {
                Rounded(6*unit,7*unit,28*unit,28*unit,"#646464",10*unit);
                DrawChromeMark("down",12*unit,13*unit,16*unit,ChromeText,"#646464");
                if (window.B("active") && BrowserTabCommand != null)
                    AddNotebookHit(new Rect(6*unit,7*unit,28*unit,28*unit),
                        ChromeTabsPrefix+"open","Поиск открытых вкладок · Ctrl+Shift+A");
            }
            double left = w >= 240 ? 40*unit : 4, controls = 100*unit;
            double available = Math.Max(42,w-left-controls-25*unit);
            int count = Math.Min(tabs.Length,Math.Max(1,(int)(available/(52*unit))));
            int active = Math.Max(0,Math.Min(tabs.Length-1,window.I("activeTab")));
            int start = Math.Min(Math.Max(0,active-count+1),Math.Max(0,tabs.Length-count));
            double tabWidth = Math.Min(235*unit,available/Math.Max(1,count));
            for (int index = start; index < start+count; index++)
            {
                bool selected = index == active;
                double position = left+(index-start)*tabWidth;
                string tone = selected ? ChromeStrap : "#282828";
                AddChromeSurfaceHit(window,new Rect(position+2*unit,6*unit,Math.Max(1,tabWidth-27*unit),tabsHeight-6*unit),
                    "tab","Открыть вкладку: "+tabs[index].S("title","Новая вкладка"),index);
                if (selected)
                {
                    Rounded(position,6*unit,tabWidth,tabsHeight-6*unit,tone,11*unit);
                    Rectangle(position,tabsHeight-10*unit,tabWidth,10*unit,tone);
                }
                else
                {
                    Rounded(position,7*unit,tabWidth-1,tabsHeight-9*unit,tone,7*unit);
                    if (index+1 != active) Rectangle(position+tabWidth-2,12*unit,1,15*unit,"#202020");
                }
                if (tabWidth >= 68*unit)
                {
                    DrawChromeSiteMark(tabs[index].S("url"),position+9*unit,13*unit,16*unit,"#B7BABC",tone);
                    Text(tabs[index].S("title","Новая вкладка"),position+31*unit,12*unit,14*unit,
                        selected ? "#F2F2F2" : "#BCBCBC",tabWidth-55*unit);
                }
                else DrawChromeSiteMark(tabs[index].S("url"),position+6*unit,13*unit,15*unit,"#B7BABC",tone);
                DrawChromeMark("close",position+tabWidth-19*unit,14*unit,12*unit,selected ? "#E7E7E7" : "#BCBCBE",tone);
            }
            double plus = left+tabWidth*count+4*unit;
            if (plus+28*unit <= w-controls+8*unit)
            {
                Rounded(plus,7*unit,28*unit,28*unit,"#646464",14*unit);
                DrawChromeMark("plus",plus+6*unit,13*unit,16*unit,ChromeText,"#646464");
            }
            bool win10 = scene.S("desktopStyle") == "win10";
            string[] controlsIcons = { "min",win10 && window.S("state") == "maximized" ? "restore" : "max","close" };
            for (int index = 0; index < controlsIcons.Length; index++)
            {
                double cx = w-(90-32*index)*unit;
                DrawChromeMark(controlsIcons[index],cx,13*unit,16*unit,ChromeText,ChromeTray);
                if (win10) AddWindowsCaptionHit(window, index, new Rect(cx-8*unit, 0, 32*unit, tabsHeight));
            }

            double nav = tabsHeight;
            Rectangle(0,nav,w,navHeight,ChromeStrap);
            string[] navIcons = w >= 460 ? new[] {"back","forward","refresh","home"} : new[] {"back","refresh"};
            for (int index = 0; index < navIcons.Length; index++)
            {
                bool enabled = navIcons[index] != "back" || window.B("canGoBack");
                DrawChromeMark(navIcons[index],(16+index*35)*unit,nav+13*unit,16*unit,enabled?ChromeIcon:"#757773");
                if (navIcons[index] == "back" && enabled)
                    AddChromeSurfaceHit(window,new Rect(7*unit,nav+4*unit,33*unit,34*unit),"back","Назад · Alt+←");
            }
            bool roomy = w >= 840, editing = window.S("focus") == "url" && window.B("active",true);
            double x = (19+35*navIcons.Length)*unit, right = (roomy ? 145 : 52)*unit;
            double addressWidth = Math.Max(26,w-right-x);
            double marginL = (addressWidth>=90*unit?35:2)*unit, marginR = (addressWidth>=90*unit?32:2)*unit;
            AddChromeSurfaceHit(window,new Rect(x+marginL,nav+5*unit,Math.Max(0,addressWidth-marginL-marginR),35*unit),"address","Адрес страницы · Ctrl+L");
            string dark = editing ? "#292B28" : ChromePill, url = window.S("url");
            if (editing) Rounded(x,nav+5*unit,addressWidth,35*unit,ChromeFocus,18*unit);
            Rounded(x+(editing ? 1.5*unit : 0),nav+(editing ? 6.5 : 5)*unit,addressWidth-(editing ? 3*unit : 0),
                (editing ? 32 : 35)*unit,dark,17*unit);
            bool internalPage = !editing && url.StartsWith("chrome://",StringComparison.Ordinal) && addressWidth >= 230*unit;
            if (internalPage)
            {
                Rounded(x+4*unit,nav+10*unit,86*unit,25*unit,"#20211F",13*unit);
                DrawChromeMark("chrome",x+9*unit,nav+13*unit,18*unit,"#F5F5F5","#20211F");
                Text("Chrome",x+29*unit,nav+11*unit,14*unit,"#F5F5F5",58*unit);
            }
            else if (addressWidth >= 90*unit)
            {
                Rounded(x+5*unit,nav+10*unit,25*unit,25*unit,"#20211F",13*unit);
                DrawChromeMark("sliders",x+10*unit,nav+14*unit,15*unit,"#DEDEDC","#20211F");
            }
            double indent = (internalPage ? 99 : addressWidth>=90*unit ? 38 : 7)*unit;
            double inputWidth = Math.Max(1,addressWidth-indent-(addressWidth>=90*unit ? 32 : 6)*unit);
            if (editing)
                Field(window.S("input"),window,x+indent,nav+13*unit,inputWidth,14*unit,"#F3F3F3",0,true,
                    scrollKey: "address:"+window.S("id"),face: regular,selectionFill: ChromeSelection);
            else Text(url.StartsWith("https://",StringComparison.Ordinal) ? url.Substring(8) : url,
                x+indent,nav+12*unit,14*unit,"#F3F3F3",inputWidth);
            var saved = window.Items("savedBookmarks").Select(item => item.Value()).ToArray();
            if (addressWidth >= 90*unit)
            {
                if (saved.Contains(url)) Text("★",x+addressWidth-24*unit,nav+9*unit,17*unit,ChromeFocus,19*unit);
                else DrawChromeMark("star",x+addressWidth-24*unit,nav+14*unit,16*unit,ChromeIcon,dark);
            }
            DrawChromeMark("download",w-(roomy?131:42)*unit,nav+12*unit,18*unit,ChromeIcon);
            if (roomy)
            {
                Rounded(w-104*unit,nav+5*unit,70*unit,34*unit,ChromePill,17*unit);
                Text("Учебный",w-96*unit,nav+13*unit,12*unit,ChromeText,58*unit);
            }
            DrawChromeMark("more",w-24*unit,nav+12*unit,16*unit,ChromeIcon);

            double top = nav+navHeight;
            if (window.B("bookmarks"))
            {
                Rectangle(0,top,w,booksHeight,ChromeStrap);
                DrawChromeMark("apps",12*unit,top+11*unit,15*unit,"#B8BABC");
                Rectangle(42*unit,top+10*unit,1,16*unit,"#686868");
                double px = 58*unit;
                var entries = window.Items("entries").ToArray();
                foreach (string bookmark in saved)
                {
                    if (px >= w-152*unit) break;
                    string caption = BrowserBookmarkTitle(bookmark,tabs,entries);
                    double bw = Math.Min(212*unit,Math.Min(MeasureSceneText(caption,13*unit)+28*unit,w-148*unit-px));
                    DrawChromeSiteMark(bookmark,px,top+11*unit,15*unit,ChromeIcon,ChromeStrap);
                    Text(caption,px+21*unit,top+10*unit,13*unit,"#EEEEEE",Math.Max(1,bw-21*unit));
                    px += bw+8*unit;
                }
                if (w>=345)
                {
                    Rectangle(w-151*unit,top+10*unit,1,16*unit,"#686868");
                    DrawChromeMark("folder",w-135*unit,top+10*unit,17*unit,ChromeIcon);
                    Text("Все закладки",w-112*unit,top+9*unit,13*unit,"#EEEEEE",108*unit);
                }
                top += booksHeight;
            }
            Rectangle(0,top-1,w,1,"#222326");
            return top;
        }

        private void DrawChromeHistory(Data window,double width,double top,double bottom,Data[] entries)
        {
            double density = ChromeDensity(width);
            context.PushTransform(new TranslateTransform(0,top));
            context.PushTransform(new ScaleTransform(density,density));
            try
            {
                DrawChromeHistoryContent(window,width/density,0,(bottom-top)/density,entries,
                    (rect, action, title, path) => AddNotebookHit(new Rect(rect.X*density,top+rect.Y*density,
                        rect.Width*density,rect.Height*density),ChromeHistoryPrefix+action,title,path: path));
            }
            finally { context.Pop(); context.Pop(); }
        }

        private void DrawChromeHistoryContent(Data window,double width,double top,double bottom,Data[] entries,
            Action<Rect,string,string,string> hit)
        {
            Rectangle(0,top,width,bottom-top,ChromeHistoryBg);
            bool roomy = width>=725 && bottom-top>=140;
            double rail = roomy ? Math.Min(254,width*.24) : 0;
            if (roomy)
            {
                DrawChromeMark("chrome",18,top+13,27,"#FFFFFF",ChromeHistoryBg);
                Text("История",65,top+12,22,ChromeText,rail-70);
                Rounded(0,top+64,rail,40,"#89B3F8",22);
                Rectangle(0,top+64,rail/2,40,"#89B3F8");
                DrawChromeMark("history",24,top+74,18,"#161C22","#89B3F8");
                Text("История Chrome",64,top+74,14,"#17212A",rail-72);
                Text("Посещения в тренажёре",64,top+118,12,ChromeHistoryMuted,rail-72);
            }
            else
            {
                DrawChromeMark("chrome",16,top+6,20,"#FFFFFF",ChromeHistoryBg);
                Text("История",43,top+6,16,ChromeText,width-60);
            }
            double left = rail+Math.Max(14,(width-rail-960)/2);
            double pane = Math.Min(960,width-left-14);
            if (pane <= 85) return;
            bool shallow = bottom-top<290;
            double sy = top+(roomy?8:31), sh=shallow?35:42, sw=Math.Min(698,pane-2);
            string query = window.S("historySearch");
            int selected = window.I("item",-1);
            bool focused = window.B("active",true) && window.S("focus") == "history" && selected<0;
            bool interactive = window.B("active",true) && BrowserHistoryCommand != null && window.D("historySearch").Exists;
            double stroke = focused?1.6:1;
            Rounded(left+3,sy,sw,sh,focused?ChromeFocus:"#515357",sh/2);
            Rounded(left+3+stroke,sy+stroke,sw-2*stroke,sh-2*stroke,"#292929",sh/2-stroke);
            DrawChromeMark("search",left+17,sy+(sh-16)/2,16,ChromeHistoryMuted,"#292929");
            double searchY = sy+(sh-17)/2;
            if (query.Length == 0) Text("Поиск в истории",left+40,searchY,13,ChromeHistoryMuted,sw-48);
            if (query.Length > 0 || focused)
                Field(query,window,left+40,searchY,sw-48,13,ChromeText,0,focused,scrollKey:"history-search:"+window.S("id"),
                    face:regular,selectionFill:ChromeSelection);
            if (interactive)
                hit(new Rect(left+3,sy,sw,sh),"search","Поиск в истории · Ctrl+F","");
            double sectionY = sy+sh+(shallow?4:10);
            Text("Недавние",left+19,sectionY+8,14,ChromeFocus,pane-36);
            double lineY = sectionY+(shallow?26:42);
            Rectangle(left+4,lineY,pane-4,1,"#3C3D41");
            Rounded(left+19,lineY-2,72,3,ChromeFocus,1.5);
            double panelY = lineY+(shallow?9:24);
            if (bottom-panelY < 28) return;
            int header = shallow?32:58, pitch = shallow?31:44;
            int visible = Math.Min(entries.Length,Math.Max(0,(int)Math.Floor((bottom-panelY-header-8)/pitch)));
            int first = visible>0 ? Math.Max(0,Math.Min(entries.Length-visible,selected-visible+1)) : 0;
            double panelHeight = Math.Min(bottom-panelY-3,header+visible*pitch+9);
            Rounded(left-1,panelY-1,pane+2,panelHeight+2,"#212225",10);
            Rounded(left,panelY,pane,panelHeight,ChromeHistoryCard,10);
            string heading = entries.Length == 0 ? (query.Length==0 ? "История пока пуста" : "Ничего не найдено") :
                query.Length == 0 ? "Посещения в этом занятии" : "Найдено: "+entries.Length;
            Text(heading,left+24,panelY+(shallow?8:25),16,ChromeText,pane-48);
            for (int index = first; index < first+visible; index++)
            {
                bool chosen = index == selected;
                double rowY = panelY+header+(index-first)*pitch;
                string path=entries[index].S("path"), title=entries[index].S("title");
                string rowBg = chosen?"#384962":ChromeHistoryCard;
                if (chosen)
                {
                    Rounded(left+8,rowY+2,pane-16,pitch-4,rowBg,6);
                    Rounded(left+8,rowY+6,2,pitch-12,ChromeFocus,1);
                }
                double ty = rowY+(pitch-16)/2;
                DrawChromeSiteMark(path,left+22,ty,16,chosen?ChromeFocus:ChromeHistoryMuted,rowBg);
                double titleWidth = Math.Min(MeasureSceneText(title,13)+6,Math.Min(pane*(pane>=330?.66:.74),pane-72));
                Text(title,left+48,ty,13,ChromeText,titleWidth);
                string host = BrowserHost(path);
                if (host.Length > 0 && pane-titleWidth>=168)
                    Text(host,left+58+titleWidth,ty+1,12,ChromeHistoryMuted,pane-84-titleWidth);
                if (chosen && pane>=260) Text("Enter",left+pane-57,ty+1,11,ChromeFocus,44);
                if (interactive && path.Length > 0)
                    hit(new Rect(left+8,rowY+2,pane-16,pitch-4),"entry","Выбрать: "+title+" · Enter — открыть",path);
            }
            if (visible>0 && entries.Length>visible)
            {
                double track = Math.Max(6,panelHeight-header-10);
                double thumb = Math.Max(19,track*visible/entries.Length);
                double read = (track-thumb)*first/(entries.Length-visible);
                Rounded(left+pane-5,panelY+header,3,track,"#3B3C40",1.5);
                Rounded(left+pane-5,panelY+header+read,3,thumb,"#9AA0A6",1.5);
            }
        }

        private string ChromeHistoryAccessible(Data window)
        {
            var entries = window.Items("entries").ToArray();
            int selected = window.I("item",-1);
            return "История Chrome. Поиск в истории: " + window.S("historySearch","пусто") + ". Записей: " + entries.Length +
                (selected >= 0 && selected < entries.Length ? ". Выбрано: " + entries[selected].S("title") + ". Enter — открыть." :
                    ". Введите запрос или нажмите Tab, чтобы выбрать запись.");
        }
    }
}
