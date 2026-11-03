using EmployeeApi.Controllers;
using EmployeeApi.Data;
using EmployeeApi.Infrastructure;
using EmployeeApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace EmployeeApi.Tests;

public sealed class HrmSecurityTests
{
    [Theory]
    [InlineData("PendingManager", "Manager", "PendingHR")]
    [InlineData("PendingHR", "HRAdmin", "PendingOwner")]
    [InlineData("PendingOwner", "OrganizationOwner", "Approved")]
    public void ApprovalMovesOnlyOneStage(string stage, string role, string expected)
        => Assert.Equal(expected, HrWorkflowsController.NextStage(stage, role, true));
    [Theory]
    [InlineData("PendingManager", "OrganizationOwner")]
    [InlineData("PendingManager", "Employee")]
    [InlineData("PendingHR", "Manager")]
    [InlineData("PendingOwner", "HRAdmin")]
    [InlineData("Approved", "OrganizationOwner")]
    [InlineData("Rejected", "Manager")]
    public void SkippedOrRepeatedApprovalsFail(string stage, string role)
        => Assert.Throws<ApiException>(() => HrWorkflowsController.NextStage(stage, role, true));
    [Theory]
    [InlineData("PendingManager", "Manager")]
    [InlineData("PendingHR", "HRAdmin")]
    [InlineData("PendingOwner", "OrganizationOwner")]
    public void RejectionIsTerminal(string stage, string role)
        => Assert.Equal("Rejected", HrWorkflowsController.NextStage(stage, role, false));

    [Fact]
    public async Task MigratedDatabaseEnforcesOwnershipAndTenantMembership()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync(); // initialization is repeatable
        var owner = new ApplicationUser { Id = "owner", UserName = "owner", NormalizedUserName = "OWNER", NormalizedEmail = "OWNER" };
        var other = new ApplicationUser { Id = "other", UserName = "other", NormalizedUserName = "OTHER", NormalizedEmail = "OTHER" };
        db.Users.AddRange(owner, other); await db.SaveChangesAsync();
        db.Organizations.Add(new Organization {Id = "a", Name = "A", OwnerUserId = owner.Id});
        db.OrganizationMemberships.Add(new OrganizationMembership {OrganizationId = "a", UserId = owner.Id, Role = OrganizationRoles.Owner});
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE OrganizationMemberships SET IsActive = 0 WHERE UserId = 'owner'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM OrganizationMemberships WHERE UserId = 'owner'"));
        db.OrganizationMemberships.Add(new OrganizationMembership {OrganizationId = "a", UserId = "other", Role = OrganizationRoles.Owner});
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.Organizations.Add(new Organization {Id = "b", Name = "B", OwnerUserId = "owner"});
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.Roles.Add(new IdentityRole {Id = "platform", Name = "PlatformAdmin", NormalizedName = "PLATFORMADMIN"}); await db.SaveChangesAsync();
        db.UserRoles.Add(new IdentityUserRole<string> {RoleId = "platform", UserId = "owner"});
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
