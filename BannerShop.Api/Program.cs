using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using BannerShop.Api.Services;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Core;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.DesignRequests.OpenAi;
using BannerShop.Api.Services.DesignRequests.Replicate;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Api.Services.Shipping;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ─── Local developer overrides ────────────────────────────────────────────────
// appsettings.Local.json is git-ignored; copy appsettings.Local.json.example and
// fill in your real API keys without touching the committed appsettings files.
//
// Always register as optional + reloadOnChange so that:
//  • the service can start when the file is absent (optional: true suppresses the
//    FileNotFoundException; reloadOnChange still sets up a filesystem watcher)
//  • if the operator creates or edits the file AFTER startup (e.g. on a production
//    server), the configuration reloads automatically — no restart required
//  • on production, `make up` copies it from the source tree if present, or leaves
//    any existing deployed copy untouched if there is no source-side file
try
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"[WARN] appsettings.Local.json could not be loaded ({ex.Message}). " +
        "Fix or delete the file; continuing without it.");
}

// ─── Database ─────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Use a fixed MariaDB 11 version to avoid AutoDetect connection at startup
var mariaDbVersion = new MariaDbServerVersion(new Version(11, 0, 0));
builder.Services.AddDbContext<BannerShopDbContext>(options =>
    options.UseMySql(connectionString, mariaDbVersion,
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(3)));

// ─── Auth Services ────────────────────────────────────────────────────────────
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// ─── Catalog Services ─────────────────────────────────────────────────────────
builder.Services.AddScoped<IPricingService, PricingService>();

// ─── Shipping (Bring/Posten) ─────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<BringOptions>(builder.Configuration.GetSection(BringOptions.SectionName));
builder.Services.AddScoped<ParcelCalculator>();

var bringSection = builder.Configuration.GetSection(BringOptions.SectionName);
var bringUid = bringSection["ApiUid"];
var bringKey = bringSection["ApiKey"];
var bringConfigured =
    !string.IsNullOrWhiteSpace(bringUid) &&
    !string.IsNullOrWhiteSpace(bringKey) &&
    !bringUid.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase) &&
    !bringKey.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase);

if (bringConfigured)
{
    builder.Services.AddHttpClient<IShippingService, BringShippingService>((sp, http) =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BringOptions>>().Value;
        http.BaseAddress = new Uri(opts.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
    });
}
else
{
    builder.Services.AddScoped<IShippingService, MockShippingService>();
}

// ─── Stripe + Orders ──────────────────────────────────────────────────────────
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));

var stripeSection = builder.Configuration.GetSection(StripeOptions.SectionName);
var stripeKey = stripeSection["SecretKey"];
var stripeConfigured =
    !string.IsNullOrWhiteSpace(stripeKey) &&
    !stripeKey.StartsWith("sk_test_REPLACE_", StringComparison.OrdinalIgnoreCase) &&
    !stripeKey.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

if (stripeConfigured)
    builder.Services.AddScoped<IStripePaymentService, StripePaymentService>();
else
    builder.Services.AddScoped<IStripePaymentService, MockStripePaymentService>();

builder.Services.AddScoped<IOrderService, OrderService>();

// ─── Banner Builder (file uploads, image processing) ─────────────────────────
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));
// IFileStore: shared abstraction for all banner-builder file I/O (BANNERSH-25).
builder.Services.AddSingleton<IFileStore, LocalDiskFileStore>();
// UploadValidator: centralised size/MIME/magic-byte checks (BANNERSH-25).
builder.Services.AddSingleton<UploadValidator>();
// BannerFileStorage: legacy wrapper kept until callers fully migrate to IFileStore.
#pragma warning disable CS0618 // legacy aliases used intentionally here
builder.Services.AddSingleton<BannerFileStorage>();
#pragma warning restore CS0618
builder.Services.AddSingleton<IImageProcessingService, ImageProcessingService>();

// ─── AI Credit Pool (BANNERSH-65) ────────────────────────────────────────────
builder.Services.AddScoped<IAiCreditService, AiCreditService>();
// BotProtectionFilter is registered as a service so it can be injected into action filters.
builder.Services.AddScoped<BotProtectionFilter>();

// ─── System Settings (BANNERSH-98: admin-editable runtime config) ────────────
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();

// ─── AI Design Requests (95 kr) ──────────────────────────────────────────────
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));

// BANNERSH-98: Always register OpenAiImageService — it resolves the API key at
// call time (DB setting → appsettings fallback) so the admin can enter / update
// the key via the settings panel without restarting the service.  When no key is
// configured it returns a solid-colour placeholder (same as MockAiImageService).
builder.Services.AddHttpClient<IAiImageService, OpenAiImageService>();
// Prompt refinement: always register the OpenAI-backed refiner. It already falls
// back to the base prompt on any HTTP error (including 401 from a missing key),
// so it is safe to call even when the key is not configured.
builder.Services.AddHttpClient<IPromptRefinementService, OpenAiPromptRefinementService>();

