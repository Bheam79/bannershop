using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.DesignRequests.Cli;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

var root = Directory.CreateTempSubdirectory("bannershop-cli-check-").FullName;
try
{
var services = new ServiceCollection();
// A shared database name across scopes is needed by the real credential manager.
var dbName = Guid.NewGuid().ToString();
services.AddDbContext<BannerShopDbContext>(o => o.UseInMemoryDatabase(dbName));
services.AddMemoryCache();
services.AddScoped<ISystemSettingsService, SystemSettingsService>();
using var provider = services.BuildServiceProvider();
var cliOptions = new ImageCliOptions { CodexExecutable = root + "/codex", GrokExecutable = root + "/grok", TimeoutSeconds = 10 };
var monitor = new Mock<IOptionsMonitor<ImageCliOptions>>(); monitor.SetupGet(m => m.CurrentValue).Returns(cliOptions);
var lifetime = new Mock<IHostApplicationLifetime>();
var runtime = new ImageCliRuntime(provider.GetRequiredService<IServiceScopeFactory>(), monitor.Object, lifetime.Object, NullLogger<ImageCliRuntime>.Instance);
var ai = new ParallelCliImageService(runtime, NullLogger<ParallelCliImageService>.Instance);
var images = new ImageProcessingService();
var storage = new BannerFileStorage(Options.Create(new FileStorageOptions { LocalRoot = root + "/storage", PublicBaseUrl = "/files" }));
using var scope = provider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<BannerShopDbContext>();

void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
async Task SetMode(string codex, string grok)
{
    await File.WriteAllTextAsync(root + "/modes.json", JsonSerializer.Serialize(new { codex, grok }));
    foreach (var p in new[] { "codex", "grok" }) { File.Delete(root + "/" + p + ".start"); File.Delete(root + "/" + p + ".end"); }
}
async Task Cleanup(IReadOnlyList<AiImageResult> outputs) { foreach (var image in outputs) File.Delete(image.AbsolutePath); await Task.CompletedTask; }

// Reproduce the old selector bug independently of the new CLI path.
db.BannerTemplates.Add(new BannerTemplate { Id = 1, NameNb = "Test", NameEn = "Test" });
var request = new DesignRequest { UserId = 1, BannerTemplateId = 1, Mode = DesignRequestMode.Ai,
    Status = DesignRequestStatus.AwaitingApproval, AspectRatio = "18:9", AiPreviewPath = "old.jpg" };
db.DesignRequests.Add(request); await db.SaveChangesAsync();
var target = new BannerGeneration { DesignRequestId = request.Id, Status = BannerGenerationStatus.Completed,
    StoragePath = "new.png", CroppedStoragePath = "new-crop.png", PreviewPath = "new-preview.jpg", Provider = "grok" };
db.BannerGenerations.Add(target); await db.SaveChangesAsync();
var selector = new DesignRequestService(db, null!, null!, storage, images, null!, null!, null!, NullLogger<DesignRequestService>.Instance);
var selected = await selector.ActivateGenerationAsync(request.Id, target.Id, 1);
Check(selected.Success && request.AiPreviewPath == target.PreviewPath && request.FinalCroppedStoragePath == target.CroppedStoragePath,
    "selecting Grok changes BOTH preview and print file");
if (args.Contains("selection-only")) return;

if (args.Contains("native-login"))
{
    cliOptions.CodexExecutable = "codex"; cliOptions.GrokExecutable = "grok";
    foreach (var name in new[] { "codex", "grok" }) runtime.StartLogin(name);
    try
    {
        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(1000);
            if ((await runtime.StatusAsync("codex", default)).UserCode != null
                && (await runtime.StatusAsync("grok", default)).UserCode != null) break;
        }
        foreach (var name in new[] { "codex", "grok" })
        {
            var status = await runtime.StatusAsync(name, default);
            Check(status.Pending && status.AuthorizationUrl != null && status.UserCode != null,
                name + " native CLI emitted parseable device login URL and code");
        }
    }
    finally
    {
        foreach (var name in new[] { "codex", "grok" }) runtime.CancelLogin(name);
        await Task.Delay(1000);
    }
    return;
}

