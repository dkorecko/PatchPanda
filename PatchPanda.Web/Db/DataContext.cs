namespace PatchPanda.Web.Db;

public class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public DbSet<ComposeStack> Stacks { get; set; } = default!;

    public DbSet<MultiContainerApp> MultiContainerApps { get; set; } = default!;

    public DbSet<Container> Containers { get; set; } = default!;

    public DbSet<AppVersion> AppVersions { get; set; } = default!;

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
    }
}
