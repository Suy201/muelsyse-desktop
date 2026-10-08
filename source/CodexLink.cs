using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Muelsyse;

internal record Quota(string Name, double Used, int Minutes, long Reset);
internal record CodexSnapshot(string State, int Active, string Detail, DateTimeOffset Checked, string[]? Notifications=null);

internal static class ReadOnlySqlite
{
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    public static List<string[]> Query(string history, string state, string query)
    {
        int rc = sqlite3_open_v2(history, out var db, 1 | 0x40, IntPtr.Zero); // READONLY + URI
        try
        {
            if (rc != 0) throw new IOException("无法只读打开 Codex 状态库");
            sqlite3_busy_timeout(db, 200);
            var uri = new Uri(state).AbsoluteUri + "?mode=ro";
            Execute(db, "ATTACH DATABASE '" + uri.Replace("'", "''") + "' AS meta");
            return Execute(db, query);
        }
        finally { if (db != IntPtr.Zero) sqlite3_close(db); }
    }
    private static List<string[]> Execute(IntPtr db, string sql)
    {
        if (sqlite3_prepare_v2(db, sql, -1, out var stmt, IntPtr.Zero) != 0) throw new IOException("Codex 状态库结构不兼容");
        try
        {
            List<string[]> rows = [];
            int rc;
            while ((rc = sqlite3_step(stmt)) == 100)
                rows.Add(Enumerable.Range(0, sqlite3_column_count(stmt)).Select(i => Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, i)) ?? "").ToArray());
            if (rc != 101) throw new IOException("Codex 状态库暂时忙碌");
            return rows;
        }
        finally { sqlite3_finalize(stmt); }
    }
}

