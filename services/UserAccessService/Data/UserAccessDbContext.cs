using Microsoft.EntityFrameworkCore;
using UserAccessService.Entities;

namespace UserAccessService.Data;

public class UserAccessDbContext : DbContext
{
    public UserAccessDbContext(DbContextOptions<UserAccessDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var user = modelBuilder.Entity<User>();

        user.ToTable("users");

        user.HasKey(x => x.Id);

        user.Property(x => x.FullName).IsRequired().HasMaxLength(200);

        user.Property(x => x.Email).IsRequired().HasMaxLength(320);

        user.HasIndex(x => x.Email).IsUnique();

        user.Property(x => x.CreatedAt).IsRequired();

        user.Property(x => x.IsActive).IsRequired();
    }
}