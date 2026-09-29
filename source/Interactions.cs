using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Muelsyse;

internal sealed class InteractionLines(int? seed=null)
{
    // The first three short excerpts are game dialogue; all other lines are original.
    // Transcription and attribution: work/desktop-companion/角色与动作设计.md.
    internal static readonly string[] Lines=["哈喽。","我在听哦。","吃糖果吗？",
        "博士，今天有什么有趣的发现？","水珠里的世界，也值得慢慢观察。",
        "忙完这一小段，陪我去看看绿叶吧。","思路打结了？我们换个角度看看。",
        "今天的观察手记，要不要一起写？","别急，我陪你把这件事做完。",
        "博士，记得给自己留一点休息时间。","这一滴清水，送给认真工作的你。",
        "你发现了吗？安静的时候也有新变化。"];
    readonly Random random=seed is{} value?new(value):new();int last=-1;
    internal string Next(){int i=random.Next(Lines.Length-(last<0?0:1));if(last>=0&&i>=last)i++;last=i;return Lines[i];}
}

internal static class CodexLaunch
{
    internal const string NewTaskUri="codex://threads/new?mode=codex";
    internal const string SettingsUri="codex://settings";
    internal static bool IsCodexFrontend(string? path)=>path!=null&&path.Contains(@"\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)&&path.EndsWith(@"\app\ChatGPT.exe",StringComparison.OrdinalIgnoreCase);
    internal static string? ReadVoiceBinding(string home)
    {
        string path=Path.Combine(home,"keybindings.json");if(!File.Exists(path))return null;
        using var doc=JsonDocument.Parse(File.ReadAllText(path));
        if(doc.RootElement.ValueKind!=JsonValueKind.Array)return null;
        foreach(var item in doc.RootElement.EnumerateArray())
            if(item.TryGetProperty("command",out var command)&&command.GetString()=="realtimeVoice")
                return item.TryGetProperty("key",out var key)&&key.ValueKind==JsonValueKind.String?key.GetString():null;
        return null;
    }
    // Only send a complete, explicitly configured chord; never guess a default.
    internal static ushort[]? ParseChord(string? binding)
    {
        if(string.IsNullOrWhiteSpace(binding))return null;
        var keys=new List<ushort>();bool modifier=false,main=false;
        foreach(string token in binding.Split('+'))
        {
            string part=token.Trim();ushort key=part.ToLowerInvariant() switch
            {"ctrl" or "control" or "cmdorctrl" or "commandorcontrol"=>0x11,"alt" or "option"=>0x12,"shift"=>0x10,"super" or "meta" or "win"=>0x5b,_=>0};
            if(key!=0)modifier=true;
            else if(!main&&part.Length==1&&char.IsAsciiLetterOrDigit(part[0])){key=char.ToUpperInvariant(part[0]);main=true;}
            else if(!main&&part.StartsWith('F')&&int.TryParse(part[1..],out int f)&&f is >=1 and <=24){key=(ushort)(0x70+f-1);main=true;}
            else return null;
            if(keys.Contains(key))return null;keys.Add(key);
        }
        return modifier&&main?keys.ToArray():null;
    }
    internal static void Open(string uri)=>Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});
    internal static string Voice()
    {
        var keys=ParseChord(ReadVoiceBinding(CodexLink.Home));
        if(keys==null){Open(SettingsUri);return "请在 Codex 设置 → 语音中设置语音聊天快捷键（如 Ctrl+Alt+V），之后点这里即可切换实时语音。";}
        var processes=Process.GetProcessesByName("ChatGPT");IntPtr window=IntPtr.Zero;int processId=0;
        foreach(var process in processes)
        {
            using(process)if(window==IntPtr.Zero&&process.MainWindowHandle!=IntPtr.Zero&&IsCodexFrontend(process.MainModule?.FileName))
            {window=process.MainWindowHandle;processId=process.Id;}
        }
        if(window==IntPtr.Zero){Open("codex://launch");return "已打开 Codex。请等待启动完成，再点语音对话。";}
        // Focus the known app before synthesizing its configured shortcut, so
        // an unavailable global registration cannot send a chord to another app.
        if(IsIconic(window))ShowWindow(window,9);
        SetForegroundWindow(window);GetWindowThreadProcessId(GetForegroundWindow(),out uint foreground);
        if(foreground!=processId)return "请先切换到 Codex 窗口，再点语音对话。";
        if(new[]{0x10,0x11,0x12,0x5b,0x5c}.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0))return "请松开键盘修饰键，再点语音对话。";
        var inputs=keys.Select(k=>Key(k,false)).Concat(keys.Reverse().Select(k=>Key(k,true))).ToArray();
        if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>())!=inputs.Length)throw new Win32Exception(Marshal.GetLastWin32Error());
        return "已发送 Codex 语音快捷键；再次点击可结束语音。首次使用请在 Codex 允许麦克风访问。";
    }
    static Input Key(ushort key,bool up)=>new(){Type=1,Data=new(){Keyboard=new(){VirtualKey=key,Flags=up?2u:0}}};
    [StructLayout(LayoutKind.Sequential)]struct Input{public uint Type;public InputData Data;}
    [StructLayout(LayoutKind.Explicit)]struct InputData{[FieldOffset(0)]public KeyboardInput Keyboard;[FieldOffset(0)]public MouseInput Mouse;}
    [StructLayout(LayoutKind.Sequential)]struct KeyboardInput{public ushort VirtualKey,Scan;public uint Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]struct MouseInput{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
}
