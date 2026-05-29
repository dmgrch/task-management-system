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
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureProject(modelBuilder);
        ConfigureTask(modelBuilder);
        ConfigureTaskHistory(modelBuilder);
        ConfigureComment(modelBuilder);
        ConfigureLabel(modelBuilder);
        ConfigureTaskLabel(modelBuilder);
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

    private static void ConfigureComment(ModelBuilder modelBuilder)
    {
        var comment = modelBuilder.Entity<Comment>();
        comment.ToTable("comments");
        comment.HasKey(x => x.Id);
        comment.Property(x => x.TaskId).IsRequired();
        comment.Property(x => x.AuthorUserId).IsRequired();
        comment.Property(x => x.Text).IsRequired().HasMaxLength(5000);
        comment.Property(x => x.CreatedAt).IsRequired();
        comment.Property(x => x.IsDeleted).IsRequired();
    }

    private static void ConfigureLabel(ModelBuilder modelBuilder)
    {
        var label = modelBuilder.Entity<Label>();

        label.ToTable("labels");

        label.HasKey(x => x.Id);

        label.Property(x => x.ProjectId)
            .IsRequired();

        label.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);

        label.Property(x => x.IsActive)
            .IsRequired();

        label.HasIndex(x => new { x.ProjectId, x.Name })
            .IsUnique();
    }

private static void ConfigureTaskLabel(ModelBuilder modelBuilder)
    {
        var taskLabel = modelBuilder.Entity<TaskLabel>();

        taskLabel.ToTable("task_labels");

        taskLabel.HasKey(x => new { x.TaskId, x.LabelId });

        taskLabel.Property(x => x.TaskId)
            .IsRequired();

        taskLabel.Property(x => x.LabelId)
            .IsRequired();
    }
}