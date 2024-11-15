using Docker.DotNet;
using Docker.DotNet.Models;
using PatchPanda.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

DockerClient client = new DockerClientConfiguration(
    new Uri("tcp://host.docker.internal:2375")
).CreateClient();

IList<ContainerListResponse> containers = await client.Containers.ListContainersAsync(
    new ContainersListParameters() { Limit = 10, }
);

foreach (var container in containers)
{
    if (!container.Image.StartsWith("ghcr.io"))
        continue;

    Console.WriteLine(
        $"Container {container.Names[0]}, ID: {container.ID}, Image: {container.Image}"
    );
}

app.Run();
