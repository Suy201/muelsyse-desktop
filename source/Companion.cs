using System.Runtime.InteropServices;

namespace Muelsyse;

internal sealed class Reminders
{
    double waterAt=1800,workSince;bool away;
    public bool Enabled=true;
    public string? Tick(double now,double idleSeconds,bool busy)
    {
        if(idleSeconds>=300){away=true;workSince=now;return null;}
        if(away){away=false;workSince=now;waterAt=Math.Max(waterAt,now+60);}
        if(!Enabled){waterAt=now+1800;workSince=now;return null;}
        if(busy)return null;
        if(now>=waterAt){waterAt=now+1800;return "water";}
        if(now-workSince>=3000){workSince=now;return "rest";}
        return null;
    }
    public void AfterSleep(double now){waterAt=now+1800;workSince=now;away=false;}
    public void Snooze(double now,string kind="water"){if(kind=="water")waterAt=now+600;else if(kind=="rest")workSince=now-2400;}
    [StructLayout(LayoutKind.Sequential)] struct InputInfo{public uint Size,Time;}
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref InputInfo info);
    public static double IdleSeconds(){var info=new InputInfo{Size=8};return GetLastInputInfo(ref info)?unchecked((uint)Environment.TickCount-info.Time)/1000.0:0;}
}

internal sealed class HoverCard:Form
{
    readonly Label state,quota,notice;
    readonly Panel bar=new(),fill=new();
    public readonly Button Details=new();internal readonly Button Later=new(),NewTask=new(),Voice=new();
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80;return p;}}
    public HoverCard(Action details,Action snooze,Action? newTask=null,Action? voice=null)
    {
        Text="缪尔赛思 · 随身手记";AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;ClientSize=new(272,182);BackColor=WaterUi.Mist;Opacity=.9;DoubleBuffered=true;
        Font=WaterUi.Font();ForeColor=WaterUi.Ink;
        Controls.Add(WaterUi.Label("随身手记",new(16,12,224,18),11,WaterUi.Teal));
        state=WaterUi.Label("",new(16,37,240,37),13);quota=WaterUi.Label("",new(16,78,240,20),11,WaterUi.Muted);
        bar.SetBounds(16,101,240,3);bar.BackColor=WaterUi.Line;fill.Height=3;fill.BackColor=WaterUi.Teal;bar.Controls.Add(fill);
        notice=WaterUi.Label("",new(16,115,240,40),12);
        WaterUi.Button(NewTask,"新任务  ↗",new(8,113,124,29));NewTask.Click+=(_,_)=>newTask?.Invoke();
        WaterUi.Button(Voice,"语音对话",new(140,113,124,29));Voice.Click+=(_,_)=>voice?.Invoke();
        WaterUi.Button(Details,"打开手记  ›",new(8,147,112,29));Details.Click+=(_,_)=>details();
        WaterUi.Button(Later,"收起",new(157,147,106,29));Later.Click+=(_,_)=>snooze();
        Controls.AddRange([state,quota,bar,notice,NewTask,Voice,Details,Later]);RoundCorners();
    }
    void RoundCorners()
    {
        using var path=new System.Drawing.Drawing2D.GraphicsPath();int d=24,w=Width-1,h=Height-1;
        path.AddArc(0,0,d,d,180,90);path.AddArc(w-d,0,d,d,270,90);path.AddArc(w-d,h-d,d,d,0,90);path.AddArc(0,h-d,d,d,90,90);path.CloseFigure();
        var old=Region;Region=new Region(path);old?.Dispose();
    }
    public void UpdateContent(string detail,List<Quota> quotas,DateTimeOffset? updated,string quotaNote,string message,bool health=false)
    {
        Later.Text=health?"10 分钟后提醒":"收起";state.Text=detail;
        quota.Text=quotas.Count==0?"额度连接中 · 打开手记查看详情":string.Join(" · ",quotas.Take(2).Select(q=>$"{WaterUi.Window(q)}余 {WaterUi.Remaining(q):0.#}%"));
        if(updated!=null&&(quotaNote.Contains("暂")||quotaNote.Contains("缓存")))quota.Text+=" · 缓存";
        fill.Width=quotas.Count==0?0:(int)(240*quotas.Min(WaterUi.Remaining)/100);
        notice.Text=message;notice.Visible=message.Length>0;
        int height=notice.Visible?228:182;if(Height!=height){Height=height;NewTask.Top=Voice.Top=height-69;Details.Top=Later.Top=height-35;RoundCorners();}
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen=new Pen(WaterUi.Line);e.Graphics.DrawLine(pen,15,0,Width-15,0);e.Graphics.DrawLine(pen,15,Height-1,Width-15,Height-1);
    }
}
