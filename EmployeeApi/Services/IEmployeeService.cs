using EmployeeApi.Models;

namespace EmployeeApi.Services;

public interface IEmployeeService
{
    Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Employee?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task CreateAsync(Employee employee, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Employee employee, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
