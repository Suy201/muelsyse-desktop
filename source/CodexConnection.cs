using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Win32;

namespace Muelsyse;

internal sealed class QuotaConnectionException(string message):IOException(message){}
internal sealed class MissingCodexLoginException:IOException{}

internal static class CodexConnection
{
    // Same first-party usage endpoint used by Codex. Never follow redirects with credentials.
    internal static readonly Uri UsageEndpoint=new("https://chatgpt.com/backend-api/wham/usage");
    internal const string LoginNote="请打开 Codex 并登录 ChatGPT 账号";
    internal const string NetworkNote="网络暂不可用，请检查系统代理；将自动重连";
    internal static string RpcFailure(string message)
    {
        string text=message.ToLowerInvariant();
        if(text.Contains("not logged")||text.Contains("unauthorized")||text.Contains("401")||text.Contains("authentication required"))return LoginNote;
        if(text.Contains("api key")||text.Contains("apikey"))return "API Key 登录不提供 ChatGPT 剩余额度";
        if(text.Contains("error sending")||text.Contains("network")||text.Contains("connect")||text.Contains("timed out")||text.Contains("routing"))return NetworkNote;
        return "Codex 额度接口暂不可用，将自动重连";
    }
    internal static IEnumerable<string> CliCandidates(string local,string roaming,string path,IEnumerable<string> packages)
    {
        // Registered Store installs are available even before the app has extracted its CLI.
        foreach(string package in packages)yield return Path.Combine(package,"app","resources","codex.exe");
        string cache=Path.Combine(local,"OpenAI","Codex","bin");
        foreach(string directory in ChildDirectories(cache).OrderByDescending(Directory.GetLastWriteTimeUtc))yield return Path.Combine(directory,"codex.exe");
        yield return Path.Combine(cache,"codex.exe");
        foreach(string raw in path.Split(Path.PathSeparator))
        {
            string directory=raw.Trim().Trim('"');if(!Path.IsPathFullyQualified(directory))continue;
            yield return Path.Combine(directory,"codex.exe");
            foreach(string file in NpmCli(directory))yield return file;
        }
        foreach(string file in NpmCli(Path.Combine(roaming,"npm")))yield return file;
    }
    static IEnumerable<string> NpmCli(string root)
    {
        string packages=Path.Combine(root,"node_modules","@openai");
        foreach(string arch in new[]{"x86_64-pc-windows-msvc","aarch64-pc-windows-msvc"})
        {
            yield return Path.Combine(packages,"codex","vendor",arch,"codex","codex.exe");
            yield return Path.Combine(packages,"codex-win32-"+(arch.StartsWith("x86")?"x64":"arm64"),"vendor",arch,"codex","codex.exe");
        }
    }
    static string[] ChildDirectories(string path)
    {
        try{return Directory.Exists(path)?Directory.GetDirectories(path):[];}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){return [];}
    }
    internal static string[] InstalledPackages()
    {
        List<string> paths=[];
        try
        {
            using var packages=Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if(packages!=null)foreach(string name in packages.GetSubKeyNames().Where(n=>n.StartsWith("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)).OrderDescending())
            {
                using var package=packages.OpenSubKey(name);
                if(package?.GetValue("PackageRootFolder") is string root)paths.Add(root);
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){}
        return paths.ToArray();
    }
    internal static Uri? WindowsProxy(bool enabled,string? server,string? bypass)
    {
        if(!enabled||string.IsNullOrWhiteSpace(server))return null;
        foreach(string item in (bypass??"").Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
        {
            if(item=="<local>")continue;
            string pattern="^"+System.Text.RegularExpressions.Regex.Escape(item).Replace("\\*",".*")+"$";
            if(System.Text.RegularExpressions.Regex.IsMatch(UsageEndpoint.Host,pattern,System.Text.RegularExpressions.RegexOptions.IgnoreCase))return null;
        }
        if(server.Contains('='))server=server.Split(';').Select(s=>s.Trim().Split('=',2)).FirstOrDefault(s=>s.Length==2&&s[0].Equals("https",StringComparison.OrdinalIgnoreCase))?.Last();
        if(string.IsNullOrWhiteSpace(server))return null;
        if(!server.Contains("://"))server="http://"+server;
        return Uri.TryCreate(server,UriKind.Absolute,out var proxy)&&proxy.Scheme is "http" or "https" or "socks5"?proxy:null;
    }
    internal static HttpClientHandler NativeHandler()
    {
        var handler=new HttpClientHandler{AllowAutoRedirect=false};
        // Explorer does not supply the shell's proxy environment. Read current Windows
        // settings on each attempt so a network/proxy change recovers without restart.
        if(!new[]{"HTTPS_PROXY","HTTP_PROXY","ALL_PROXY","https_proxy","http_proxy","all_proxy"}.Any(k=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(k))))
        {
            using var settings=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            bool enabled=settings?.GetValue("ProxyEnable") is int value&&value!=0;
            string? server=settings?.GetValue("ProxyServer") as string;
            if(enabled&&!string.IsNullOrWhiteSpace(server))
            {
                var proxy=WindowsProxy(enabled,server,settings?.GetValue("ProxyOverride") as string);
                handler.UseProxy=proxy!=null;if(proxy!=null)handler.Proxy=new WebProxy(proxy);
            }
        }
        return handler;
    }
    internal static async Task<List<Quota>> ReadNativeQuota(string home,CancellationToken token,HttpMessageHandler? testHandler=null)
    {
        string path=Path.Combine(home,"auth.json");
        if(!File.Exists(path))throw new MissingCodexLoginException();
        using var auth=JsonDocument.Parse(await File.ReadAllTextAsync(path,token));
        if(auth.RootElement.TryGetProperty("auth_mode",out var mode)&&mode.ValueKind==JsonValueKind.String&&mode.GetString()=="apikey")throw new QuotaConnectionException("API Key 登录不提供 ChatGPT 剩余额度");
        if(!auth.RootElement.TryGetProperty("tokens",out var tokens)||tokens.ValueKind!=JsonValueKind.Object||
           !tokens.TryGetProperty("access_token",out var access)||access.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(access.GetString()))throw new MissingCodexLoginException();
        using var client=new HttpClient(testHandler??NativeHandler());
        using var request=new HttpRequestMessage(HttpMethod.Get,UsageEndpoint);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access.GetString());
        if(tokens.TryGetProperty("account_id",out var account)&&account.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(account.GetString()))request.Headers.Add("ChatGPT-Account-Id",account.GetString());
        using var response=await client.SendAsync(request,token);
        if(response.StatusCode==HttpStatusCode.Unauthorized)throw new QuotaConnectionException(LoginNote);
        if(response.StatusCode==HttpStatusCode.Forbidden)throw new QuotaConnectionException("额度服务拒绝访问，请在 Codex 中确认账号");
        if(!response.IsSuccessStatusCode)throw new QuotaConnectionException("额度服务暂不可用，将自动重连");
        using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return ParseUsage(document.RootElement);
    }
    internal static List<Quota> ParseUsage(JsonElement result)
    {
        List<Quota> quotas=[];
        void Add(string name,JsonElement bucket)
        {
            if(bucket.ValueKind!=JsonValueKind.Object)return;
            foreach(string field in new[]{"primary_window","secondary_window"})
            {
                if(!bucket.TryGetProperty(field,out var w)||w.ValueKind!=JsonValueKind.Object||!w.TryGetProperty("used_percent",out var u)||u.ValueKind!=JsonValueKind.Number||!u.TryGetDouble(out double used)||!double.IsFinite(used))continue;
                int minutes=w.TryGetProperty("limit_window_seconds",out var m)&&m.ValueKind==JsonValueKind.Number&&m.TryGetInt32(out int seconds)?seconds/60:0;
                long reset=w.TryGetProperty("reset_at",out var r)&&r.ValueKind==JsonValueKind.Number&&r.TryGetInt64(out long at)?at:0;
                quotas.Add(new(name,Math.Clamp(used,0,100),minutes,reset));
            }
        }
        if(result.TryGetProperty("rate_limit",out var main))Add("Codex",main);
        if(result.TryGetProperty("additional_rate_limits",out var extra)&&extra.ValueKind==JsonValueKind.Array)
            foreach(var item in extra.EnumerateArray())
                if(item.ValueKind==JsonValueKind.Object&&item.TryGetProperty("rate_limit",out var limits))
                    Add(item.TryGetProperty("limit_name",out var name)&&name.ValueKind==JsonValueKind.String?name.GetString()!:"Codex",limits);
        return quotas;
    }
}