// IUpscalingService stays Noop for the customer-facing AiGenerationPipeline —
// per BANNERSH-57 the Real-ESRGAN 4x pass is an order-backend / admin step,
// not part of the preview flow customers wait on.
builder.Services.AddSingleton<IUpscalingService, NoopUpscalingService>();

// ─── Replicate (Real-ESRGAN 4x upscaler for order/admin backend) ─────────────
builder.Services.Configure<ReplicateOptions>(builder.Configuration.GetSection(ReplicateOptions.SectionName));

var replicateSection = builder.Configuration.GetSection(ReplicateOptions.SectionName);
var replicateToken = replicateSection["ApiToken"];
var replicateConfigured =
    !string.IsNullOrWhiteSpace(replicateToken) &&
    !replicateToken.StartsWith("r8_REPLACE", StringComparison.OrdinalIgnoreCase) &&
    !replicateToken.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

if (replicateConfigured)
    builder.Services.AddHttpClient<RealEsrganUpscalingService>();
// When the token isn't set, admin endpoints that depend on RealEsrganUpscalingService
// will return 503 (see AdminDesignRequestsController.Upscale).

builder.Services.AddSingleton<IPhotoCompositor, PhotoCompositorNotImplemented>();
builder.Services.AddSingleton<IBannerPromptService, BannerPromptService>();
builder.Services.AddSingleton<IDesignRequestJobQueue, DesignRequestJobQueue>();
builder.Services.AddScoped<DesignRequestService>();          // concrete type needed by AdminDesignRequestService
builder.Services.AddScoped<IDesignRequestService>(sp => sp.GetRequiredService<DesignRequestService>());
builder.Services.AddScoped<IAdminDesignRequestService, AdminDesignRequestService>();
builder.Services.AddScoped<AiGenerationPipeline>();
builder.Services.AddHostedService<DesignRequestJobProcessor>();

// ─── Email ────────────────────────────────────────────────────────────────────
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));

// SmtpEmailService when Email:SmtpHost is set; NullEmailService otherwise.
// Per BANNERSH-58 the NullEmailService logs at Warning outside Development so
// a dropped production email is loud rather than silent.
var emailOpts = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
    ?? new EmailOptions();
if (!string.IsNullOrWhiteSpace(emailOpts.SmtpHost))
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
else
    builder.Services.AddScoped<IEmailService, NullEmailService>();

// Allow multipart uploads up to the configured cap (+25% headroom for form overhead).
var fileStorageOpts = builder.Configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>()
    ?? new FileStorageOptions();
// Use MaxUploadBytes (canonical) directly — gives exact byte control.
var multipartMaxBytes = fileStorageOpts.MaxUploadBytes * 5 / 4; // +25% headroom for multipart framing
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = multipartMaxBytes;
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = multipartMaxBytes;
});

// ─── Authentication (JWT) ─────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT secret is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "bannershop.no";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "bannershop.no";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1),
            // Map standard claim types so [Authorize(Roles="...")] works
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
    });

builder.Services.AddAuthorization();

