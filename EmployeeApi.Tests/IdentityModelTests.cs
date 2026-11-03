using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Tests;

public sealed class IdentityModelTests
{
    [Fact]
    public void ApplicationUser_IsMappedToUsersTable()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new ApplicationDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(ApplicationUser));

        Assert.NotNull(entityType);
        Assert.Equal("Users", entityType.GetTableName());
    }
}
