using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace Muelsyse;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        Directory.CreateDirectory(CodexLink.Data);
        if(args.Contains("--self-test")){Checks.Run(args);return;}
        if(args.Contains("--diagnose")){Checks.Diagnose().GetAwaiter().GetResult();return;}
        if(args.Contains("--monitor-probe")){Checks.MonitorProbe(args).GetAwaiter().GetResult();return;}
        using var mutex=new Mutex(true,"Local\\MuelsyseDesktopPet",out bool first);
        if(!first&&!args.Contains("--smoke-test")&&!args.Contains("--ui-preview")&&!args.Contains("--ui-check")&&!args.Contains("--kettle-preview"))return;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException+=(_,e)=>{File.WriteAllText(Path.Combine(CodexLink.Data,"last-error.txt"),e.Exception.ToString());MessageBox.Show("诊断已保存到 %LOCALAPPDATA%\\MuelsysePet\\last-error.txt。","缪尔赛思");};
        Application.Run(new PetForm(args));
    }
}
internal record Settings(int X,int Y,double Scale,bool OnTop,bool KettleVoice=true);

internal sealed partial class PetForm : Form
{
    readonly Behavior brain=new();readonly Sprites sprites=new();readonly SpritePlayer player;readonly InteractionLines interactionLines=new();
    readonly CodexLink link=new();readonly Stopwatch clock=Stopwatch.StartNew();readonly NotifyIcon tray=new();
    SharpRaster? raster;
    readonly Font speechFont=new("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly string[] args;readonly bool smoke;
    Surface? surface;FrameClock? frameClock;Point? down;Point dragOrigin;bool dragging,closing;
    int queued;double scale=1,speechUntil;string speech="",status="idle",statusDetail="独立陪伴中";
    long ticks,lastPresented=-1;bool dirty=true;Form? dashboard;Action? updateDashboard;
    readonly List<double> times=[];
    readonly System.Windows.Forms.Timer heartbeat=new(){Interval=250};readonly Reminders reminders=new();
    HoverCard? card;bool cardDismissed;double nearCardSince=-1,leaveCardSince=-1,noticeUntil,lastHeartbeat;string notice="",noticeKind="status";
    ActionPalette? actionPalette;
    bool PanelInteraction=>actionPalette?.Visible==true||dragging||dashboard?.Visible==true||ContextMenuStrip?.Visible==true||tray.ContextMenuStrip?.Visible==true;
    void RefreshPanels(){updateDashboard?.Invoke();card?.UpdateContent(statusDetail,link.Quotas,link.QuotaUpdated,link.QuotaNote,notice,noticeKind is "water" or "rest");}
    void Heartbeat(Point? testPointer=null)
    {
        double now=clock.Elapsed.TotalSeconds;
        actionPalette?.UpdatePlayback(brain.Action,brain.PendingAction,brain.Paused);
        if(lastHeartbeat>0&&now-lastHeartbeat>60)reminders.AfterSleep(now);lastHeartbeat=now;
        if(now>speechUntil&&speech.Length>0){speech="";dirty=true;if(brain.Paused||kettle.Hidden)DrawSurface();}
        if(now>noticeUntil&&notice.Length>0){notice="";RefreshPanels();}
        if(!Visible||KettleBusy){card?.Hide();return;}
        var p=testPointer??Cursor.Position;var region=Bounds;region.Inflate(85,60);
        bool near=region.Contains(p)||(card?.Visible==true&&card.Bounds.Contains(p));
        if(PanelInteraction){card?.Hide();nearCardSince=-1;}
        else if(near){leaveCardSince=-1;if(nearCardSince<0)nearCardSince=now;if(now-nearCardSince>=.2)ShowCard();}
        else{cardDismissed=false;nearCardSince=-1;if(leaveCardSince<0)leaveCardSince=now;if(now-leaveCardSince>.65&&now>noticeUntil)card?.Hide();}
        if(!smoke)
        {
            string? reminder=reminders.Tick(now,Reminders.IdleSeconds(),brain.Busy(now)||now<noticeUntil);
            if(reminder!=null){string action=reminder=="water"?"drink":"stretch";brain.Request(action,now,Behavior.GestureDuration(action));Notify(reminder=="water"?"博士，喝一口水吧，水分很重要哦。":"已经忙了一阵子，博士，起来活动一下吧。",12,reminder);}
        }
    }
    void ShowCard()
    {
        if(PanelInteraction||cardDismissed||KettleBusy)return;
        if(card==null||card.IsDisposed){card=new HoverCard(ShowDashboard,()=>{reminders.Snooze(clock.Elapsed.TotalSeconds,noticeKind);notice="";noticeUntil=0;speech="";dirty=true;cardDismissed=true;RefreshPanels();card?.Hide();},()=>LaunchCodex(false),()=>LaunchCodex(true));RefreshPanels();}
        var area=Screen.FromRectangle(Bounds).WorkingArea;
        int x=Right+8;if(x+card.Width>area.Right)x=Left-card.Width-8;
        var position=new Point(Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-card.Width)),Math.Clamp(Top+(int)(82*scale),area.Top,Math.Max(area.Top,area.Bottom-card.Height)));
        if(card.Location!=position)card.Location=position;
        if(card.TopMost!=TopMost)card.TopMost=TopMost;if(!card.Visible)card.Show(this);
    }
    void Notify(string text,double duration,string kind="status"){noticeKind=kind;notice=text;noticeUntil=clock.Elapsed.TotalSeconds+duration;Say(text,duration);RefreshPanels();if(Visible)ShowCard();}
    void LaunchCodex(bool voice)
    {
        if(smoke)return;
        cardDismissed=true;card?.Hide();
        try
        {
            if(voice){string result=CodexLaunch.Voice();if(result.StartsWith("已发送"))Say("已发送语音快捷键，请在 Codex 查看。",4);else MessageBox.Show(this,result,"缪尔赛思 · 实时语音",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            else CodexLaunch.Open(CodexLaunch.NewTaskUri);
        }
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or System.IO.IOException or JsonException or UnauthorizedAccessException)
        {MessageBox.Show(this,"无法打开 Codex："+ex.Message,"缪尔赛思",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    }

    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000|0x80;return p;}}
    protected override bool ShowWithoutActivation=>true;
    public PetForm(string[] args)
    {
        this.args=args;smoke=args.Contains("--kettle-preview")||args.Contains("--smoke-test")||args.Contains("--ui-preview")||args.Contains("--ui-check");player=new(sprites);
        Text="缪尔赛思 · 独立桌宠";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(252,290);StartPosition=FormStartPosition.Manual;
        ContextMenuStrip=Menu();heartbeat.Tick+=(_,_)=>Heartbeat();
        brain.ActionChanged+=a=>{if(a=="wave")Say("博士，我在这里。",3);};
        Shown+=async(_,_)=>
        {
            LoadSettings();SetupTray();await sprites.Warm("idle","working","wave","look");player.Paint("idle",0);
            if(args.Contains("--ui-check")){await CheckUi();Close();return;}
            if(args.Contains("--ui-preview"))
            {
                await link.RefreshQuota();OnStatus(link.ReadStatus());ContextMenuStrip!.Items[0].PerformClick();await Task.Delay(250);
                bool menuOpensDetails=dashboard?.Visible==true;
                using var image=new Bitmap(dashboard!.Width,dashboard.Height);dashboard.DrawToBitmap(image,new Rectangle(0,0,dashboard.Width,dashboard.Height));
                string output=args[Array.IndexOf(args,"--ui-preview")+1];image.Save(output,ImageFormat.Png);
                dashboard.Close();ShowCard();await Task.Delay(200);
                using var small=new Bitmap(card!.Width,card.Height);card.DrawToBitmap(small,card.ClientRectangle);small.Save(Path.ChangeExtension(output,"hover.png"),ImageFormat.Png);
                card.Details.PerformClick();await Task.Delay(100);
                File.WriteAllText(Path.ChangeExtension(output,"ui.json"),JsonSerializer.Serialize(new{contextMenuOpensDetails=menuOpensDetails,hoverDetailsOpensWindow=dashboard?.Visible==true,hoverHidesWhileDetailsOpen=!card.Visible}));Close();return;
            }
            link.Changed+=s=>Post(()=>OnStatus(s));link.QuotaChanged+=()=>Post(RefreshPanels);
            if(!smoke)link.Start();
            Say("博士，今天也一起看看新发现吧。",5);StartClock();heartbeat.Start();
            if(args.Contains("--smoke-test"))_=RunSmoke();
        };
    }
    void Post(Action action){if(closing||IsDisposed||!IsHandleCreated)return;try{BeginInvoke(action);}catch(InvalidOperationException){}}
    void StartClock()
    {
        if(frameClock!=null||!Visible||(!pendingKettle&&!kettle.Moving&&(brain.Paused||kettle.Hidden)))return;
        frameClock=new(()=>{if(Interlocked.CompareExchange(ref queued,1,0)==0)Post(()=>{Interlocked.Exchange(ref queued,0);Tick();});});frameClock.Start();
    }
    void StopClock(){frameClock?.Dispose();frameClock=null;Interlocked.Exchange(ref queued,0);}
    void Tick()
    {
        if(closing)return;double now=clock.Elapsed.TotalSeconds;ticks++;if(smoke)times.Add(now);
        Point mouse=PointToClient(Cursor.Position);double x=mouse.X/scale-30,y=mouse.Y/scale-82;
        double dx=Math.Max(Math.Max(-x,x-192),0),dy=Math.Max(Math.Max(-y,y-208),0);
        if(kettle.Active)PaintKettle(now);
        else
        {
            if(pendingKettle)brain.Tick(now,500,500,500);
            else if(!smoke)brain.Tick(now,x-96,y-100,Math.Sqrt(dx*dx+dy*dy));
            player.Draw(brain,now);
            if(pendingKettle&&(brain.Paused||(!brain.Busy(now)&&!brain.Looking&&!player.LastClip.StartsWith("look"))))
            {pendingKettle=false;brain.ReturnToBase(now);player.ResetPose();BeginKettleTransition(true,now);PaintKettle(now);}
        }
        if(now>speechUntil&&speech.Length>0){speech="";dirty=true;}
        if(lastPresented!=player.Presented||dirty)DrawSurface();
    }
    void DrawSurface()
    {
        surface??=new Surface(ClientSize.Width,ClientSize.Height);
        raster??=new SharpRaster((int)(192*scale),(int)(208*scale));
        if(lastPresented!=player.Presented){raster.Update(player.Pixels);lastPresented=player.Presented;}
        var g=surface.Graphics;g.ResetTransform();g.Clear(Color.Transparent);
        int spriteX=(int)Math.Round(30*scale),spriteY=(int)Math.Round(82*scale);
        float opacity=kettle.Active?KettleMotion.Opacity(kettle.Frame(144)/143.0):1;
        if(opacity<1)
        {
            using var attributes=new ImageAttributes();attributes.SetColorMatrix(new ColorMatrix{Matrix33=opacity});
            g.DrawImage(raster.Image,new Rectangle(spriteX,spriteY,raster.Image.Width,raster.Image.Height),0,0,raster.Image.Width,raster.Image.Height,GraphicsUnit.Pixel,attributes);
            // The prop remains solid while the complete water drawing is translucent.
            var state=g.Save();g.SetClip(new Rectangle(spriteX+(int)(112*scale),spriteY+(int)(129*scale),(int)(80*scale),(int)(79*scale)));
            g.DrawImageUnscaled(raster.Image,spriteX,spriteY);g.Restore(state);
        }
        else g.DrawImageUnscaled(raster.Image,spriteX,spriteY);
        g.ScaleTransform((float)scale,(float)scale);
        if(speech.Length>0)
        {
            using var path=Rounded(new RectangleF(4,2,244,64),12);g.SmoothingMode=SmoothingMode.AntiAlias;
            using var fill=new SolidBrush(Color.FromArgb(247,246,246,231));using var pen=new Pen(Color.FromArgb(161,180,133));g.FillPath(fill,path);g.DrawPath(pen,path);
            using var ink=new SolidBrush(Color.FromArgb(46,65,55));using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
            g.DrawString(speech,speechFont,ink,new RectangleF(14,8,224,52),format);
        }
        surface.Present(this);dirty=false;
    }
    static GraphicsPath Rounded(RectangleF r,float radius)
    {var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    void Say(string value,double duration){if(KettleBusy)return;speech=value;speechUntil=clock.Elapsed.TotalSeconds+duration;dirty=true;if(brain.Paused||kettle.Hidden)DrawSurface();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        if(e.Y<74*scale&&speech.Length>0){ShowDashboard();return;}
        down=Cursor.Position;dragOrigin=Location;Capture=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(down is not{} start)return;Point pos=Cursor.Position;int dx=pos.X-start.X,dy=pos.Y-start.Y;
        if(!dragging&&dx*dx+dy*dy>49){dragging=true;if(!KettleBusy)brain.BeginDrag(clock.Elapsed.TotalSeconds,dx>=0);}
        if(dragging)Location=new Point(dragOrigin.X+dx,dragOrigin.Y+dy);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        // The assigned ContextMenuStrip is opened by WinForms on WM_CONTEXTMENU.
        base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||down==null)return;bool moved=dragging;dragging=false;down=null;Capture=false;
        if(moved)FinishDrop();
        else if(KettleBusy)SetKettle(false);
        else if(brain.Greet(clock.Elapsed.TotalSeconds))Say(interactionLines.Next(),4.5);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {base.OnMouseCaptureChanged(e);if(!Capture&&dragging){dragging=false;down=null;if(brain.Dragging)brain.EndDrag(clock.Elapsed.TotalSeconds);ClampPosition();SaveSettings();}}
    void OnStatus(CodexSnapshot snapshot)
    {
        bool changed=status!=snapshot.State;status=snapshot.State;statusDetail=snapshot.Detail;
        brain.SetTask(status is "completed" or "failed" or "interrupted"?"idle":status,clock.Elapsed.TotalSeconds);
        var events=snapshot.Notifications??[];
        if(events.Length>0&&!KettleBusy)
        {
            string reaction=events.Contains("failed")?"failed":events.Contains("completed")?"completed":"interrupted";
            brain.React(reaction,clock.Elapsed.TotalSeconds);
            Notify(reaction=="completed"?$"{events.Count(x=>x=="completed")} 个任务完成啦，博士，看看结果吧。":reaction=="failed"?"任务遇到了一点问题，请查看 Codex。":"任务已中断，我陪你稍作停留。",10);
        }
        else if(!KettleBusy&&changed && status=="thinking")Notify("正在思考 / 执行…我会仔细观察。",5);
        else if(!KettleBusy&&changed && status is "offline" or "unavailable")Notify(status=="offline"?"Codex 已离线，我会继续陪着你。":"Codex 状态暂不可用，请稍后查看。",6);
        tray.Text=("缪尔赛思 · "+statusDetail)[..Math.Min(63,("缪尔赛思 · "+statusDetail).Length)];RefreshPanels();
    }
    ContextMenuStrip Menu()
    {
        var menu=WaterUi.Menu();
        ToolStripMenuItem Item(string text,Action action){var item=new ToolStripMenuItem(text,null,(_,_)=>action());menu.Items.Add(item);return item;}
        Item("观察手记 · 状态与额度",ShowDashboard);Item("打个招呼",()=>{if(KettleBusy)PlayGesture("wave");else if(brain.Greet(clock.Elapsed.TotalSeconds))Say(interactionLines.Next(),4.5);});
        menu.Items.Add(new ToolStripSeparator());
        var pause=Item("暂停动作",TogglePause);
        var health=Item("喝水与休息提醒",()=>{reminders.Enabled=!reminders.Enabled;Say(reminders.Enabled?"喝水和休息提醒已开启。":"健康提醒已关闭，本次运行有效。",4);});
        var kettleVoice=Item("进出水壶语音",ToggleKettleVoice);
        var onTop=Item("保持置顶",()=>{TopMost=!TopMost;SaveSettings();});
        var sizes=new ToolStripMenuItem("显示大小");
        foreach(double value in new[]{1.0,1.25,1.5}){var item=new ToolStripMenuItem($"{value*100:0}%",null,(_,_)=>SetScale(value)){Tag=value,Padding=new Padding(8,7,12,7)};sizes.DropDownItems.Add(item);}
        menu.Items.Add(sizes);menu.Items.Add(new ToolStripSeparator());
        var conceal=Item("藏进热水壶",()=>SetKettle(!WantsKettle));Item("退出桌宠",Close);
        menu.Opening+=(_,_)=>{actionPalette?.HidePreview();card?.Hide();nearCardSince=-1;conceal.Text=WantsKettle?"取消隐藏 · 从壶中出来":"藏进热水壶";pause.Text=brain.Paused?"恢复动作":"暂停动作";health.Checked=reminders.Enabled;kettleVoice.Checked=kettleVoiceEnabled;onTop.Checked=TopMost;foreach(ToolStripMenuItem item in sizes.DropDownItems)item.Checked=(double)item.Tag! == scale;};
        return menu;
    }
    void SetupTray()
    {
        tray.Icon=new Icon(Path.Combine(AppContext.BaseDirectory,"assets","pet.ico"));tray.Text="缪尔赛思 · 独立陪伴中";
        var menu=WaterUi.Menu();menu.Items.Add("取消隐藏 · 从壶中出来",null,(_,_)=>SetKettle(false));menu.Items.Add("观察手记 · 状态与额度",null,(_,_)=>ShowDashboard());menu.Items.Add("暂停 / 恢复动作",null,(_,_)=>TogglePause());menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出桌宠",null,(_,_)=>Close());
        menu.Opening+=(_,_)=>{actionPalette?.HidePreview();card?.Hide();nearCardSince=-1;};tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowDashboard();tray.Visible=true;
    }
    void TogglePause(){brain.Paused=!brain.Paused;if(brain.Paused&&!kettle.Moving&&!pendingKettle)StopClock();else StartClock();Say(brain.Paused?"安静陪伴中，右键可恢复动作。":"我回来啦。",3);}
    void SetScale(double value)
    {scale=value;ClientSize=new Size((int)(252*value),(int)(290*value));surface?.Dispose();surface=null;raster?.Dispose();raster=null;lastPresented=-1;dirty=true;ClampPosition();SaveSettings();if(brain.Paused||kettle.Hidden)DrawSurface();}
    void LoadSettings()
    {
        var area=Screen.PrimaryScreen!.WorkingArea;Location=new Point(area.Right-Width-35,area.Bottom-Height-15);
        try{var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(CodexLink.Data,"settings-native.json")));if(s!=null){Location=new Point(s.X,s.Y);TopMost=s.OnTop;kettleVoiceEnabled=s.KettleVoice;SetScale(Math.Clamp(s.Scale,1,1.5));}}
        catch(Exception e)when(e is IOException or JsonException){}ClampPosition();
    }
    void SaveSettings(){if(!smoke)File.WriteAllText(Path.Combine(CodexLink.Data,"settings-native.json"),JsonSerializer.Serialize(new Settings(Left,Top,scale,TopMost,kettleVoiceEnabled)));}
    void ClampPosition(){var area=Screen.FromRectangle(Bounds).WorkingArea;Left=Math.Clamp(Left,area.Left,Math.Max(area.Left,area.Right-Width));Top=Math.Clamp(Top,area.Top,Math.Max(area.Top,area.Bottom-Height));}
    void ShowDashboard()
    {
        card?.Hide();nearCardSince=-1;
        if(dashboard!=null&&!dashboard.IsDisposed){if(dashboard.WindowState==FormWindowState.Minimized)dashboard.WindowState=FormWindowState.Normal;dashboard.Show();dashboard.BringToFront();dashboard.Activate();return;}
        var journal=new JournalForm(async()=>{await link.RefreshQuota();RefreshPanels();},ShowActionPalette);dashboard=journal;
        updateDashboard=()=>journal.UpdateContent(statusDetail,link.Quotas,link.QuotaUpdated,link.QuotaNote);
        dashboard.FormClosed+=(_,_)=>{dashboard=null;updateDashboard=null;nearCardSince=-1;};updateDashboard();dashboard.Show(this);dashboard.BringToFront();dashboard.Activate();
    }
    void ShowActionPalette()
    {
        card?.Hide();
        if(actionPalette==null||actionPalette.IsDisposed)
        {
            actionPalette=new ActionPalette(sprites,PlayGesture);actionPalette.FormClosed+=(_,_)=>actionPalette=null;
            ActionPalette.PlaceBeside(actionPalette,dashboard?.Bounds??Bounds);
        }
        actionPalette.UpdatePlayback(brain.Action,brain.PendingAction,brain.Paused);actionPalette.Show(this);actionPalette.Activate();
    }
    void PlayGesture(string action)
    {
        if(kettle.Active){afterKettleGesture=action;SetKettle(false);return;}
        if(pendingKettle)SetKettle(false);
        if(!brain.PlayGesture(action,clock.Elapsed.TotalSeconds))return;
        if(!Visible)Show();StartClock();
        actionPalette?.UpdatePlayback(brain.Action,brain.PendingAction,brain.Paused);
    }
    async Task RunSmoke()
    {
        string output=args.SkipWhile(x=>x!="--smoke-test").Skip(1).FirstOrDefault()??Path.Combine(CodexLink.Data,"smoke");Directory.CreateDirectory(output);await link.RefreshQuota();SetScale(args.Contains("--scale150")?1.5:1.0);
        using var proc=Process.GetCurrentProcess();double startCpu=proc.TotalProcessorTime.TotalSeconds,start=clock.Elapsed.TotalSeconds;times.Clear();long firstTicks=ticks,firstFrames=player.Presented;HashSet<string> actions=[];
        for(int i=0;i<530;i++)
        {
            double now=clock.Elapsed.TotalSeconds;
            if(i<100)brain.Tick(now,500,500,500);
            else if(i<245){double a=(i-100)*2*Math.PI/145;brain.Tick(now,200*Math.Sin(a),-200*Math.Cos(a),30);}
            else if(i==245)brain.Greet(now);
            else if(i is 280 or 325 or 372 or 430){string action=i switch{280=>"leaf",325=>"water-observe",372=>"drink",_=>"stretch"};brain.Request(action,now,Behavior.GestureDuration(action));}
            else if(i==490)OnStatus(new("thinking",1,"1 个任务执行中",DateTimeOffset.Now,["completed"]));else brain.Tick(now,500,500,500);
            actions.Add(brain.Action);
            if(i%50==0)surface?.Canvas.Save(Path.Combine(output,$"frame-{i:000}.png"),ImageFormat.Png);
            if(i==400){ShowDashboard();await Task.Delay(100);if(dashboard!=null){using var image=new Bitmap(dashboard.Width,dashboard.Height);dashboard.DrawToBitmap(image,new Rectangle(0,0,dashboard.Width,dashboard.Height));image.Save(Path.Combine(output,"dashboard.png"));}}
            if(i==410)dashboard?.Close();await Task.Delay(100);
        }
        proc.Refresh();double elapsed=clock.Elapsed.TotalSeconds-start,cpu=proc.TotalProcessorTime.TotalSeconds-startCpu;var gaps=times.Zip(times.Skip(1),(a,b)=>b-a).Order().ToArray();
        File.WriteAllText(Path.Combine(output,"runtime.json"),JsonSerializer.Serialize(new{elapsedSeconds=elapsed,cpuSeconds=cpu,oneCoreCpuPercent=cpu/elapsed*100,logicalProcessors=Environment.ProcessorCount,workingSetBytes=proc.WorkingSet64,privateBytes=proc.PrivateMemorySize64,cacheBytes=sprites.Bytes,spriteLoads=sprites.Loads,renderTicks=ticks-firstTicks,presentedFrames=player.Presented-firstFrames,scheduledFps=times.Count/elapsed,p95FrameIntervalMs=gaps[(int)(gaps.Length*.95)]*1000,observedActions=actions,realDesktopWindow=true,displayScale=scale,renderer="Win32 per-pixel-alpha, single physical-pixel raster",interaction="Engine input injection; no synthetic mouse events sent"},new JsonSerializerOptions{WriteIndented=true}));Close();
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {closing=true;if(!smoke)KettleAudio.Stop();heartbeat.Stop();heartbeat.Dispose();card?.Dispose();actionPalette?.Dispose();StopClock();link.Dispose();tray.Visible=false;tray.Dispose();dashboard?.Close();surface?.Dispose();raster?.Dispose();speechFont.Dispose();SaveSettings();base.OnFormClosed(e);}
}
