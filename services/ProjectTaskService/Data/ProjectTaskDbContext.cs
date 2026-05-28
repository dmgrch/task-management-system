using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Entities;

namespace ProjectTaskService.Data;

public class ProjectTaskDbContext : DbContext
{
    public ProjectTaskDbContext(DbContextOptions<ProjectTaskDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureProject(modelBuilder);
    }

    private static void ConfigureProject(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<Project>();

        project.ToTable("projects");

        project.HasKey(x => x.Id);

        project.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(250);

        project.Property(x => x.Description)
            .HasMaxLength(10000);

        project.Property(x => x.CreatedByUserId)
            .IsRequired();

        project.Property(x => x.CreatedAt)
            .IsRequired();

        project.Property(x => x.UpdatedAt)
            .IsRequired();

        project.Property(x => x.IsArchived)
            .IsRequired();
    }
}