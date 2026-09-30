namespace Muelsyse;

internal static class KettleChecks
{
    public static void Run(Action<bool,string> check)
    {
        var motion=new KettleMotion();
        check(!motion.Active&&!motion.Moving&&motion.Frame(144)==0,"kettle starts as the exact normal endpoint");
        check(KettleMotion.Opacity(0)==1&&KettleMotion.Opacity(1)==1&&KettleMotion.Opacity(.91)==1,"normal and kettle endpoints remain fully opaque");
        check(Math.Abs(KettleMotion.Opacity(1.0/3)-.68)<.0001,"complete Flowing Shape has a transparent render layer");
        motion.Request(true,10);motion.Advance(11.2);
        check(Math.Abs(motion.Progress-.25)<.00001&&motion.Moving,"entry progresses at the authored duration");
        int at=motion.Frame(144);motion.Request(false,11.2);
        check(motion.Frame(144)==at,"mid-entry cancellation does not jump to another pose");
        motion.Advance(11.8);check(Math.Abs(motion.Progress-.125)<.00001,"cancel returns along the same frames at the same speed");
        motion.Request(true,11.8);motion.Advance(16);
        check(motion.Hidden&&motion.Frame(144)==143,"repeated reversal reaches the exact closed-kettle endpoint");
        motion.Request(true,20);motion.Advance(50);check(motion.Hidden,"repeated hide never restarts a completed transition");
        motion.Request(false,50);motion.Advance(54.8);
        check(!motion.Active&&motion.Frame(144)==0,"exit restores the exact original character frame");
        motion.Request(true,100);motion.Advance(1000);check(motion.Hidden,"long scheduling gap clamps to the endpoint");
        foreach(var area in new[]{new Rectangle(0,0,1920,1040),new Rectangle(-1920,-200,1920,1080)})
        foreach(double scale in new[]{1.0,1.25,1.5})
        {
            int w=(int)(252*scale),h=(int)(290*scale),enter=(int)(18*scale),leave=(int)(65*scale);
            var middle=new Rectangle(area.Left+300,area.Top+250,w,h);
            var positions=new[]{new Rectangle(area.Left+enter,middle.Top,w,h),new Rectangle(area.Right-w-enter,middle.Top,w,h),new Rectangle(middle.Left,area.Top+enter,w,h),new Rectangle(middle.Left,area.Bottom-h-enter,w,h)};
            check(positions.All(b=>KettleMotion.HideAfterDrop(false,b,area,scale)),"all four edges hide at scale "+scale+" monitor "+area.Left);
            var near=new Rectangle(area.Left+(int)(40*scale),area.Top+200,w,h);
            check(!KettleMotion.HideAfterDrop(false,near,area,scale)&&KettleMotion.HideAfterDrop(true,near,area,scale),"edge hysteresis prevents jitter at scale "+scale+" monitor "+area.Left);
            var away=new Rectangle(area.Left+leave,area.Top+leave,w,h);
            check(!KettleMotion.HideAfterDrop(true,away,area,scale),"dragging the kettle inward restores it at scale "+scale+" monitor "+area.Left);
        }
        var brain=new Behavior(3){Paused=true};brain.ReturnToBase(10);
        check(brain.Paused&&!brain.Dragging&&!brain.Looking,"restoring from kettle preserves the user's pause setting");
        brain.Paused=false;brain.SetTask("thinking",11);brain.ReturnToBase(12);
        check(brain.Action=="working"&&brain.TaskState=="thinking","restoring from kettle preserves task state");
        check(Behavior.Gestures.Count==12&&!Behavior.Gestures.Any(g=>g.Id=="kettle"),"kettle transition does not enter the random idle gesture pool");
    }
}

internal sealed partial class PetForm
{
    async Task CheckKettleUi(Action<bool,string> check,string output)
    {
        async Task WaitForEndpoint(bool hidden)
        {
            double deadline=clock.Elapsed.TotalSeconds+8;
            while(clock.Elapsed.TotalSeconds<deadline&&(hidden?!kettle.Hidden:kettle.Active||pendingKettle))await Task.Delay(50);
        }
        dashboard?.Close();actionPalette?.Close();ContextMenuStrip?.Close();card?.Hide();
        brain.Paused=false;brain.ReturnToBase(clock.Elapsed.TotalSeconds);
        SetKettle(true);await Task.Delay(250);
        check(Visible&&kettle.Active,"hide starts a visible transformation instead of hiding the native window");
        await Task.Delay(500);int before=player.LastFrame;SetKettle(false);await Task.Delay(100);
        check(player.LastFrame<=before&&player.LastFrame>=before-5,"cancel during entry reverses continuously");
        await Task.Delay(1200);check(!kettle.Active&&!pendingKettle,"cancelled entry returns to the normal state");
        brain.Paused=true;SetKettle(true);await WaitForEndpoint(true);
        check(kettle.Hidden&&Visible&&brain.Paused&&frameClock==null,"paused pet still enters kettle and stops the animation clock when hidden");
        surface?.Canvas.Save(Path.Combine(output,"kettle-hidden.png"));
        var point=PointToScreen(new Point(110,230));ContextMenuStrip!.Show(point);await Task.Delay(100);
        check(ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Any(i=>i.Text?.Contains("取消隐藏")==true),"kettle right-click menu offers an exit");
        ContextMenuStrip.Close();SetKettle(false);await WaitForEndpoint(false);
        File.WriteAllText(Path.Combine(output,"kettle-exit-state.json"),System.Text.Json.JsonSerializer.Serialize(new{kettle.Progress,kettle.TargetHidden,kettle.Active,brain.Paused,clockRunning=frameClock!=null,player.LastClip,player.LastFrame}));
        check(!kettle.Active&&brain.Paused&&frameClock==null,"exit preserves pause and rests on the original endpoint");
        surface?.Canvas.Save(Path.Combine(output,"kettle-restored.png"));
        brain.Paused=false;brain.ReturnToBase(clock.Elapsed.TotalSeconds);StartClock();
        var area=Screen.FromRectangle(Bounds).WorkingArea;
        Location=new Point(area.Left+5,area.Top+100);FinishDrop();await WaitForEndpoint(true);
        check(kettle.Hidden,"release at a work-area edge hides into kettle");
        Location=new Point(area.Left+100,area.Top+100);FinishDrop();await WaitForEndpoint(false);
        check(!kettle.Active,"dragging the kettle away from the edge restores the pet");
        check(Behavior.Gestures.Count==12,"existing twelve actions remain available after repeated hide/show");
    }
}
