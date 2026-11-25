using System;
using System.Linq;
using System.Windows;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView
    {
        private const string ChromeTabsPrefix = "browser-tabs:";
        internal event Action<string, string> BrowserTabCommand;

        private bool OnBrowserTabHotspot(NotebookHit hit)
        {
            if (BrowserTabCommand == null || !hit.Action.StartsWith(ChromeTabsPrefix, StringComparison.Ordinal))
                return false;
            BrowserTabCommand(hit.Action.Substring(ChromeTabsPrefix.Length), hit.Path);
            return true;
        }

        private void DrawChromeTabSearch(Data window, double width, double top, double bottom)
        {
            var search = window.D("tabSearch");
            if (!search.Exists) return;
            var rows = search.Items("rows").ToArray();
            int selected = search.I("selected", -1);
            double pane = Math.Min(520, width-16), left = 8;
            const double pitch = 39;
            if (pane < 105 || bottom-top < 110) return;
            int visible = Math.Min(rows.Length, Math.Max(0, (int)((bottom-top-110)/pitch)));
            int first = Math.Min(Math.Max(0,selected-visible+1),Math.Max(0,rows.Length-visible));
            Rounded(left-1,top,pane+2,104+visible*pitch,"#16171A",11);
            Rounded(left,top+1,pane,102+visible*pitch,"#303134",10);
            Rounded(left+10,top+10,pane-20,35,ChromeFocus,7);
            Rounded(left+11,top+11,pane-22,33,"#27282B",6);
            DrawChromeMark("search",left+19,top+20,15,ChromeHistoryMuted,"#27282B");
            if (window.S("input").Length == 0)
                Text("Поиск открытых вкладок",left+44,top+18,13,ChromeHistoryMuted,pane-65);
            Field(window.S("input"),window,left+44,top+18,pane-65,13,"#FFFFFF",0,window.B("active"),
                scrollKey:"tabs-search:"+window.S("id"),face:regular,selectionFill:ChromeSelection);
            Text(rows.Length == 0 ? "Нет совпадений" : "Открытые вкладки · " + rows.Length,
                left+13,top+55,12,ChromeHistoryMuted,pane-26);
            for (int i = first; i < first+visible; i++)
            {
                var row = rows[i];
                string path = row.S("url"), title = row.S("title");
                double y = top+75+(i-first)*pitch;
                bool chosen = selected==i;
                string tone = chosen?"#41536D":"#303134";
                Rounded(left+6,y,pane-12,pitch-2,tone,6);
                DrawChromeSiteMark(path,left+15,y+10,17,chosen?"#D4E3FB":ChromeHistoryMuted,tone);
                Text(title,left+43,y+3,13,"#F3F4F6",pane-60);
                Text(BrowserShortAddress(path),left+43,y+20,11,"#C2C8D0",pane-60);
                if (window.B("active") && BrowserTabCommand != null)
                    AddNotebookHit(new Rect(left+6,y,pane-12,pitch-2), ChromeTabsPrefix+"select",
                        "Перейти: "+title,path:i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            Text("↑ ↓ выбрать  ·  Enter открыть  ·  Esc закрыть",left+13,top+80+visible*pitch,11,ChromeHistoryMuted,pane-26);
        }

        private void DrawChromeSourceReview(Data window, double width, double top, double bottom)
        {
            var review = window.D("sourceReview");
            double pane = Math.Min(660,width-28), left = 14+Math.Max(0,(width-28-pane)/2);
            double area = Math.Max(0,Math.Min(216,bottom-top-16));
            if (area<100 || pane<145) return;
            Rounded(left,top+8,pane,area,"#E4E8EF",10);
            Rounded(left+1,top+9,pane-2,area-2,"#FFFFFF",9);
            Text("Лист сверки",left+16,top+19,20,"#202124",pane-32);
            string saved = review.S("saved");
            if (saved.Length > 0)
            {
                Text("Сверка сохранена",left+16,top+53,15,"#246143",pane-32);
                var lines=saved.Split('\n');
                for (int i=0;i<Math.Min(2,lines.Length);i++)
                    Text(lines[i],left+16,top+79+i*21,13,"#3F4651",pane-32);
                return;
            }
            if (area>=170)
                Text("Скопируйте ссылку действующей редакции и внесите её трёхзначный код.",
                    left+16,top+52,13,"#4C5159",pane-32);
            double y=top+(area>=170?82:55);
            bool focused=window.S("focus")=="source-review" && window.B("active");
            Rounded(left+16,y,154,36,focused?"#3078D2":"#C1C5CF",6);
            Rounded(left+17,y+1,152,34,"#FFFFFF",5);
            string code=review.S("code"), copied=review.S("copied");
            if (code.Length==0) Text("Контрольный код",left+25,y+9,13,"#555D69",123);
            if (focused || code.Length>0)
                Field(code,window,left+25,y+9,135,13,"#202124",0,focused,
                    scrollKey:"source-review:"+window.S("id"),face:regular,
                    selectionFill:ChromeFocus);
            if (window.B("active") && BrowserTabCommand != null)
                AddNotebookHit(new Rect(left+16,y,154,36),ChromeTabsPrefix+"review","Контрольный код");
            if (area>=154)
            {
                Text("Адрес из буфера",left+16,y+44,12,"#555D69",pane-32);
                Text(copied.Length == 0 ? "Адрес не скопирован" : copied,left+16,y+63,13,"#20324C",pane-32);
            }
        }

        private static string ChromeTabAccessible(Data window)
        {
            var search=window.D("tabSearch");
            if (search.Exists)
            {
                var rows=search.Items("rows").ToArray();
                int selected=search.I("selected",-1);
                string query=window.S("input");
                return "Поиск открытых вкладок. Запрос: "+(query.Length==0?"пусто":query)
                    +". Совпадений: "+rows.Length+". Выбрано: "
                    +(selected>=0 && selected<rows.Length ? rows[selected].S("title"):"нет")+".";
            }
            var review=window.D("sourceReview");
            if (!review.Exists) return "";
            if (review.S("saved").Length>0) return "Лист сверки. Сохранено: "+review.S("saved");
            return "Лист сверки. Трёхзначный код: "+(review.S("code").Length==0?"пусто":review.S("code"))
                +". Скопированный адрес: "+(review.S("copied").Length==0?"нет":review.S("copied"))+".";
        }
    }
}
