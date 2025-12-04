using PatchPanda.Web.Components;
using PatchPanda.Web.Services.Background;

namespace PatchPanda.Web;

public sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<DockerService>();
        builder.Services.AddSingleton<IPortainerService, PortainerService>();
        builder.Services.AddSingleton<IVersionService, VersionService>();
        builder.Services.AddSingleton<DiscordService>();
        builder.Services.AddSingleton<AppriseService>();
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddSingleton<IFileService, SystemFileService>();
        builder.Services.AddSingleton<JobRegistry>();
        builder.Services.AddSingleton<UpdateQueue>();
        builder.Services.AddHostedService<VersionCheckHostedService>();
        builder.Services.AddHostedService<UpdateBackgroundService>();

        var baseUrl = builder.Configuration.GetValue<string?>(Constants.VariableKeys.BASE_URL);

        Constants.BASE_URL = baseUrl is null ? null : baseUrl.TrimEnd('/');

#if DEBUG
        builder.Services.AddDbContextFactory<DataContext>(CreateDebugDatabaseAtWorkingFolder);
#else
        builder.Services.AddDbContextFactory<DataContext>(CreateDatabaseAtRoot);
#endif

        var app = builder.Build();

        var dbContext = await app
            .Services.GetRequiredService<IDbContextFactory<DataContext>>()
            .CreateDbContextAsync();

        if (dbContext.Database.IsRelational())
            dbContext.Database.Migrate();

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

    private static void CreateDebugDatabaseAtWorkingFolder(DbContextOptionsBuilder opt)
    {
        opt.UseSqlite($"Data Source={Constants.DB_NAME}");
        opt.EnableSensitiveDataLogging();
    }

    private static void CreateDatabaseAtRoot(DbContextOptionsBuilder opt)
    {
        Directory.CreateDirectory("/app/data");
        opt.UseSqlite($"Data Source=/app/data/{Constants.DB_NAME}");
    }
}
