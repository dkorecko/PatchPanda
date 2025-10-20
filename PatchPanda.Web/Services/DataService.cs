using System.Text.Json;

namespace PatchPanda.Web.Services;

public class DataService : IDisposable
{
    private readonly ILogger<DataService> _logger;
    private readonly IServiceScopeFactory _serviceProvider;

    public DataService(ILogger<DataService> logger, IServiceScopeFactory serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public void Dispose()
    {
        var path = GetDataFilePath();
        File.WriteAllText(path, JsonSerializer.Serialize(Constants.COMPOSE_APPS));
    }

    private string GetDataFilePath()
    {
        string path = "C:\\Users\\PC\\Coding\\self-host\\data.json";
#if !DEBUG
        path = "/media/data/data.json";
#endif
        return path;
    }

    public async Task<IEnumerable<ComposeStack>> GetData()
    {
        if (Constants.COMPOSE_APPS is null)
        {
            string path = GetDataFilePath();

            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                Constants.COMPOSE_APPS = JsonSerializer.Deserialize<IEnumerable<ComposeStack>>(
                    json
                );

                if (Constants.COMPOSE_APPS is null)
                    await UpdateData();
                else
                    _logger.LogInformation("Data retrieved from previous app run.");
            }
            else
            {
                await UpdateData();
            }
        }

        return Constants.COMPOSE_APPS!;
    }

    public async Task UpdateData()
    {
        using var scope = _serviceProvider.CreateScope();
        var dockerService = scope.ServiceProvider.GetRequiredService<DockerService>();
        Constants.COMPOSE_APPS = await dockerService.GetAllComposeStacks();
    }
}
