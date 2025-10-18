using PatchPanda.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddScoped<DockerService>();
builder.Services.AddScoped<VersionService>();
builder.Services.AddScoped<DataService>();
builder.Services.AddScoped<DiscordService>();
builder.Services.AddHostedService<VersionCheckHostedService>();

var app = builder.Build();

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