// ─── CORS ─────────────────────────────────────────────────────────────────────
var frontendUrl = builder.Configuration["Frontend:Url"] ?? "http://localhost:5173";
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ─── Controllers & Swagger ────────────────────────────────────────────────────
var controllersBuilder = builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// BANNERSH-114: TestOnlyController exposes pre-seed endpoints used by Playwright
// E2E specs (e.g. POST /api/test/seed-ip-ai-usage). Outside Development we strip
// it from MVC's controller discovery so the routes literally do not exist —
// requests get a vanilla 404 instead of any test-only behavior.
if (!builder.Environment.IsDevelopment())
{
    controllersBuilder.ConfigureApplicationPartManager(apm =>
        apm.FeatureProviders.Add(new BannerShop.Api.Controllers.TestOnlyControllerExcludingFeatureProvider()));
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BannerShop API",
        Version = "v1",
        Description = "API for bannershop.no – custom banner printing"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ─── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── BANNERSH-98 / BANNERSH-127: loud OpenAI-key state log at startup ────────
// Operators were unsure whether appsettings.Local.json was being picked up at
// all. We dump the resolved working directory, which appsettings files are on
// disk next to the dll, and the resolved OpenAi:ApiKey state (masked) so the
// answer is in journalctl from the first line of every boot.
//
// BANNERSH-127: also walk EACH configuration provider in the chain and print
// what (if anything) each contributes for OpenAi:ApiKey / OpenAi:ImageQuality
// so that "I changed the file but it didn't take effect" is immediately
// diagnosable from the log — you can SEE which provider "won". Likewise probe
// the DB system_settings row (which OpenAiImageService prefers over appsettings)
// so a stale admin-panel value overriding the file is obvious.
{
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.OpenAi");
    var cwd = Directory.GetCurrentDirectory();
    var contentRoot = app.Environment.ContentRootPath;
    var envName = app.Environment.EnvironmentName;

    string[] candidateFiles =
    {
        "appsettings.json",
        $"appsettings.{envName}.json",
        "appsettings.Local.json",
    };
    var fileStates = candidateFiles
        .Select(name =>
        {
            // AddJsonFile resolves paths via the configuration's file provider,
            // which WebApplicationBuilder roots at ContentRootPath. Use the same
            // base here so the diagnostic matches where the framework looked.
            var abs = Path.IsPathRooted(name) ? name : Path.Combine(contentRoot, name);
            return File.Exists(abs)
                ? $"{name}=present ({abs})"
                : $"{name}=absent ({abs})";
        })
        .ToArray();

    var openAiCfg = app.Configuration.GetSection("OpenAi");
    var rawKey = openAiCfg["ApiKey"];
    string keyState = DescribeKeyState(rawKey);

    startupLog.LogInformation(
        "Boot config: Environment={Env} ContentRoot={ContentRoot} CWD={Cwd}",
        envName, contentRoot, cwd);
    startupLog.LogInformation(
        "Boot config: appsettings files: {Files}", string.Join(" | ", fileStates));
    startupLog.LogInformation(
        "Boot config: OpenAi:ApiKey={KeyState} ImageModel={Model} ImageQuality={Quality} BaseUrl={BaseUrl}",
        keyState,
        openAiCfg["ImageModel"] ?? "(default)",
        openAiCfg["ImageQuality"] ?? "(default)",
        openAiCfg["BaseUrl"] ?? "(default)");

    // BANNERSH-127: per-provider trace. Walk the configuration chain and print
    // what (if anything) each provider contributes for OpenAi:ApiKey /
    // OpenAi:ImageQuality. Providers are listed in load order — LATER providers
    // win, so the last one with a value is what the app actually sees. If
    // appsettings.Local.json appears in this list and contains the key but it
    // is still being overridden, something is very wrong; if it does NOT appear
    // here at all, the file isn't being loaded.
    if (app.Configuration is IConfigurationRoot cfgRoot)
    {
        var providerDescriptions = new List<string>();
        var index = 0;
        foreach (var provider in cfgRoot.Providers)
        {
            index++;
            var providerName = provider.GetType().Name;
            // JsonConfigurationProvider exposes its Source.Path — surface it so
            // we know which JSON file each Json* entry corresponds to.
            var providerPath = provider switch
            {
                Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider j => j.Source.Path,
                Microsoft.Extensions.Configuration.FileConfigurationProvider f => f.Source.Path,
                _ => null,
            };

            var providerKey = provider.TryGet("OpenAi:ApiKey", out var keyVal) ? keyVal : null;
            var providerQuality = provider.TryGet("OpenAi:ImageQuality", out var qVal) ? qVal : null;

            providerDescriptions.Add(
                $"#{index} {providerName}{(providerPath is null ? "" : $"({providerPath})")} " +
                $"ApiKey={(providerKey is null ? "<not contributing>" : DescribeKeyState(providerKey))} " +
                $"ImageQuality={(providerQuality is null ? "<not contributing>" : $"\"{providerQuality}\"")}");
        }
        startupLog.LogInformation(
            "Boot config: configuration providers (load order — last value wins):\n  {Providers}",
            string.Join("\n  ", providerDescriptions));
    }
    else
    {
        startupLog.LogWarning(
            "Boot config: app.Configuration is not IConfigurationRoot ({Type}); cannot enumerate providers.",
            app.Configuration.GetType().FullName);
    }

    // BANNERSH-127: probe the DB system_settings row for openai_api_key.
    // OpenAiImageService prefers this over appsettings — a stale value left from
    // an admin-panel edit will silently override any change to
    // appsettings.Local.json, which is exactly the "still not working" symptom
    // this task reports.
    try
    {
        using var scope = app.Services.CreateScope();
        var settings = scope.ServiceProvider
            .GetRequiredService<BannerShop.Api.Services.SystemSettings.ISystemSettingsService>();
        var dbKey = await settings.GetValueAsync("openai_api_key");
        var dbModel = await settings.GetValueAsync("openai_image_model");
        startupLog.LogInformation(
            "Boot config: DB system_settings 'openai_api_key'={DbKeyState} 'openai_image_model'={DbModelState} " +
            "(DB takes precedence over appsettings in OpenAiImageService — clear via admin panel if stale)",
            DescribeKeyState(dbKey),
            string.IsNullOrWhiteSpace(dbModel) ? "<unset>" : $"\"{dbModel}\"");
    }
    catch (Exception ex)
    {
        startupLog.LogWarning(ex,
            "Boot config: could not read DB system_settings (DB may be unreachable at startup).");
    }

    // Verify the IAiImageService implementation that DI resolves — the type
    // name confirms whether real OpenAI or a fallback is wired in. Wrapped so
    // a DI-resolution issue can't take down the boot.
    try
    {
        using var scope = app.Services.CreateScope();
        var aiSvc = scope.ServiceProvider
            .GetRequiredService<BannerShop.Api.Services.DesignRequests.IAiImageService>();
        startupLog.LogInformation(
            "Boot config: IAiImageService implementation = {Type}", aiSvc.GetType().FullName);
    }
    catch (Exception ex)
    {
        startupLog.LogError(ex,
            "Boot config: failed to resolve IAiImageService — DI is broken.");
    }

    static string Mask(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "(empty)";
        if (key.Length <= 10) return new string('*', key.Length);
        return key[..6] + "…" + key[^4..];
    }

    static string DescribeKeyState(string? key) => key switch
    {
        null => "missing",
        "" => "empty",
        var k when string.IsNullOrWhiteSpace(k) => "whitespace",
        var k when k.StartsWith("sk-REPLACE", StringComparison.OrdinalIgnoreCase) => $"placeholder({Mask(k)})",
        var k when k.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase) => $"placeholder({Mask(k)})",
        var k => $"set({Mask(k)}, {k.Length} chars)",
    };
}

