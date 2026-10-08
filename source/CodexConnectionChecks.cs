using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Muelsyse;

internal static class CodexConnectionChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        // Isolated fixtures contain only invented credentials, never the user's login.
        string root=Path.Combine(Path.GetTempPath(),"Muelsyse-ConnectionChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string store=Path.Combine(root,"WindowsApps","OpenAI.Codex_1.0"),local=Path.Combine(root,"Local"),roaming=Path.Combine(root,"Roaming");
        string bundled=Path.Combine(store,"app","resources","codex.exe");Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);File.WriteAllText(bundled,"");
        var candidates=CodexConnection.CliCandidates(local,roaming,"",[store]).ToArray();
        check(candidates.FirstOrDefault(File.Exists)==bundled,"new Store install is found without PATH or an extracted CLI cache");
        string npm=Path.Combine(roaming,"npm","node_modules","@openai","codex","vendor","x86_64-pc-windows-msvc","codex","codex.exe");Directory.CreateDirectory(Path.GetDirectoryName(npm)!);File.WriteAllText(npm,"");
        check(CodexConnection.CliCandidates(local,roaming,"",[]).FirstOrDefault(File.Exists)==npm,"npm install is found without a codex.exe PATH entry");
        check(!CodexConnection.CliCandidates(local,roaming,".;relative-folder",[]).Any(p=>p.StartsWith("relative-folder")),"current working directory is not searched for executables");
        check(CodexConnection.WindowsProxy(true,"127.0.0.1:7897",null)?.AbsoluteUri=="http://127.0.0.1:7897/","Explorer startup uses Windows global proxy for HTTPS");
        check(CodexConnection.WindowsProxy(true,"http=localhost:8000;https=localhost:8443",null)?.Port==8443,"protocol-specific Windows HTTPS proxy is selected");
        check(CodexConnection.WindowsProxy(false,"localhost:7897",null)==null&&CodexConnection.WindowsProxy(true,"localhost:7897","*.chatgpt.com;chatgpt.com")==null,"disabled proxies and bypass rules are respected");
        check(CodexConnection.RpcFailure("error sending request")!=CodexConnection.LoginNote&&CodexConnection.RpcFailure("not logged in")==CodexConnection.LoginNote,"network errors are not reported as a missing login");
        using var usage=JsonDocument.Parse("""{"rate_limit":{"primary_window":{"used_percent":28,"limit_window_seconds":604800,"reset_at":123},"secondary_window":null},"additional_rate_limits":[{"limit_name":"Extra","rate_limit":{"primary_window":{"used_percent":12,"limit_window_seconds":18000,"reset_at":456}}}]}""");
        var values=CodexConnection.ParseUsage(usage.RootElement);
        check(values.Count==2&&values[0].Minutes==10080&&100-values[0].Used==72&&values[1].Minutes==300,"native response maps actual independent windows and remaining percentages");
        using var unknown=JsonDocument.Parse("""{"rate_limit":{"primary_window":{"used_percent":null},"secondary_window":{"used_percent":"unknown"}}}""");
        check(CodexConnection.ParseUsage(unknown.RootElement).Count==0,"missing or unknown usage does not become zero used");
        using var link=new CodexLink();check(link.Quotas.Count==0&&link.QuotaUpdated==null&&link.RetryDelay==TimeSpan.FromSeconds(30),"fresh startup never trusts a previous account's quota cache and retries failures promptly");
        string auth=Path.Combine(root,"auth.json");var handler=new FixtureHandler(HttpStatusCode.OK,usage.RootElement.GetRawText());
        bool missing=false;try{await CodexConnection.ReadNativeQuota(root,CancellationToken.None,handler);}catch(MissingCodexLoginException){missing=true;}
        check(missing&&!handler.Called,"first-time unsigned user gets login guidance before any authenticated request");
        const string fake="""{"auth_mode":"chatgpt","tokens":{"access_token":"test-only-token","account_id":"test-only-account"}}""";File.WriteAllText(auth,fake);
        values=await CodexConnection.ReadNativeQuota(root,CancellationToken.None,handler);
        check(values.Count==2&&handler.Called&&handler.CorrectAuth&&File.ReadAllText(auth)==fake,"native fallback authenticates only to the fixed HTTPS endpoint and leaves login storage untouched");
        var unauthorized=new FixtureHandler(HttpStatusCode.Unauthorized,"{}");bool login=false;
        try{await CodexConnection.ReadNativeQuota(root,CancellationToken.None,unauthorized);}catch(QuotaConnectionException e){login=e.Message==CodexConnection.LoginNote;}
        check(login,"expired login is distinguished from a network failure");
        var unavailable=new FixtureHandler(HttpStatusCode.ServiceUnavailable,"{}");bool retry=false;
        try{await CodexConnection.ReadNativeQuota(root,CancellationToken.None,unavailable);}catch(QuotaConnectionException e){retry=e.Message.Contains("自动重连");}
        check(retry,"temporary service failure preserves automatic retry guidance");
        using var native=CodexConnection.NativeHandler();check(!native.AllowAutoRedirect,"native quota transport cannot forward credentials through redirects");
    }
    sealed class FixtureHandler(HttpStatusCode status,string body):HttpMessageHandler
    {
        internal bool Called,CorrectAuth;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Called=true;CorrectAuth=request.RequestUri==CodexConnection.UsageEndpoint&&request.Method==HttpMethod.Get&&request.Headers.Authorization?.Parameter=="test-only-token"&&request.Headers.GetValues("ChatGPT-Account-Id").Single()=="test-only-account";
            return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body)});
        }
    }
}
