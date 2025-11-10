using PatchPanda.Web.Components;
using PatchPanda.Web.Services.Background;

namespace PatchPanda.Web;

public sealed partial class Program
{
    private const string DatabaseName = "patchpanda.db";

    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        builder.Services.AddScoped<DockerService>();
        builder.Services.AddScoped<VersionService>();
        builder.Services.AddScoped<DiscordService>();
        builder.Services.AddScoped<UpdateService>();
        builder.Services.AddScoped<IFileService, SystemFileService>();
        builder.Services.AddSingleton<UpdateRegistry>();
        builder.Services.AddSingleton<UpdateQueue>();
        builder.Services.AddHostedService<VersionCheckHostedService>();
        builder.Services.AddHostedService<UpdateBackgroundService>();

        var baseUrl = builder.Configuration.GetValue<string?>("BASE_URL");

        Constants.BASE_URL = baseUrl is null ? null : baseUrl.TrimEnd('/');

#if DEBUG
        builder.Services.AddDbContextFactory<DataContext>(CreateDebugDatabase);
#else
    if (OperatingSystem.IsWindows())
    {
        builder.Services.AddDbContextFactory<DataContext>(CreateDatabaseAtWorkingFolder);
    }
    else
    {
        builder.Services.AddDbContextFactory<DataContext>(CreateDatabaseAtRoot);
    }
#endif

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>()!;

            if (dbContext.Database.IsRelational())
                dbContext.Database.Migrate();
        }

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        app.Run();
    }

    private static void CreateDebugDatabase(DbContextOptionsBuilder opt)
    {
        opt.UseSqlite($"Data Source={DatabaseName}");
        opt.EnableSensitiveDataLogging();
    }

    private static void CreateDatabaseAtWorkingFolder(DbContextOptionsBuilder opt)
    {
        opt.UseSqlite($"Data Source={DatabaseName}");
    }

    private static void CreateDatabaseAtRoot(DbContextOptionsBuilder opt)
    {
        Directory.CreateDirectory("/app/data");
        opt.UseSqlite($"Data Source=/app/data/{DatabaseName}");
    }
}