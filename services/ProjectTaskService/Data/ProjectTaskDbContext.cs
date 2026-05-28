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
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskHistory> TaskHistories => Set<TaskHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureProject(modelBuilder);
        ConfigureTask(modelBuilder);
        ConfigureTaskHistory(modelBuilder);
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
    
    private static void ConfigureTask(ModelBuilder modelBuilder)
    {
    var task = modelBuilder.Entity<TaskItem>();
    task.ToTable("tasks");
    task.HasKey(x => x.Id);
    task.Property(x => x.ProjectId).IsRequired();
    task.Property(x => x.Title).IsRequired().HasMaxLength(250);
    task.Property(x => x.Description).HasMaxLength(10000);
    task.Property(x => x.Status).IsRequired();
    task.Property(x => x.AssigneeUserId);
    task.Property(x => x.CreatedByUserId).IsRequired();
    task.Property(x => x.CreatedAt).IsRequired();
    task.Property(x => x.UpdatedAt).IsRequired();
    task.Property(x => x.IsDeleted).IsRequired();
    }
    
    private static void ConfigureTaskHistory(ModelBuilder modelBuilder)
    {
    var history = modelBuilder.Entity<TaskHistory>();
    history.ToTable("task_histories");
    history.HasKey(x => x.Id);
    history.Property(x => x.TaskId).IsRequired();
    history.Property(x => x.ChangedByUserId).IsRequired();
    history.Property(x => x.ChangeType).IsRequired();
    history.Property(x => x.OldValue);
    history.Property(x => x.NewValue);
    history.Property(x => x.CreatedAt).IsRequired();
    }
}