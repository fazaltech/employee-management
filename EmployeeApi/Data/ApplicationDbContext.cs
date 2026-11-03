using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Organization>().Property(x => x.Name).HasMaxLength(160);
        builder.Entity<OrganizationMembership>().HasKey(x => new { x.OrganizationId, x.UserId });
        builder.Entity<OrganizationMembership>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        builder.Entity<OrganizationMembership>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
        builder.Entity<OrganizationMembership>().HasIndex(x => new { x.UserId, x.IsActive });
        builder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique();

        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<IdentityRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
    }
}
