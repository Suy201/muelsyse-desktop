using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Muelsyse;

internal sealed class Surface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct PointI{public int X,Y;public PointI(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)] struct SizeI{public int W,H;public SizeI(int w,int h){W=w;H=h;}}
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend{public byte Op,Flags,Alpha,Format;}
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo{public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int X,Y;public uint Colors,Important;}
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr h);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr h);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,ref PointI position,ref SizeI size,IntPtr dc,ref PointI source,int key,ref Blend blend,int flags);
    private readonly IntPtr dc,bitmap,old;
    public Bitmap Canvas{get;}
    public Graphics Graphics{get;}
    public Surface(int width,int height)
    {
        dc=CreateCompatibleDC(IntPtr.Zero);
        var info=new BitmapInfo{Size=40,Width=width,Height=-height,Planes=1,Bits=32};
        bitmap=CreateDIBSection(dc,ref info,0,out var bits,IntPtr.Zero,0);
        if(bitmap==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
        old=SelectObject(dc,bitmap);Canvas=new(width,height,width*4,PixelFormat.Format32bppPArgb,bits);Graphics=Graphics.FromImage(Canvas);
        Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
    }
    public void Present(Form form)
    {
        var position=new PointI(form.Left,form.Top);var size=new SizeI(Canvas.Width,Canvas.Height);var source=new PointI(0,0);var blend=new Blend{Alpha=255,Format=1};
        if(!UpdateLayeredWindow(form.Handle,IntPtr.Zero,ref position,ref size,dc,ref source,0,ref blend,2))throw new System.ComponentModel.Win32Exception();
    }
    public void Dispose(){Graphics.Dispose();Canvas.Dispose();SelectObject(dc,old);DeleteObject(bitmap);DeleteDC(dc);}
}

// A per-process high-resolution waitable timer; no global timer-resolution changes.
internal sealed class FrameClock(Action tick) : IDisposable
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attr,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetWaitableTimer(SafeWaitHandle timer,ref long due,int period,IntPtr callback,IntPtr arg,bool resume);
    readonly CancellationTokenSource cancel=new();
    Task? worker;
    public void Start()
    {
        worker=Task.Run(()=>
        {
            using var handle=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);
            if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception();
            using var waiter=new EventWaitHandle(false,EventResetMode.AutoReset){SafeWaitHandle=handle};
            WaitHandle[] waits=[waiter,cancel.Token.WaitHandle];
            var clock=System.Diagnostics.Stopwatch.StartNew();double next=0;
            while(!cancel.IsCancellationRequested)
            {
                next+=1.0/30;double delay=next-clock.Elapsed.TotalSeconds;
                if(delay<=0){next=clock.Elapsed.TotalSeconds;delay=.001;}
                long due=-(long)Math.Max(1,delay*10000000);
                if(!SetWaitableTimer(handle,ref due,0,IntPtr.Zero,IntPtr.Zero,false))break;
                if(WaitHandle.WaitAny(waits)!=0)break;
                tick();
            }
        });
    }
    public void Dispose(){cancel.Cancel();worker?.Wait(500);cancel.Dispose();}
}
