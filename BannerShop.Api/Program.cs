using System.Security.Claims;
using System.Text;
using BannerShop.Api.Services;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Api.Services.Shipping;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddControllers();
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

// ─── Middleware pipeline ──────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "BannerShop API v1"));
}

app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ─── Auto-migrate & seed on startup ──────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BannerShopDbContext>();
    if (app.Environment.IsDevelopment())
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
