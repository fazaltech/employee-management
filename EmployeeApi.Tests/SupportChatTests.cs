using System.Security.Claims;
using EmployeeApi.Data;
using EmployeeApi.Models;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EmployeeApi.Tests;

public sealed class SupportChatTests
{
    [Fact]
    public async Task PrivateThreadsPersistAndAdminCanHandleMultipleUsers()
    {
        await using var fixture = await Fixture.Create();
        var chat = fixture.Chat;
        var owner = await chat.Authorize(fixture.Principal("owner"));
        var manager = await chat.Authorize(fixture.Principal("manager"));
        var admin = await chat.Authorize(fixture.Principal("admin"));
        var ownerThread = Assert.Single(await chat.List(owner));
        Assert.Equal(ownerThread.Id, Assert.Single(await chat.List(owner)).Id);
        var managerThread = Assert.Single(await chat.List(manager));
        Assert.NotEqual(ownerThread.Id, managerThread.Id);
        await chat.Send(owner, ownerThread.Id, "Please help");
        await chat.Send(manager, managerThread.Id, "Manager question");
        Assert.Equal(2, (await chat.List(admin)).Count);
        await chat.Send(admin, ownerThread.Id, "Owner reply");
        await chat.Send(admin, managerThread.Id, "Manager reply");
        fixture.Db.ChangeTracker.Clear();
        var history = await chat.History(owner, ownerThread.Id, null);
        Assert.Equal(new[] { "Please help", "Owner reply" }, history.Select(x => x.Text));
        Assert.True(history[1].FromAdmin);
        Assert.Single(await chat.History(owner, ownerThread.Id, history[1].Id));
        Assert.Equal(new[] { "admin", "owner" }, (await chat.Recipients(ownerThread.Id)).Order());
        await Assert.ThrowsAsync<HubException>(() => chat.History(manager, ownerThread.Id, null));
        await Assert.ThrowsAsync<HubException>(() => chat.Send(manager, ownerThread.Id, "forged"));
        await Assert.ThrowsAsync<HubException>(() => chat.History(owner, "missing", null));
        await Assert.ThrowsAsync<HubException>(() => chat.Send(owner, ownerThread.Id, "  "));
        await Assert.ThrowsAsync<HubException>(() => chat.Send(owner, ownerThread.Id, new string('x', 4001)));
    }

    [Fact]
    public async Task AccessIsRecheckedAfterRoleMembershipAndAccountChanges()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(f.Principal("employee")));
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(f.Principal("hr")));
        var principal = f.Principal("manager");
        await f.Chat.Authorize(principal);
        var membership = await f.Db.OrganizationMemberships.SingleAsync(x => x.UserId == "manager");
        membership.Role = OrganizationRoles.Employee; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(principal));
        membership.Role = OrganizationRoles.Manager; membership.IsActive = false; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(principal));
        membership.IsActive = true;
        var org = await f.Db.Organizations.SingleAsync(); org.Status = "Suspended"; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(principal));
        org.Status = "Active";
        var user = await f.Db.Users.SingleAsync(x => x.Id == "manager"); user.SecurityStamp = "changed"; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(principal));
        user.SecurityStamp = "stamp"; user.MustChangePassword = true; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(principal));
        user.MustChangePassword = false; user.EmailConfirmed = false; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(principal));
        f.Db.UserRoles.Remove(await f.Db.UserRoles.SingleAsync()); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<HubException>(() => f.Chat.Authorize(f.Principal("admin")));
    }

    private sealed class Fixture(SqliteConnection connection, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db => db;
        public SupportChatService Chat { get; } = new(db, new ConfigurationBuilder().Build());
        public ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, id), new Claim("security_stamp", "stamp") }, "Test"));
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.MigrateAsync();
            foreach (var id in new[] { "owner", "manager", "employee", "hr", "admin" })
                db.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), NormalizedEmail = id.ToUpperInvariant(), FullName = id, SecurityStamp = "stamp", EmailConfirmed = true });
            db.Roles.Add(new IdentityRole { Id = "admin-role", Name = "PlatformAdmin", NormalizedName = "PLATFORMADMIN" });
            await db.SaveChangesAsync();
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = "admin", RoleId = "admin-role" });
            db.Organizations.Add(new Organization { Id = "org", OwnerUserId = "owner", Name = "Organization" });
            await db.SaveChangesAsync();
            foreach (var (id, role) in new[] { ("owner", OrganizationRoles.Owner), ("manager", OrganizationRoles.Manager), ("employee", OrganizationRoles.Employee), ("hr", OrganizationRoles.HR) })
                db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = "org", UserId = id, Role = role });
            await db.SaveChangesAsync();
            return new(connection, db);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
