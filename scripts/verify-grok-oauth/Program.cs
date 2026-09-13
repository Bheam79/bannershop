// Targeted OAuth smoke check; runs a local CLI fixture, no provider account or project test suite.
using BannerShop.Api.Controllers.Admin;
using BannerShop.Api.Services.DesignRequests.Cli;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;

var root = Directory.CreateTempSubdirectory("bannershop-grok-oauth-check-").FullName;
var services = new ServiceCollection();
var database = Guid.NewGuid().ToString();
services.AddDbContext<BannerShopDbContext>(o => o.UseInMemoryDatabase(database));
services.AddMemoryCache();
services.AddScoped<ISystemSettingsService, SystemSettingsService>();
using var provider = services.BuildServiceProvider();
var options = new Mock<IOptionsMonitor<ImageCliOptions>>();
options.SetupGet(o => o.CurrentValue).Returns(new ImageCliOptions { GrokExecutable = root + "/grok" });
var lifetime = new Mock<IHostApplicationLifetime>();
var runtime = new ImageCliRuntime(provider.GetRequiredService<IServiceScopeFactory>(), options.Object,
    lifetime.Object, NullLogger<ImageCliRuntime>.Instance);
var controller = new AdminImageCliController(runtime);
var port = int.Parse(Environment.GetEnvironmentVariable("GROK_OAUTH_CHECK_PORT") ?? "10013");
void Check(bool result, string label)
{
    if (!result) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}
