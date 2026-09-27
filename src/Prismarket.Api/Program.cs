using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prismarket.Api.Infrastructure;
using Prismarket.Application;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Automation;
using Prismarket.Infrastructure;
using Prismarket.Infrastructure.Persistence;
using Prismarket.Infrastructure.Security;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Services ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config, enableHangfireServer: !builder.Environment.IsEnvironment("Testing"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IRealtimePublisher, SignalRRealtimePublisher>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks().AddNpgSql(config.GetConnectionString("Postgres")!);

var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Jwt section is missing.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = JwtTokenService.CreateKey(jwt.Secret),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = "unique_name",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        o.Events = new JwtBearerEvents
        {
            // SignalR and the Hangfire dashboard cannot send the Authorization header.
            OnMessageReceived = ctx =>
            {
                var path = ctx.HttpContext.Request.Path;
                var queryToken = ctx.Request.Query["access_token"].ToString();
                if (path.StartsWithSegments(StoreHub.Path) && !string.IsNullOrEmpty(queryToken))
                    ctx.Token = queryToken;
                else if (path.StartsWithSegments("/hangfire"))
                {
                    if (!string.IsNullOrEmpty(queryToken))
                    {
                        ctx.Token = queryToken;
                        ctx.Response.Cookies.Append("pm_hangfire", queryToken,
                            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/hangfire" });
                    }
                    else ctx.Token = ctx.Request.Cookies["pm_hangfire"];
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// ---------- Database & automation bootstrap ----------
if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Migrations are the normal path; EnsureCreated is a fallback for a fresh clone without migrations.
    if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();
    if (config.GetValue("App:SeedDemoData", true))
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<IAutomationAdminService>().SyncCatalogAsync(CancellationToken.None);
}

// ---------- Pipeline ----------
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("Prismarket API"));
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<StoreHub>(StoreHub.Path);
app.MapHealthChecks("/health");
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Prismarket · фоновые задачи",
    AsyncAuthorization = [new HangfireAdminFilter()],
    AppPath = config["App:FrontendUrl"] + "/admin/automation"
});

// SPA fallback when the React build is served from wwwroot (single-container deployment).
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
