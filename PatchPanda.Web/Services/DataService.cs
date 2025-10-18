namespace PatchPanda.Web.Services;

public class DataService
{
    private DockerService _dockerService;

    public DataService(DockerService dockerService)
    {
        _dockerService = dockerService;
    }

    public async Task<IEnumerable<ComposeStack>> GetData()
    {
        if (Constants.COMPOSE_APPS is null)
            await LoadData();

        return Constants.COMPOSE_APPS!;
    }

    private async Task LoadData()
    {
        Constants.COMPOSE_APPS = await _dockerService.GetAllComposeStacks();
    }
}
