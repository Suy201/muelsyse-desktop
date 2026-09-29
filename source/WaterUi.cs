using System.Drawing.Drawing2D;

namespace Muelsyse;

internal static class WaterUi
{
    public static readonly Color Mist=Color.FromArgb(240,249,248),Paper=Color.FromArgb(250,253,252),Ink=Color.FromArgb(29,72,78),Muted=Color.FromArgb(83,116,120),Teal=Color.FromArgb(43,119,131),Line=Color.FromArgb(194,221,222),Wash=Color.FromArgb(219,240,240);
    public static Font Font(float size=13,FontStyle style=FontStyle.Regular)=>new("Microsoft YaHei UI",size,style,GraphicsUnit.Pixel);
    public static Label Label(string text,Rectangle bounds,float size=13,Color? color=null)=>new(){Text=text,Bounds=bounds,Font=Font(size),ForeColor=color??Ink,BackColor=Color.Transparent,AutoEllipsis=true,UseMnemonic=false};
    public static void Button(Button button,string text,Rectangle bounds,bool primary=false)
    {
        button.Text=text;button.Bounds=bounds;button.Font=Font(12);button.FlatStyle=FlatStyle.Flat;
        button.FlatAppearance.BorderSize=primary?1:0;button.FlatAppearance.BorderColor=Line;button.FlatAppearance.MouseOverBackColor=Wash;
        button.BackColor=primary?Wash:Color.Transparent;button.ForeColor=Ink;button.Cursor=Cursors.Hand;
    }
    public static string Window(Quota q)=>q.Minutes>=1440?$"{q.Minutes/1440.0:0.#} 天":q.Minutes>=60?$"{q.Minutes/60.0:0.#} 小时":q.Minutes>0?$"{q.Minutes} 分钟":"未知周期";
    public static double Remaining(Quota q)=>Math.Clamp(100-q.Used,0,100);
    public static ContextMenuStrip Menu()
    {
        var menu=new ContextMenuStrip{Font=Font(14),ForeColor=Ink,BackColor=Paper,Renderer=new WaterMenuRenderer(),ShowImageMargin=false,ShowCheckMargin=true,Padding=new Padding(6),DropShadowEnabled=true};
        menu.ItemAdded+=(_,e)=>{if(e.Item!=null)e.Item.Padding=new Padding(8,7,12,7);};
        return menu;
    }
    sealed class WaterMenuRenderer:ToolStripProfessionalRenderer
    {
        public WaterMenuRenderer():base(new WaterColors()){RoundedEdges=false;}
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Ink:Muted;base.OnRenderItemText(e);}
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r=e.ImageRectangle;float x=r.Left+r.Width/2f,y=r.Top+r.Height/2f;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var pen=new Pen(Teal,1.8f);
            e.Graphics.DrawLines(pen,[new PointF(x-4,y),new PointF(x-1,y+3),new PointF(x+5,y-4)]);
        }
    }
    sealed class WaterColors:ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground=>Paper;
        public override Color ImageMarginGradientBegin=>Paper;
        public override Color ImageMarginGradientMiddle=>Paper;
        public override Color ImageMarginGradientEnd=>Paper;
        public override Color MenuItemSelected=>Wash;
        public override Color MenuItemBorder=>Line;
        public override Color MenuBorder=>Line;
        public override Color MenuItemSelectedGradientBegin=>Wash;
        public override Color MenuItemSelectedGradientEnd=>Wash;
        public override Color MenuItemPressedGradientBegin=>Wash;
        public override Color MenuItemPressedGradientMiddle=>Wash;
        public override Color MenuItemPressedGradientEnd=>Wash;
        public override Color SeparatorDark=>Line;
        public override Color SeparatorLight=>Paper;
        public override Color CheckBackground=>Wash;
        public override Color CheckSelectedBackground=>Wash;
    }
}

internal sealed class WaterHeader:Panel
{
    public WaterHeader(){DoubleBuffered=true;BackColor=WaterUi.Mist;}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using var wash=new SolidBrush(Color.FromArgb(222,242,241));g.FillEllipse(wash,Width-132,-66,194,194);
        using var line=new Pen(WaterUi.Line,1);
        for(int i=0;i<3;i++)g.DrawArc(line,Width-148-i*17,22+i*9,182+i*34,72+i*18,12,151);
        using var drop=new GraphicsPath();float x=Width-56,y=37;
        drop.AddBezier(x,y,x-2,y+11,x-16,y+19,x-12,y+28);drop.AddBezier(x-12,y+28,x-8,y+42,x+14,y+38,x+13,y+25);drop.AddBezier(x+13,y+25,x+12,y+17,x+3,y+6,x,y);
        using var water=new SolidBrush(Color.FromArgb(95,170,179));g.FillPath(water,drop);
    }
}

