using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace EmployeeApi.Data.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261103180000_OwnershipGuards")]
public sealed class OwnershipGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.Sql(@"CREATE TRIGGER MembershipOwnerInsert BEFORE INSERT ON OrganizationMemberships BEGIN
                SELECT RAISE(ABORT, 'Invalid owner membership') WHERE
                  (NEW.Role = 'OrganizationOwner' AND (NEW.IsActive != 1 OR NOT EXISTS (SELECT 1 FROM Organizations WHERE Id = NEW.OrganizationId AND OwnerUserId = NEW.UserId)))
                  OR (NEW.Role != 'OrganizationOwner' AND EXISTS (SELECT 1 FROM Organizations WHERE OwnerUserId = NEW.UserId));
                SELECT RAISE(ABORT, 'Platform accounts cannot join organizations') WHERE EXISTS (SELECT 1 FROM UserRoles ur JOIN Roles r ON r.Id = ur.RoleId WHERE ur.UserId = NEW.UserId AND r.Name = 'PlatformAdmin');
            END;");
            migrationBuilder.Sql(@"CREATE TRIGGER MembershipOwnerUpdate BEFORE UPDATE ON OrganizationMemberships BEGIN
                SELECT RAISE(ABORT, 'Ownership is immutable') WHERE (OLD.Role = 'OrganizationOwner' OR NEW.Role = 'OrganizationOwner') AND (NEW.UserId != OLD.UserId OR NEW.OrganizationId != OLD.OrganizationId OR NEW.Role != OLD.Role OR NEW.IsActive != 1);
            END;");
            migrationBuilder.Sql(@"CREATE TRIGGER MembershipOwnerDelete BEFORE DELETE ON OrganizationMemberships WHEN OLD.Role = 'OrganizationOwner' AND EXISTS (SELECT 1 FROM Organizations WHERE Id = OLD.OrganizationId) BEGIN SELECT RAISE(ABORT, 'Organization must retain its owner'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER OrganizationOwnerUpdate BEFORE UPDATE OF OwnerUserId ON Organizations WHEN NEW.OwnerUserId != OLD.OwnerUserId BEGIN SELECT RAISE(ABORT, 'Ownership is immutable'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER PlatformRoleInsert BEFORE INSERT ON UserRoles WHEN EXISTS (SELECT 1 FROM Roles WHERE Id = NEW.RoleId AND Name = 'PlatformAdmin') AND EXISTS (SELECT 1 FROM OrganizationMemberships WHERE UserId = NEW.UserId) BEGIN SELECT RAISE(ABORT, 'Organization accounts cannot become PlatformAdmin'); END;");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
            foreach (var trigger in new[] { "MembershipOwnerInsert", "MembershipOwnerUpdate", "MembershipOwnerDelete", "OrganizationOwnerUpdate", "PlatformRoleInsert" })
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {trigger};");
    }
}
