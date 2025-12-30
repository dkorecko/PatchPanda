using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PatchPanda.Web.Db;

public class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public DbSet<ComposeStack> Stacks { get; set; } = default!;

    public DbSet<MultiContainerApp> MultiContainerApps { get; set; } = default!;

    public DbSet<Container> Containers { get; set; } = default!;

    public DbSet<AppVersion> AppVersions { get; set; } = default!;

    public DbSet<AppSetting> AppSettings { get; set; } = default!;

    public DbSet<UpdateAttempt> UpdateAttempts { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder
            .Entity<Container>()
            .HasOne(c => c.MultiContainerApp)
            .WithMany(m => m.Containers)
            .HasForeignKey(c => c.MultiContainerAppId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder
            .Entity<ComposeStack>()
            .HasMany(x => x.Apps)
            .WithOne(x => x.Stack)
            .HasForeignKey(x => x.StackId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder
            .Entity<Container>()
            .HasMany(x => x.NewerVersions)
            .WithMany(x => x.Applications);

        modelBuilder
            .Entity<Container>()
            .HasMany(x => x.UpdateAttempts)
            .WithOne(x => x.Container)
            .HasForeignKey(x => x.ContainerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder
            .Entity<ComposeStack>()
            .HasMany(x => x.UpdateAttempts)
            .WithOne(x => x.Stack)
            .HasForeignKey(x => x.StackId)
            .OnDelete(DeleteBehavior.Cascade);

        var tupleStringConverter = new ValueConverter<Tuple<string, string>?, string?>(
            v => v == null ? null : v.Item1 + "/" + v.Item2,
            v =>
                v == null
                    ? null
                    : Tuple.Create(
                        v.Split('/', StringSplitOptions.None)[0],
                        v.Split('/', StringSplitOptions.None)[1]
                    )
        );

        var jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.General);

        var listStringConverter = new ValueConverter<List<Tuple<string, string>>?, string?>(
            v =>
                JsonSerializer.Serialize(
                    v,
                    typeof(List<Tuple<string, string>>),
                    jsonSerializerOptions
                ),
            v =>
                v == null
                    ? null
                    : JsonSerializer.Deserialize<List<Tuple<string, string>>>(
                        v,
                        jsonSerializerOptions
                    ) ?? new List<Tuple<string, string>>()
        );

        modelBuilder
            .Entity<Container>()
            .Property(x => x.GitHubRepo)
            .HasConversion(tupleStringConverter);

        modelBuilder
            .Entity<Container>()
            .Property(x => x.OverrideGitHubRepo)
            .HasConversion(tupleStringConverter);

        modelBuilder
            .Entity<Container>()
            .Property(x => x.SecondaryGitHubRepos)
            .HasConversion(listStringConverter);
    }
}