internal sealed class JournalForm:Form
{
    readonly Label status,note;readonly FlowLayoutPanel quotas;readonly Button refresh=new();
    internal readonly Button Actions=new();
    string quotaKey="";
    public JournalForm(Func<Task> refreshQuota,Action openActions)
    {
        Text="缪尔赛思 · 观察手记";ClientSize=new(420,490);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        BackColor=WaterUi.Paper;Font=WaterUi.Font();ForeColor=WaterUi.Ink;StartPosition=FormStartPosition.CenterScreen;TopMost=true;AutoScaleMode=AutoScaleMode.None;
        var header=new WaterHeader{Dock=DockStyle.Top,Height=118};
        header.Controls.Add(WaterUi.Label("缪尔赛思",new(24,18,300,20),12,WaterUi.Muted));
        header.Controls.Add(WaterUi.Label("观察手记",new(22,43,300,39),26));
        header.Controls.Add(WaterUi.Label("博士，今天有什么新发现？",new(24,88,310,20),12,WaterUi.Muted));
        var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(24,0,24,0)};
        var state=new Panel{Dock=DockStyle.Top,Height=85};
        state.Controls.Add(WaterUi.Label("Codex 状态",new(0,18,340,18),11,WaterUi.Teal));
        status=WaterUi.Label("",new(0,43,364,36),14);state.Controls.Add(status);
        var footer=new Panel{Dock=DockStyle.Bottom,Height=126,BackColor=WaterUi.Mist};
        note=WaterUi.Label("",new(24,12,372,43),11,WaterUi.Muted);footer.Controls.Add(note);
        WaterUi.Button(refresh,"刷新额度",new(24,60,102,32),true);
        refresh.Click+=async(_,_)=>{refresh.Enabled=false;refresh.Text="正在刷新…";try{await refreshQuota();}finally{if(!refresh.IsDisposed){refresh.Enabled=true;refresh.Text="刷新额度";}}};
        footer.Controls.Add(refresh);footer.Controls.Add(WaterUi.Label("左键招呼 · 拖动移动\n右键打开更多选项",new(169,62,227,39),11,WaterUi.Muted));
        footer.Controls.Add(WaterUi.Label("连接暂不可用时，我也会在这里陪你。",new(24,102,372,19),11,WaterUi.Muted));
        quotas=new FlowLayoutPanel{Name="quotas",Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(0,0,0,12)};
        WaterUi.Button(Actions,"动作预览…",new(252,55,112,29));Actions.Click+=(_,_)=>openActions();state.Controls.Add(Actions);Actions.BringToFront();
        status.Width=240;
        body.Controls.Add(quotas);body.Controls.Add(state);Controls.Add(body);Controls.Add(footer);Controls.Add(header);
    }
    public void UpdateContent(string detail,List<Quota> values,DateTimeOffset? updated,string quotaNote)
    {
        status.Text=detail;
        string key=System.Text.Json.JsonSerializer.Serialize(values);
        if(key!=quotaKey)
        {
            ClientSize=new Size(420,Math.Min(640,490+Math.Max(0,values.Count-1)*100));
            if(Visible){var area=Screen.FromRectangle(Bounds).WorkingArea;Top=Math.Clamp(Top,area.Top,Math.Max(area.Top,area.Bottom-Height));}
            quotaKey=key;quotas.SuspendLayout();foreach(Control child in quotas.Controls.Cast<Control>().ToArray())child.Dispose();quotas.Controls.Clear();
            int rowWidth=Math.Max(1,quotas.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-8);
            foreach(var q in values.Take(4))
            {
                var row=new Panel{Size=new(rowWidth,102),Margin=new Padding(0,0,0,12)};
                row.Controls.Add(WaterUi.Label($"{q.Name}  ·  {WaterUi.Window(q)}",new(0,3,rowWidth-127,22),12,WaterUi.Muted));
                var amount=WaterUi.Label($"{WaterUi.Remaining(q):0.#}%",new(rowWidth-127,0,127,32),24);amount.TextAlign=ContentAlignment.MiddleRight;row.Controls.Add(amount);
                var remaining=WaterUi.Label("剩余",new(rowWidth-65,33,65,19),11,WaterUi.Muted);remaining.TextAlign=ContentAlignment.MiddleRight;row.Controls.Add(remaining);
                var bar=new Panel{Bounds=new(0,58,rowWidth,5),BackColor=WaterUi.Wash};bar.Controls.Add(new Panel{Bounds=new(0,0,(int)(rowWidth*WaterUi.Remaining(q)/100),5),BackColor=WaterUi.Teal});row.Controls.Add(bar);
                string reset=q.Reset>0?DateTimeOffset.FromUnixTimeSeconds(q.Reset).ToLocalTime().ToString("MM-dd HH:mm"):"未知";
                row.Controls.Add(WaterUi.Label($"重置于 {reset}",new(0,76,rowWidth,20),11,WaterUi.Muted));
                quotas.Controls.Add(row);
            }
            if(values.Count==0){var empty=WaterUi.Label("额度暂不可用\n连接恢复后会自动更新。",new(0,14,rowWidth,75),13,WaterUi.Muted);empty.Padding=new Padding(0,15,0,0);quotas.Controls.Add(empty);}
            quotas.ResumeLayout();
        }
        note.Text=(updated is{} time?$"{time:MM-dd HH:mm} 更新 · 每 5 分钟刷新":"尚无成功读取记录")+"\n"+quotaNote;
    }
}