internal sealed class CodexLink : IDisposable
{
    readonly CancellationTokenSource cancel = new();
    readonly SemaphoreSlim quotaLock = new(1);
    readonly Dictionary<string, string> observed = new();
    bool initialized;
    long lastStatusRead;
    public static string Home => Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuelsysePet");
    public event Action<CodexSnapshot>? Changed;
    public event Action? QuotaChanged;
    public List<Quota> Quotas { get; private set; } = [];
    public DateTimeOffset? QuotaUpdated { get; private set; }
    public string QuotaNote { get; private set; } = "正在读取额度…";
    internal bool QuotaFresh { get; private set; }
    internal string? QuotaDiagnostic { get; private set; }
    internal TimeSpan RetryDelay=>QuotaFresh?TimeSpan.FromMinutes(5):TimeSpan.FromSeconds(30);
    public void Start()
    {
        // Verify the current account on every launch; never present an old cache as live.
        _ = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                try { Changed?.Invoke(ReadStatus()); }
                catch (Exception e) when (e is IOException or DllNotFoundException or UnauthorizedAccessException)
                { Changed?.Invoke(new("unavailable", 0, e.Message, DateTimeOffset.Now)); }
                try { await Task.Delay(2000, cancel.Token); } catch (OperationCanceledException) { break; }
            }
        });
        _ = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                await RefreshQuota();
                try { await Task.Delay(RetryDelay, cancel.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }
    public CodexSnapshot ReadStatus()
    {
        string history = Path.Combine(Home, "thread_history_1.sqlite"), state = Path.Combine(Home, "state_5.sqlite");
        if (!File.Exists(history) || !File.Exists(state)) return new("unavailable", 0, "等待 Codex 的本地任务记录", DateTimeOffset.Now);
        // Metadata only: never select user prompts, model text, tool output or credentials.
        var rows = ReadOnlySqlite.Query(history, state, """
            WITH recent AS (
              SELECT h.thread_id,h.turn_id,h.status,h.started_at,h.completed_at,t.updated_at,
                     ROW_NUMBER() OVER(PARTITION BY h.thread_id ORDER BY h.started_at DESC,h.rollout_ordinal DESC) AS rn
              FROM thread_turns h JOIN meta.threads t ON t.id=h.thread_id
              WHERE t.archived=0 AND t.agent_path IS NULL AND t.source NOT LIKE '%subagent%'
                AND h.started_at > strftime('%s','now')-172800
            ) SELECT thread_id,turn_id,status,started_at,completed_at,updated_at FROM recent WHERE rn=1
            """);
        bool desktopOpen=false;
        foreach(var process in Process.GetProcessesByName("ChatGPT").Concat(Process.GetProcessesByName("Codex")))
        {
            using(process){try{desktopOpen|=process.MainModule?.FileName?.Contains("OpenAI.Codex",StringComparison.OrdinalIgnoreCase)??false;}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}}
        }
        int active = 0, stale = 0; string terminal = "idle"; List<string> notifications=[];
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var row in rows)
        {
            string key = row[0] + ":" + row[1], status = row[2];
            long.TryParse(row[5], out long updated);
            if (status == "inProgress" && desktopOpen && now - updated < 1800) active++;
            else if(status=="inProgress"&&desktopOpen)stale++;
            long.TryParse(row[4],out long completed);
            bool previousRunning=observed.TryGetValue(key,out var previous)&&previous=="inProgress";
            bool unseenCompletion=!observed.ContainsKey(key)&&completed>=lastStatusRead;
            if (initialized && status!="inProgress" && (previousRunning||unseenCompletion))
                {terminal = status == "completed" ? "completed" : status == "failed" ? "failed" : "interrupted";notifications.Add(terminal);}
            observed[key] = status;
        }
        initialized = true;lastStatusRead=now;
        if (observed.Count > 400) { var keep=rows.Select(r=>r[0]+":"+r[1]).ToHashSet(); foreach(var key in observed.Keys.Where(k=>!keep.Contains(k)).ToArray()) observed.Remove(key); }
        if (active > 0) return new("thinking", active, $"{active} 个任务正在思考 / 执行", DateTimeOffset.Now,notifications.ToArray());
        if (terminal != "idle") return new(terminal, 0, terminal == "completed" ? "思考完毕，结果已准备好" : terminal == "failed" ? "任务遇到问题，请查看 Codex" : "任务已中断", DateTimeOffset.Now,notifications.ToArray());
        if(stale>0)return new("unavailable",0,"部分运行状态已过期，请在 Codex 中确认",DateTimeOffset.Now);
        return new(desktopOpen ? "idle" : "offline", 0, desktopOpen ? "Codex 暂无运行任务" : "Codex 未运行 · 独立陪伴中", DateTimeOffset.Now);
    }
    public static List<Quota> ParseQuota(JsonElement result)
    {
        List<Quota> list=[];
        IEnumerable<JsonElement> buckets;
        if (result.TryGetProperty("rateLimitsByLimitId", out var by) && by.ValueKind == JsonValueKind.Object && by.EnumerateObject().Any())
            buckets=by.EnumerateObject().Select(p=>p.Value).ToArray();
        else if(result.TryGetProperty("rateLimits",out var legacy) && legacy.ValueKind==JsonValueKind.Object) buckets=[legacy];
        else return list;
        foreach(var bucket in buckets)
        {
            string name=bucket.TryGetProperty("limitName",out var label)&&label.ValueKind==JsonValueKind.String?label.GetString()!:
                bucket.TryGetProperty("limitId",out var id)&&id.ValueKind==JsonValueKind.String?id.GetString()!:"Codex";
            foreach(string key in new[]{"primary","secondary"})
            {
                if(!bucket.TryGetProperty(key,out var w)||w.ValueKind!=JsonValueKind.Object) continue;
                if(!w.TryGetProperty("usedPercent",out var u)||u.ValueKind!=JsonValueKind.Number||!u.TryGetDouble(out double used)||!double.IsFinite(used)) continue;
                int mins=w.TryGetProperty("windowDurationMins",out var m)&&m.ValueKind==JsonValueKind.Number&&m.TryGetInt32(out var mv)?mv:0;
                long reset=w.TryGetProperty("resetsAt",out var r)&&r.ValueKind==JsonValueKind.Number&&r.TryGetInt64(out var rv)?rv:0;
                list.Add(new(name,Math.Clamp(used,0,100),mins,reset));
            }
        }
        return list;
    }
    public async Task RefreshQuota()
    {
        if (!await quotaLock.WaitAsync(0)) return;
        QuotaFresh=false;
        QuotaDiagnostic=null;
        try
        {
            string? exe=FindCli();List<Quota>? values=null;
            if(exe!=null)
            {
                try { values=await ReadRpcQuota(exe); }
                catch(Exception e) when(e is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or JsonException)
                { QuotaDiagnostic=e.GetType().Name; }
            }
            if(cancel.IsCancellationRequested)return;
            if(values==null)
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try { values=await CodexConnection.ReadNativeQuota(Home,timeout.Token); }
                catch(MissingCodexLoginException)
                { throw new QuotaConnectionException(CodexConnection.LoginNote); }
            }
            Quotas=values;QuotaUpdated=DateTimeOffset.Now;QuotaFresh=true;QuotaDiagnostic=null;
            QuotaNote=Quotas.Count==0?"账号未提供额度窗口（未知）":"已连接 Codex · 来自账户额度接口";
            try { File.WriteAllText(Path.Combine(Data,"quota-cache.json"),JsonSerializer.Serialize(new QuotaCache(Quotas,QuotaUpdated.Value))); }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){} // Live values work without a writable cache.
        }
        catch(QuotaConnectionException e)
        {
            if(e.Message==CodexConnection.LoginNote||e.Message.Contains("API Key")){Quotas=[];QuotaUpdated=null;}
            QuotaNote=e.Message+(QuotaUpdated!=null?" · 显示缓存":"");
        }
        catch(Exception e) when(e is IOException or HttpRequestException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or JsonException or UnauthorizedAccessException)
        { QuotaDiagnostic=e.GetType().Name+"\n"+e.StackTrace;QuotaNote=CodexConnection.NetworkNote+(QuotaUpdated!=null?" · 显示缓存":""); }
        finally { quotaLock.Release(); if(!cancel.IsCancellationRequested)QuotaChanged?.Invoke(); }
    }
    async Task<List<Quota>> ReadRpcQuota(string exe)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);timeout.CancelAfter(TimeSpan.FromSeconds(12));
        // stdio is the default on old and new releases; --stdio is not universal.
        using var p=new Process { StartInfo=new(exe,"app-server") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Data } };
        Directory.CreateDirectory(Data);p.Start();
        var drain=p.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await p.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"muelsyse_desktop_pet\",\"version\":\"1.3.2\"}}}");
            await p.StandardInput.FlushAsync();
            using var initializedResponse=await Response(p,1,timeout.Token);
            await p.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await p.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
            await p.StandardInput.FlushAsync();
            using var response=await Response(p,2,timeout.Token);
            return ParseQuota(response.RootElement.GetProperty("result"));
        }
        finally
        {
            p.StandardInput.Close();
            try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch(TimeoutException) { if(!p.HasExited)p.Kill(entireProcessTree:true); }
            try { await drain; } catch(OperationCanceledException){}
        }
    }
    private static async Task<JsonDocument> Response(Process p,int id,CancellationToken token)
    {
        while(true)
        {
            var line=await p.StandardOutput.ReadLineAsync(token)??throw new IOException("Codex 接口已关闭");
            var doc=JsonDocument.Parse(line);
            if(doc.RootElement.TryGetProperty("id",out var value)&&value.ValueKind==JsonValueKind.Number&&value.GetInt32()==id)
            { if(doc.RootElement.TryGetProperty("error",out var error)){string message=error.TryGetProperty("message",out var detail)?detail.GetString()??"":"";doc.Dispose();throw new QuotaConnectionException(CodexConnection.RpcFailure(message));} return doc; }
            doc.Dispose();
        }
    }
    public static string? FindCli()=>CodexConnection.CliCandidates(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetEnvironmentVariable("PATH")??"",CodexConnection.InstalledPackages()).FirstOrDefault(File.Exists);
    private record QuotaCache(List<Quota> Values,DateTimeOffset At);
    public void Dispose()=>cancel.Cancel();
}
