using System.Text.Json;

namespace PatchPanda.Web.Services;

public class DataService : IDisposable
{
    private readonly DockerService _dockerService;
    private readonly ILogger<DataService> _logger;

    public DataService(DockerService dockerService, ILogger<DataService> logger)
    {
        _dockerService = dockerService;
        _logger = logger;
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
        Constants.COMPOSE_APPS = await _dockerService.GetAllComposeStacks();
    }
}
