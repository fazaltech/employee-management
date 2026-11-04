using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<SupportConversation> SupportConversations => Set<SupportConversation>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<SupportConversation>().HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        builder.Entity<SupportConversation>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupportConversation>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupportMessage>().HasOne<SupportConversation>().WithMany().HasForeignKey(x => x.ConversationId);
        builder.Entity<SupportMessage>().HasIndex(x => new { x.ConversationId, x.Id });
        builder.Entity<SupportMessage>().Property(x => x.Text).HasMaxLength(4000);
        builder.Entity<Organization>().HasIndex(x => x.OwnerUserId).IsUnique();
        builder.Entity<Organization>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrganizationMembership>().HasIndex(x => x.OrganizationId).IsUnique().HasFilter("Role = 'OrganizationOwner'");
        builder.Entity<OrganizationMembership>().HasIndex(x => x.UserId).IsUnique();
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
