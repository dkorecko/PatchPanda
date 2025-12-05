using Microsoft.AspNetCore.Mvc;

namespace PatchPanda.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InfoController : ControllerBase
{
    private readonly IDbContextFactory<DataContext> _dbFactory;

    public InfoController(IDbContextFactory<DataContext> dbFactory, IWebHostEnvironment env)
    {
        _dbFactory = dbFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var stacks = await db
            .Stacks.Include(x => x.Apps)
            .ThenInclude(x => x.NewerVersions)
            .AsNoTracking()
            .ToListAsync();

        var result = new
        {
            stackCount = stacks.Count,
            containerCount = stacks.Sum(x => x.Apps.Count),
            toBeUpdatedContainersCount = stacks.Sum(x =>
                x.Apps.Count(app => app.NewerVersions.Any(v => !v.Ignored))
            )
        };

        return new JsonResult(result);
    }
}