// ─── Middleware pipeline ──────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "BannerShop API v1"));
}

app.UseCors("FrontendPolicy");

// ─── Static file serving for the SPA (wwwroot) ───────────────────────────────
// In production, the built Vite frontend is copied into BannerShop.Api/wwwroot
// at publish time (see the root Makefile). In Development, wwwroot is typically
// empty and the SPA is served by `vite dev` on :5173 with /api proxied here.
app.UseDefaultFiles();
app.UseStaticFiles();

// ─── Static file serving for uploaded files ──────────────────────────────────
// Files are stored under FileStorage:LocalRoot and exposed at FileStorage:PublicBaseUrl ("/files").
//
// Access model:
//   • Banner-builder uploads:  served directly via StaticFiles — safe because filenames are
//     unguessable GUIDs.  No authorization header required.
//   • Design-request previews: SHOULD be proxied through an authorized API controller so that
//     only the owning user (or admin) can fetch them.  The static route still covers them as a
//     fallback while the controller layer is not yet in place.
//
// BANNERSH-25: IFileStore now owns path-building; GetPublicUrl() returns "/files/…" paths.
{
    var storageRoot = fileStorageOpts.LocalRoot;
    if (!string.IsNullOrWhiteSpace(storageRoot))
    {
        Directory.CreateDirectory(storageRoot);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(storageRoot),
            RequestPath = fileStorageOpts.PublicBaseUrl.TrimEnd('/'),
            ServeUnknownFileTypes = false
        });
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// SPA fallback: serve index.html for any non-API, non-file route so client-side
// routing works on deep-links / refresh. Only kicks in when wwwroot/index.html
// actually exists (i.e. the prod build copied the Vite output in).
{
    var indexPath = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");
    if (File.Exists(indexPath))
    {
        app.MapFallbackToFile("index.html");
    }
}

// ─── Auto-migrate & seed on startup ──────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BannerShopDbContext>();
    // Apply pending migrations on startup in Development and Production —
    // production installs (see Makefile) rely on this so `make up` "just works"
    // without a separate `dotnet ef database update` step. Skipped in other
    // environments (e.g. "Testing") because the integration-test WebApplicationFactory
    // swaps in the InMemory provider, which does not support migrations.
    if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
    {
        db.Database.Migrate();
    }

    // Seed admin user (runs in all environments)
    await SeedAdminAsync(db, app.Configuration);
}

app.Run();

// ─── Admin seed helper ────────────────────────────────────────────────────────
static async Task SeedAdminAsync(BannerShopDbContext db, IConfiguration config)
{
    var adminEmail = config["Admin:SeedEmail"] ?? "admin@bannershop.no";
    var adminPassword = config["Admin:SeedPassword"];

    if (string.IsNullOrWhiteSpace(adminPassword))
        return; // No password configured — skip seed

    if (await db.Users.AnyAsync(u => u.Email == adminEmail))
        return; // Already exists

    db.Users.Add(new User
    {
        Email = adminEmail,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
        Name = "Administrator",
        Role = UserRole.Admin,
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
}

// Make Program accessible for integration tests
public partial class Program { }
