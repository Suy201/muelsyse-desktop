using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Muelsyse;

internal sealed class ActionPalette:Form
{
    [DllImport("user32.dll")]internal static extern IntPtr GetForegroundWindow();
    internal readonly FlowLayoutPanel ActionButtons;
    internal readonly GesturePreview Preview;
    readonly Label playback;
    readonly System.Windows.Forms.Timer hoverDelay=new(){Interval=180};
    Gesture? hovered;
    public ActionPalette(Sprites sprites,Action<string> playGesture)
    {
        Text="缪尔赛思 · 动作";ClientSize=new(288,Math.Min(142+((Behavior.Gestures.Count+1)/2)*46,Screen.FromPoint(Cursor.Position).WorkingArea.Height-48));FormBorderStyle=FormBorderStyle.FixedToolWindow;ShowInTaskbar=false;TopMost=true;
        StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;BackColor=WaterUi.Mist;Padding=new Padding(12,0,12,0);
        var title=WaterUi.Label("悬停预览，点击播放",new(0,0,248,42),13);title.Dock=DockStyle.Top;title.TextAlign=ContentAlignment.MiddleLeft;
        var footer=new Panel{Dock=DockStyle.Bottom,Height=100};playback=WaterUi.Label("点击选择动作",new(0,4,248,43),12,WaterUi.Teal);footer.Controls.Add(playback);
        footer.Controls.Add(WaterUi.Label("当前动作播完后切换，保留最后一次选择。\n暂停时点击会恢复播放。",new(0,52,248,42),11,WaterUi.Muted));
        ActionButtons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=true};Preview=new GesturePreview(sprites);
        foreach(var gesture in Behavior.Gestures)
        {
            var button=new Button{Tag=gesture.Id,Margin=new Padding(0,0,8,8),AccessibleName="预览或播放"+gesture.Label};
            WaterUi.Button(button,gesture.Label,new(0,0,112,38),true);
            button.MouseEnter+=(_,_)=>HoverGesture(gesture.Id);button.MouseLeave+=(_,_)=>HidePreview();
            button.Click+=(_,_)=>{HidePreview();playGesture(gesture.Id);};ActionButtons.Controls.Add(button);
        }
        hoverDelay.Tick+=(_,_)=>
        {
            hoverDelay.Stop();if(hovered==null||!Visible)return;
            var button=ActionButtons.Controls.Cast<Button>().Single(b=>(string)b.Tag! == hovered.Id);
            var anchor=new Rectangle(Left,button.PointToScreen(Point.Empty).Y,Width,button.Height);
            PlaceBeside(Preview,anchor);Preview.ShowGesture(hovered,this);
        };
        LocationChanged+=(_,_)=>HidePreview();
        Controls.Add(ActionButtons);Controls.Add(footer);Controls.Add(title);
        // Create the hidden popup before the palette takes focus; hovering only shows it without activation.
        Preview.Owner=this;_ = Preview.Handle;foreach(Control child in Preview.Controls)_ = child.Handle;
    }
    internal void HoverGesture(string id)
    {
        HidePreview();hovered=Behavior.Gestures.FirstOrDefault(g=>g.Id==id);if(hovered!=null)hoverDelay.Start();
    }
    internal void HidePreview(){hoverDelay.Stop();hovered=null;Preview.HidePreview();}
    public void UpdatePlayback(string action,string? pending,bool paused)
    {
        var current=Behavior.Gestures.FirstOrDefault(g=>g.Id==action);var next=Behavior.Gestures.FirstOrDefault(g=>g.Id==pending);
        playback.Text=paused?"动作已暂停":(current!=null?$"正在播放：{current.Label}":"点击选择动作")+(next!=null?$"\n接下来：{next.Label}":"");
        foreach(Button button in ActionButtons.Controls)
        {bool active=!paused&&(string?)button.Tag==current?.Id;button.BackColor=active?WaterUi.Teal:WaterUi.Wash;button.ForeColor=active?Color.White:WaterUi.Ink;}
    }
    internal static void PlaceBeside(Form window,Rectangle anchor)
    {
        var area=Screen.FromRectangle(anchor).WorkingArea;int x=anchor.Right+8;if(x+window.Width>area.Right)x=anchor.Left-window.Width-8;
        window.Location=new Point(Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-window.Width)),Math.Clamp(anchor.Top,area.Top,Math.Max(area.Top,area.Bottom-window.Height)));
    }
    protected override void Dispose(bool disposing)
    {if(disposing){hoverDelay.Dispose();Preview.Dispose();}base.Dispose(disposing);}
}

internal sealed class GesturePreview:Form
{
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr handle,int command);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr handle);
    readonly SpritePlayer player;readonly Sprites sprites;readonly SharpRaster raster=new(192,208);
    readonly Stopwatch clock=new();readonly System.Windows.Forms.Timer frames=new(){Interval=33};readonly Label title;
    Gesture? gesture;internal long RenderedFrames{get;private set;}internal string? GestureId=>gesture?.Id;
    internal bool HasForeground=>ActionPalette.GetForegroundWindow()==Handle;
    internal bool PreviewVisible=>IsHandleCreated&&IsWindowVisible(Handle);
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80|0x08000000;return p;}}
    public GesturePreview(Sprites sprites)
    {
        this.sprites=sprites;player=new(sprites);ClientSize=new(240,267);StartPosition=FormStartPosition.Manual;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;BackColor=WaterUi.Paper;DoubleBuffered=true;
        title=WaterUi.Label("",new(12,8,216,22),12,WaterUi.Teal);Controls.Add(title);
        Controls.Add(WaterUi.Label("预览中 · 点击动作让桌宠播放",new(12,245,216,18),11,WaterUi.Muted));
        frames.Tick+=(_,_)=>DrawFrame();
    }
    internal void ShowGesture(Gesture value,Form owner)
    {
        gesture=value;title.Text=$"{value.Label} · {value.Duration:0.#} 秒";clock.Restart();DrawFrame();Owner=owner;
        // SW_SHOWNOACTIVATE avoids the activation performed by the managed owned-form Show path.
        ShowWindow(Handle,4);frames.Start();
    }
    internal void HidePreview(){frames.Stop();if(IsHandleCreated)ShowWindow(Handle,0);}
    protected override void WndProc(ref Message message)
    {if(message.Msg==0x21){message.Result=(IntPtr)3;return;}base.WndProc(ref message);}
    void DrawFrame()
    {
        if(gesture==null)return;
        var clip=sprites.Get(gesture.Id);player.Paint(gesture.Id,(int)(clock.Elapsed.TotalSeconds*30)%clip.Frames);raster.Update(player.Pixels);RenderedFrames++;Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {base.OnPaint(e);e.Graphics.DrawImageUnscaled(raster.Image,24,32);using var border=new Pen(WaterUi.Line);e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);}
    protected override void OnVisibleChanged(EventArgs e)
    {if(!Visible)frames.Stop();base.OnVisibleChanged(e);}
    protected override void Dispose(bool disposing)
    {if(disposing){frames.Dispose();raster.Dispose();}base.Dispose(disposing);}
}