async Task<ImageCliLoginStatus> WaitFor(Func<ImageCliLoginStatus, bool> condition)
{
    for (var i = 0; i < 200; i++)
    {
        var status = await runtime.StatusAsync("grok", default);
        if (condition(status)) return status;
        await Task.Delay(50);
    }
    throw new Exception("Timed out waiting for Grok login status");
}
async Task<ImageCliLoginStatus> Start()
{
    Check(await controller.Start("grok", default, "oauth") is OkObjectResult, "OAuth start endpoint accepts Grok");
    return await WaitFor(s => s.Pending && s.AuthorizationUrl != null);
}
async Task Rejected(string? code, string label)
{
    Check(await controller.Complete("grok", new(code), default) is BadRequestObjectResult, label);
}
async Task<string?> Credential()
{
    using var scope = provider.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>()
        .GetValueAsync(ImageCliRuntime.CredentialKey("grok"));
}
try
{
    await File.WriteAllTextAsync(root + "/grok", "#!/usr/bin/python3\n" + $$$"""
import os,sys,json,pathlib,http.server,urllib.parse,uuid,time
root=pathlib.Path('{{{root}}}')
home=pathlib.Path(os.environ['GROK_HOME'])
assert 'XAI_API_KEY' not in os.environ
assert sys.argv[1]=='login'
assert len(sys.argv)==3
if sys.argv[2]=='--device-auth':
 print('https://auth.x.ai/device',flush=True)
 print('ABCD-EFGH',flush=True)
 time.sleep(60)
 sys.exit(0)
assert sys.argv[2]=='--oauth'
state=str(uuid.uuid4())
class Handler(http.server.BaseHTTPRequestHandler):
 def log_message(self,*args): pass
 def do_GET(self):
  query=urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
  assert query['state']==[state]
  (root/'received').write_text('received')
  self.send_response(200); self.end_headers(); self.wfile.write(b'ok')
  if query['code']==['reject']: return
  (home/'auth.json').write_text(json.dumps({'https://auth.x.ai::fixture':{
   'key':'fixture-access','refresh_token':'fixture-refresh','auth_mode':'oidc',
   'oidc_issuer':'https://auth.x.ai','oidc_client_id':'fixture','expires_at':'2099-01-01T00:00:00Z'}}))
class Server(http.server.HTTPServer): allow_reuse_address=True
with Server(('127.0.0.1',{{{port}}}),Handler) as server:
 print('https://auth.x.ai/oauth2/authorize?'+urllib.parse.urlencode({
  'redirect_uri':'http://127.0.0.1:{{{port}}}/callback','state':state,'code_challenge':'fixture','code_challenge_method':'S256'}),flush=True)
 server.handle_request()
if not (home/'auth.json').exists(): sys.exit(1)
""");
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(root + "/grok", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    Check(await controller.Start("codex", default, "oauth") is BadRequestObjectResult, "reject unsupported provider/method");
    Check(await controller.Start("grok", default, "invalid") is BadRequestObjectResult, "reject unknown login method");
    await Rejected("code", "reject completion without active flow");
    var pending = await Start();
    Check(pending.LoginMethod == "oauth" && pending.UserCode == null, "OAuth flow exposed distinctly from device login");
    var query = QueryHelpers.ParseQuery(new Uri(pending.AuthorizationUrl!).Query);
    var callback = QueryHelpers.AddQueryString(query["redirect_uri"].ToString(),
        new Dictionary<string, string?> { ["state"] = query["state"], ["code"] = "fixture-code" });
    await Rejected(null, "reject missing code");
    await Rejected(callback.Replace(query["state"].ToString(), "wrong-state"), "reject wrong state before callback delivery");
    await Rejected(callback.Replace("127.0.0.1", "example.com"), "reject foreign callback host");
    await Rejected(callback.Replace($":{port}", $":{port + 1}"), "reject foreign callback port");
    await Rejected(callback.Replace("/callback", "/other"), "reject foreign callback path");
    await Rejected(callback + "&code=second", "reject duplicate code parameter");
    Check(!File.Exists(root + "/received"), "invalid input made no callback request");
    Check(await controller.Complete("grok", new(callback), default) is OkObjectResult, "complete using full callback URL");
    var complete = await WaitFor(s => !s.Pending);
    Check(complete.IsConfigured && complete.Error == null && complete.AuthorizationUrl == null, "successful login configured and transient URL cleared");
    var credential = await Credential();
    Check(credential!.Contains("fixture-refresh"), "complete refreshable CLI credential saved");
    using (var scope = provider.CreateScope())
        Check((await scope.ServiceProvider.GetRequiredService<BannerShopDbContext>().SystemSettings.SingleAsync()).IsSensitive,
            "credential stored in sensitive setting");
    Check(!JsonSerializer.Serialize(complete).Contains("fixture-access"), "status never exposes access token");
    await Rejected(callback, "reject replay of completed flow");
    await Start();
    Check(await controller.Complete("grok", new("bare-code"), default) is OkObjectResult, "complete using displayed bare code");
    Check((await WaitFor(s => !s.Pending)).Error == null, "bare code delivered with current OAuth state");
    await Start();
    controller.Cancel("grok");
    Check((await WaitFor(s => !s.Pending)).Error != null, "cancel terminates pending login");
    await Rejected(callback, "reject completion after cancellation");
    await Start();
    Check(await controller.Complete("grok", new("reject"), default) is OkObjectResult, "callback receipt waits for CLI token validation");
    Check((await WaitFor(s => !s.Pending)).Error != null, "failed token exchange reports error");
    Check(await Credential() == credential, "failed reconnect preserves previous credentials");
    Check(await controller.Start("grok", default) is OkObjectResult, "device-code fallback remains available");
    var device = await WaitFor(s => s.UserCode != null);
    Check(device.LoginMethod == "device" && device.UserCode == "ABCD-EFGH", "device login URL and code remain visible");
    await Rejected("code", "browser completion rejects device flow");
    controller.Cancel("grok");
    await WaitFor(s => !s.Pending);
    Console.WriteLine("Grok OAuth smoke checks passed. No live token exchange or project test suite run.");
}
finally
{
    runtime.CancelLogin("grok");
    await WaitFor(s => !s.Pending);
    Directory.Delete(root, true);
}
