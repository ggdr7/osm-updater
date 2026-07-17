using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OsmUpdateUtility.Data;
using OsmUpdateUtility.HangfireJobs;
using OsmUpdateUtility.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration);
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<IGeofabrikService, GeofabrikService>(client =>
{
    client.Timeout = TimeSpan.FromHours(2);
});

builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IOsmUpdateService, OsmUpdateService>();
builder.Services.AddScoped<INotificationService, LogNotificationService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddSingleton<UpdateStateService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = true;
    });
builder.Services.AddAntiforgery();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(opt =>
    {
        opt.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"));
    }, new PostgreSqlStorageOptions
    {
        SchemaName = "public",
        QueuePollInterval = TimeSpan.FromSeconds(15)
    }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
});

builder.Services.AddRazorPages();

var app = builder.Build();

try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[WARN] Database initialization failed: {ex.Message}");
    Console.WriteLine("[WARN] Application will start in setup mode.");
}

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AllowAllDashboardAuthorizationFilter() }
});

app.UseStaticFiles();
app.UseRouting();

app.UseMiddleware<OsmUpdateUtility.Middleware.SetupMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.UseExceptionHandler("/Error");

try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.CanConnect())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

            var scheduleType = await settings.GetAsync("ScheduleType", "Daily");
            var scheduleHour = await settings.GetIntAsync("ScheduleHour", 3);

            string cronExpression = scheduleType switch
            {
                "Daily" => $"0 {scheduleHour} * * *",
                "Every3Days" => $"0 {scheduleHour} */3 * *",
                "Weekly" => $"0 {scheduleHour} * * 0",
                _ => $"0 {scheduleHour} * * *"
            };

            var localTimeZone = TimeZoneInfo.Local;

            RecurringJob.AddOrUpdate<CheckUpdatesJob>(
                "check-osm-updates",
                job => job.ExecuteAsync(),
                cronExpression,
                new RecurringJobOptions { TimeZone = localTimeZone });

            Console.WriteLine($"[INFO] Recurring job scheduled: '{cronExpression}' (Asia/Vladivostok)");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[WARN] Failed to schedule recurring job: {ex.Message}");
}

app.MapRazorPages();
app.Run();

public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}