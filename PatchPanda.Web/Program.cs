using PatchPanda.Web.Components;
using PatchPanda.Web.Services.Background;

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

builder.Services.AddDbContextFactory<DataContext>(opt =>
{
    opt.UseSqlite("Data Source=patchpanda.db");

#if DEBUG
    opt.EnableSensitiveDataLogging();
#endif
});

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
