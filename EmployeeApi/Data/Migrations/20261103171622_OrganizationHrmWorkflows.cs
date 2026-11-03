using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationHrmWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "Organizations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Only unambiguous legacy ownership is migrated. Invalid legacy data fails the
            // following unique/FK constraints instead of silently selecting or deleting owners.
            migrationBuilder.Sql("UPDATE Organizations SET OwnerUserId = (SELECT UserId FROM OrganizationMemberships m WHERE m.OrganizationId = Organizations.Id AND m.Role = 'OrganizationOwner' AND m.IsActive = 1) WHERE (SELECT COUNT(*) FROM OrganizationMemberships m WHERE m.OrganizationId = Organizations.Id AND m.Role = 'OrganizationOwner' AND m.IsActive = 1) = 1;");
            migrationBuilder.CreateIndex(
                name: "IX_Organizations_OwnerUserId",
                table: "Organizations",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_OrganizationId",
                table: "OrganizationMemberships",
                column: "OrganizationId",
                unique: true,
                filter: "Role = 'OrganizationOwner'");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_UserId",
                table: "OrganizationMemberships",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_Users_OwnerUserId",
                table: "Organizations",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_Users_OwnerUserId",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_OwnerUserId",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationMemberships_OrganizationId",
                table: "OrganizationMemberships");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationMemberships_UserId",
                table: "OrganizationMemberships");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Organizations");
        }
    }
}
