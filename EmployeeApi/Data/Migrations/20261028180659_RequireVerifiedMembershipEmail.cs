using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequireVerifiedMembershipEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresVerifiedEmail",
                table: "OrganizationMemberships",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresVerifiedEmail",
                table: "OrganizationMemberships");
        }
    }
}
