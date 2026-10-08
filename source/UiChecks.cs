using System.Drawing.Imaging;
using System.Text.Json;

namespace Muelsyse;

internal sealed partial class PetForm
{
    async Task CheckUi()
    {
        string output=args[Array.IndexOf(args,"--ui-check")+1];Directory.CreateDirectory(output);
        // Keep the isolated test popup away from a simultaneously running pet.
        var testArea=Screen.FromRectangle(Bounds).WorkingArea;Location=new Point(testArea.Left+40,testArea.Bottom-Height-40);
        StartClock();
        var checks=new List<object>();int failures=0;
        void Check(bool ok,string name){checks.Add(new{name,ok});if(!ok)failures++;}
        void Capture(Control control,string name)
        {
            using var image=new Bitmap(control.Width,control.Height);
            control.DrawToBitmap(image,new Rectangle(0,0,control.Width,control.Height));image.Save(Path.Combine(output,name+".png"),ImageFormat.Png);
        }
        int newTasks=0,voiceRequests=0;
        int connections=0;
        using(var firstRun=new JournalForm(()=>Task.CompletedTask,()=>{},()=>connections++))
        {
            firstRun.UpdateContent("等待 Codex 的本地任务记录",[],null,CodexConnection.LoginNote);firstRun.Show(this);firstRun.Connect.PerformClick();
            Check(connections==1&&firstRun.Connect.Right<firstRun.ClientSize.Width,"first-run journal offers a working connection button without opening external apps in tests");
            Capture(firstRun,"first-run-connection");firstRun.Close();
        }
        using(var companion=new HoverCard(()=>{},()=>{},()=>newTasks++,()=>voiceRequests++))
        {
            companion.Show(this);companion.NewTask.PerformClick();companion.Voice.PerformClick();
            Check(newTasks==1&&voiceRequests==1,"companion shortcuts dispatch distinct callbacks without opening Codex or the microphone in tests");
            foreach(string reminderText in new[]{"","博士，休息一下吧。"})
            {
                companion.UpdateContent("独立陪伴中",[],null,"",reminderText);
                Check(companion.NewTask.Bottom<=companion.Details.Top&&companion.Voice.Right<=companion.ClientSize.Width&&companion.Details.Bottom<companion.Height,"companion shortcut rows fit with reminder length "+reminderText.Length);
            }
            companion.Close();
        }
        Point near=PointToScreen(new Point(100,150));int opened=0;var reasons=new List<string>();
        ContextMenuStrip!.Opened+=(_,_)=>opened++;
        ContextMenuStrip.Closed+=(_,e)=>reasons.Add(e.CloseReason.ToString());
        // WinForms receives mouse-up, followed by WM_CONTEXTMENU for one right click.
        OnMouseUp(new MouseEventArgs(MouseButtons.Right,1,100,150,0));
        var message=Message.Create(Handle,0x007b,Handle,(IntPtr)((near.Y<<16)|(near.X&0xffff)));
        WndProc(ref message);await Task.Delay(100);
        int singleRightClickOpenCount=opened;
        Check(opened==1,"one right click opens the context menu exactly once");
        for(int i=0;i<8;i++){Heartbeat(near);await Task.Delay(250);}
        Check(ContextMenuStrip.Visible,"context menu survives two seconds of companion heartbeats");
        if(!ContextMenuStrip.Visible)ContextMenuStrip.Show(near);
        var sizes=ContextMenuStrip.Items.OfType<ToolStripMenuItem>().First(i=>i.HasDropDownItems);
        sizes.Select();sizes.ShowDropDown();await Task.Delay(100);
        for(int i=0;i<4;i++){Heartbeat(near);await Task.Delay(250);}
        Check(ContextMenuStrip.Visible&&sizes.DropDown.Visible,"size submenu remains open while choosing an option");
        sizes.HideDropDown();Capture(ContextMenuStrip,"menu");
        ContextMenuStrip.Close();ContextMenuStrip.Items[0].PerformClick();await Task.Delay(100);
        for(int i=0;i<8;i++){Heartbeat(near);await Task.Delay(250);}
        Check(dashboard?.Visible==true,"details stay open while the pointer returns to the pet");
        Check(card?.Visible!=true,"hover card does not cover the open details window");
        if(dashboard is JournalForm details)
        {
            Capture(details,"details-unavailable");
            var samples=new List<Quota>{new("codex",98,10080,1790788920)};
            details.UpdateContent("1 个任务正在思考 / 执行",samples,DateTimeOffset.Now,"来自 Codex 账户接口 · 各周期独立计算");
            Check(!details.Controls.Find("quotas",true).OfType<FlowLayoutPanel>().Any(f=>f.HorizontalScroll.Visible||f.VerticalScroll.Visible),"one quota is fully visible without scrollbars");
            Capture(details,"details");
            samples.Insert(0,new("codex",24,300,1790742600));
            details.UpdateContent("2 个任务正在思考 / 执行",samples,DateTimeOffset.Now,"来自 Codex 账户接口 · 各周期独立计算");Capture(details,"details-two-windows");
            Check(!details.Controls.Find("quotas",true).OfType<FlowLayoutPanel>().Any(f=>f.HorizontalScroll.Visible||f.VerticalScroll.Visible),"both main quota windows fit without scrollbars");
            samples.Add(new("review",0,10080,0));samples.Add(new("extra",100,300,0));
            details.UpdateContent("任务已完成",samples,DateTimeOffset.Now,"暂不可用，显示缓存额度");Capture(details,"details-four-windows");
            Check(details.Controls.Find("quotas",true).OfType<FlowLayoutPanel>().Any(f=>f.VerticalScroll.Visible&&!f.HorizontalScroll.Visible),"four quota windows scroll vertically without clipping values or the refresh button");
        }
        dashboard?.Close();await Task.Delay(100);
        ShowCard();await Task.Delay(100);
        if(card is{} hover)
        {
            hover.UpdateContent("1 个任务正在思考 / 执行",[new("codex",98,10080,0)],DateTimeOffset.Now,"来自 Codex 账户接口","",false);
            Capture(hover,"hover");Check(hover.Opacity==.9,"companion uses a translucent native window");
            hover.UpdateContent("独立陪伴中",[],null,"","博士，喝一口水吧，水分很重要哦。",true);Capture(hover,"hover-reminder");
            hover.Later.PerformClick();for(int i=0;i<4;i++){Heartbeat(near);await Task.Delay(250);}
            Check(!hover.Visible,"dismissed companion stays hidden while pointer remains near");
            Heartbeat(new Point(-10000,-10000));Heartbeat(near);await Task.Delay(300);Heartbeat(near);
            Check(hover.Visible,"companion returns after pointer leaves and comes back");
            hover.Details.PerformClick();await Task.Delay(100);
            Check(dashboard?.Visible==true,"hover details button opens the journal");
        }
        dashboard?.Close();
        ContextMenuStrip.Show(near);var escape=Message.Create(ContextMenuStrip.Handle,0x0100,(IntPtr)Keys.Escape,IntPtr.Zero);ContextMenuStrip.PreProcessMessage(ref escape);await Task.Delay(100);
        Check(!ContextMenuStrip.Visible,"Escape dismisses the context menu normally");
        var trayCloseReasons=new List<string>();tray.ContextMenuStrip!.Closed+=(_,e)=>trayCloseReasons.Add(e.CloseReason.ToString());
        // An explicit owner keeps the synthetic popup attached to this test's window.
        Activate();await Task.Delay(100);
        tray.ContextMenuStrip.Show(this,PointToClient(near));Notify("博士，喝一口水吧。",2,"water");
        for(int i=0;i<4;i++){Heartbeat(near);await Task.Delay(250);}
        Check(tray.ContextMenuStrip.Visible&&card?.Visible!=true,"tray menu survives a reminder and companion heartbeats");tray.ContextMenuStrip.Close();
        bool pausedBefore=brain.Paused;var pause=ContextMenuStrip.Items.OfType<ToolStripMenuItem>().First(i=>i.Text=="暂停动作");pause.PerformClick();Check(brain.Paused!=pausedBefore,"menu action can pause animation");pause.PerformClick();
        Check(brain.Paused==pausedBefore,"menu action can resume animation");
        ShowDashboard();await Task.Delay(100);var actionJournal=(JournalForm)dashboard!;
        Check(actionJournal.ClientSize.Width==420,"journal keeps its original compact width");
        actionJournal.Actions.PerformClick();await Task.Delay(100);var palette=actionPalette!;
        Check(palette.Visible&&palette!=dashboard&&!palette.Preview.PreviewVisible,"action bar opens independently without showing a preview before hover");
        Check(palette.ActionButtons.Controls.Cast<Button>().Select(b=>(string)b.Tag!).SequenceEqual(Behavior.Gestures.Select(g=>g.Id)),"action buttons follow the shared gesture catalog exactly");
        Check(!palette.ActionButtons.HorizontalScroll.Visible&&!palette.ActionButtons.VerticalScroll.Visible,"all twelve action buttons fit without scrolling");
        Capture(actionJournal,"compact-journal");Capture(palette,"actions");
        foreach(var gesture in Behavior.Gestures)
        {
            var button=palette.ActionButtons.Controls.Cast<Button>().Single(b=>(string)b.Tag! == gesture.Id);
            string beforeAction=brain.Action;double beforeStart=brain.ActionStarted;palette.HoverGesture(gesture.Id);await Task.Delay(240);
            Check(palette.Preview.PreviewVisible&&palette.Preview.GestureId==gesture.Id,gesture.Id+" hover opens the matching preview");
            long frames=palette.Preview.RenderedFrames;await Task.Delay(120);
            Check(palette.Preview.RenderedFrames>frames&&brain.Action==beforeAction&&brain.ActionStarted==beforeStart,gesture.Id+" preview animates without playing the desktop pet");
            Check(!palette.Preview.HasForeground,gesture.Id+" preview does not take keyboard focus");
            Capture(palette.Preview,"preview-"+gesture.Id);
            button.PerformClick();StopClock();double at=brain.ActionStarted;player.Draw(brain,at+gesture.Duration/2);DrawSurface();
            Check(brain.Action==gesture.Id&&player.LastClip==gesture.Id&&palette.Visible,gesture.Id+" button plays its animation and keeps the action window open");
            surface!.Canvas.Save(Path.Combine(output,"action-"+gesture.Id+".png"),ImageFormat.Png);
            palette.UpdatePlayback(brain.Action,brain.PendingAction,brain.Paused);
            if(gesture.Id=="water-observe")Capture(palette,"actions-playing");
            brain.Tick(at+gesture.Duration+.001,500,500,500);
        }
        palette.HidePreview();long stoppedFrames=palette.Preview.RenderedFrames;await Task.Delay(100);
        Check(!palette.Preview.PreviewVisible&&palette.Preview.RenderedFrames==stoppedFrames,"leaving an action hides the preview and stops rendering");
        brain.Paused=true;palette.ActionButtons.Controls.OfType<Button>().First().PerformClick();
        Check(!brain.Paused&&frameClock!=null,"an action button resumes paused playback");
        actionJournal.Close();Check(palette.Visible,"action window can remain open independently of the journal");
        var previewWindow=palette.Preview;palette.Close();Check(previewWindow.IsDisposed,"closing the action window releases its preview and timer");
        await CheckKettleUi(Check,output);
        File.WriteAllText(Path.Combine(output,"ui.json"),JsonSerializer.Serialize(new{ok=failures==0,failures,checks,singleRightClickOpenCount,menuOpenCount=opened,menuCloseReasons=reasons,trayCloseReasons,scope="Real WinForms windows and active animation clock; native message dispatch and injected heartbeat pointer; no system mouse input"},new JsonSerializerOptions{WriteIndented=true}));
        Environment.ExitCode=failures==0?0:1;
    }
}