using (var fixture = new Image<Rgba32>(1024, 576, new Rgba32(50, 80, 190)))
{
    for (var x = 200; x < 800; x++) for (var y = 150; y < 450; y++) fixture[x, y] = new Rgba32(220, 140, 25);
    await fixture.SaveAsPngAsync(root + "/fixture.png");
}
using (var solid = new Image<Rgba32>(1024, 576, new Rgba32(10, 10, 10))) await solid.SaveAsPngAsync(root + "/solid.png");
foreach (var name in new[] { "codex", "grok" })
{
    var script = "#!/usr/bin/python3\n" + $$$"""
import os, sys, json, time, pathlib, shutil
root = pathlib.Path('{{{root}}}')
name = pathlib.Path(sys.argv[0]).name
home = pathlib.Path(os.environ['CODEX_HOME'] if name == 'codex' else os.environ['GROK_HOME'])
assert 'OPENAI_API_KEY' not in os.environ and 'XAI_API_KEY' not in os.environ
assert home != pathlib.Path('/workspace/.codex')
if 'login' in sys.argv:
    print('https://auth.openai.com/codex/device' if name == 'codex' else 'https://auth.x.ai/device', flush=True)
    print('ABCD-EFGH', flush=True)
    time.sleep(.7)
    (home / 'auth.json').write_text(json.dumps({'https://grok.com': {'key': 'fixture-token'}} if name == 'grok' else {'tokens': {'access_token': 'fixture-token'}}))
    sys.exit(0)
if name == 'codex':
    prompt = sys.stdin.read()
    assert '--sandbox' in sys.argv and 'read-only' in sys.argv and 'features.shell_tool=false' in sys.argv
else:
    prompt = pathlib.Path(sys.argv[sys.argv.index('--prompt-file') + 1]).read_text()
    assert '--tools' in sys.argv and '--no-subagents' in sys.argv
assert 'original alternative' in prompt
(root / (name + '.start')).write_text(str(time.time()))
mode = json.loads((root / 'modes.json').read_text())[name]
if mode == 'timeout': time.sleep(30)
else: time.sleep(.3)
(home / 'auth.json').write_text(json.dumps({'https://grok.com': {'key': 'rotated-token'}} if name == 'grok' else {'tokens': {'access_token': 'rotated-token'}}))
if mode == 'fail': sys.exit(1)
dest = home / ('generated_images/run' if name == 'codex' else 'sessions/run/images')
dest.mkdir(parents=True, exist_ok=True)
if mode == 'invalid': (dest / '1.png').write_text('not an image')
else: shutil.copy(root / ('solid.png' if mode == 'solid' else 'fixture.png'), dest / '1.png')
(root / (name + '.end')).write_text(str(time.time()))
""";
    await File.WriteAllTextAsync(root + "/" + name, script);
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root + "/" + name, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}

foreach (var name in new[] { "codex", "grok" }) runtime.StartLogin(name);
await Task.Delay(250);
foreach (var name in new[] { "codex", "grok" })
{
    var pending = await runtime.StatusAsync(name, default);
    Check(pending.Pending && pending.AuthorizationUrl is not null && pending.UserCode == "ABCD-EFGH", name + " device code exposed without tokens");
}
await Task.Delay(900);
foreach (var name in new[] { "codex", "grok" }) Check((await runtime.StatusAsync(name, default)).IsConfigured, name + " login stores credentials");
Check((await db.SystemSettings.ToListAsync()).All(x => x.IsSensitive), "OAuth credentials marked sensitive");
await SetMode("valid", "valid");
var outputs = await ai.GenerateCandidatesAsync(new("Test `echo injection` $(touch /tmp/should-not-exist)", "18:9", null), default);
Check(outputs.Count == 2 && outputs.Select(x => x.Provider).Distinct().Count() == 2, "two valid provider results");
var latestStart = new[] { "codex", "grok" }.Max(p => double.Parse(File.ReadAllText(root + "/" + p + ".start")));
var earliestEnd = new[] { "codex", "grok" }.Min(p => double.Parse(File.ReadAllText(root + "/" + p + ".end")));
Check(latestStart < earliestEnd, "both CLI processes overlap");
await Cleanup(outputs);
Check((await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().GetValueAsync(ImageCliRuntime.CredentialKey("codex")))!.Contains("rotated-token"), "refreshed credentials saved to DB");
foreach (var mode in new[] { "fail", "invalid", "solid", "timeout" })
{
    await SetMode("valid", mode);
    outputs = await ai.GenerateCandidatesAsync(new("Test", "16:9", null), default);
    Check(outputs.Count == 1 && outputs[0].Provider == "codex", "single-image fallback when Grok is " + mode);
    await Cleanup(outputs);
}
await SetMode("fail", "valid");
outputs = await ai.GenerateCandidatesAsync(new("Test", "16:9", null), default);
Check(outputs.Count == 1 && outputs[0].Provider == "grok", "single-image fallback when Codex fails"); await Cleanup(outputs);
await SetMode("valid", "valid");
request.Status = DesignRequestStatus.InProgress;
request.LastChargeKind = AiChargeKind.Consumed;
var credits = new Mock<IAiCreditService>();
var pipeline = new AiGenerationPipeline(db, new BannerPromptService(), new NoopPromptRefinementService(), ai,
    new NoopUpscalingService(), images, storage, credits.Object, NullLogger<AiGenerationPipeline>.Instance);
await pipeline.RunAsync(request.Id, default);
var completed = await db.BannerGenerations.Where(g => g.DesignRequestId == request.Id && g.Id != target.Id).ToListAsync();
Check(request.Status == DesignRequestStatus.AwaitingApproval && completed.Count == 2 && completed.Count(g => g.IsActive) == 1,
    "pipeline persists two distinct generations with exactly one active");
Check(completed.All(g => File.Exists(storage.AbsolutePathFor(g.PreviewPath!))) && completed.Select(g => g.StoragePath).Distinct().Count() == 2,
    "both provider previews and full-resolution files persisted");
foreach (var g in completed)
{
    var (w, h) = await images.ReadDimensionsAsync(storage.AbsolutePathFor(g.CroppedStoragePath!), default);
    Check(w == h * 2, g.Provider + " print image cropped to requested 18:9");
}
await SetMode("fail", "fail");
request.Status = DesignRequestStatus.InProgress; request.LastChargeKind = AiChargeKind.Consumed;
await pipeline.RunAsync(request.Id, default);
Check(request.Status == DesignRequestStatus.Failed && request.LastChargeKind == AiChargeKind.None, "both failures mark request failed and clear charge");
credits.Verify(x => x.RefundGenerationChargeAsync(1, It.IsAny<string?>(), AiChargeKind.Consumed, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
Console.WriteLine("PASS: both providers failing refund exactly one charge");

Console.WriteLine("All targeted checks passed; no project test suite or live paid generation run.");

}
finally { Directory.Delete(root, recursive: true); }
