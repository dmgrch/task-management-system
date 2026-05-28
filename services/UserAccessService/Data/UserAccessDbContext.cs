using Microsoft.EntityFrameworkCore;
using UserAccessService.Entities;

namespace UserAccessService.Data;

public class UserAccessDbContext : DbContext
{
    public UserAccessDbContext(DbContextOptions<UserAccessDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureProjectMember(modelBuilder);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<User>();

        user.ToTable("users");

        user.HasKey(x => x.Id);

        user.Property(x => x.FullName).IsRequired().HasMaxLength(200);

        user.Property(x => x.Email).IsRequired().HasMaxLength(320);

        user.HasIndex(x => x.Email).IsUnique();

        user.Property(x => x.CreatedAt).IsRequired();

        user.Property(x => x.IsActive).IsRequired();
    }

    private static void ConfigureProjectMember(ModelBuilder modelBuilder)
    {
        var projectMember = modelBuilder.Entity<ProjectMember>();

        projectMember.ToTable("project_members");

        projectMember.HasKey(x => x.Id);

        projectMember.Property(x => x.ProjectId).IsRequired();

        projectMember.Property(x => x.UserId).IsRequired();

        projectMember.Property(x => x.Role).HasConversion<string>().IsRequired().HasMaxLength(30);

        projectMember.Property(x => x.JoinedAt).IsRequired();

        projectMember.Property(x => x.IsActive).IsRequired();

        projectMember.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();

        projectMember.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}