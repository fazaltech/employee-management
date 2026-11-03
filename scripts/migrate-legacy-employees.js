// Run with mongosh, with the API stopped and a verified backup. Dry-run is the default.
// EMS_ORGANIZATION_ID must identify an existing SQLite organization with an owner.
// EMS_EMPLOYEE_IDS is a JSON array of explicitly approved legacy employee ObjectIds.
const organizationId = process.env.EMS_ORGANIZATION_ID;
const ids = JSON.parse(process.env.EMS_EMPLOYEE_IDS || '[]');
const apply = process.env.EMS_APPLY === 'yes';
if (!organizationId || !/^[0-9a-f-]{36}$/i.test(organizationId) || !Array.isArray(ids) || !ids.length) {
  throw new Error('Set EMS_ORGANIZATION_ID and a nonempty EMS_EMPLOYEE_IDS JSON array.');
}
const employees = db.getCollection(process.env.EMS_EMPLOYEES_COLLECTION || 'Employees');
const departments = db.getCollection('Departments');
const unassigned = { $or: [{ OrganizationId: { $exists: false } }, { OrganizationId: '' }, { OrganizationId: null }] };
const plan = [];
const numbers = new Set(); const emails = new Set();
for (const id of ids) {
  if (typeof id !== 'string' || !/^[0-9a-f]{24}$/i.test(id)) throw new Error('Invalid employee ID.');
  const employee = employees.findOne({ _id: ObjectId(id), $or: [...unassigned.$or, { OrganizationId: organizationId }] });
  if (!employee) throw new Error('Employee is missing or already belongs to another organization.');
  if (employee.OrganizationId === organizationId && employee.DepartmentId) continue; // Retry after partial completion.
  const number = (employee.EmployeeNumber || '').trim().toUpperCase();
  const email = (employee.Email || '').trim().toLowerCase();
  const name = (employee.Department || '').trim() || 'Unassigned';
  if (!number || !email || numbers.has(number) || emails.has(email)) throw new Error('Resolve empty or duplicate employee numbers/emails before migration.');
  numbers.add(number); emails.add(email);
  if (employees.findOne({ OrganizationId: organizationId, _id: { $ne: employee._id }, $or: [{ EmployeeNumber: number }, { Email: email }] }))
    throw new Error('Target organization already contains a matching number or email.');
  plan.push({ employee, number, email, name });
}
print(`Validated ${plan.length} employees. Mode: ${apply ? 'APPLY' : 'DRY RUN'}.`);
if (apply) {
  departments.createIndex({ OrganizationId: 1, NormalizedName: 1 }, { unique: true });
  for (const item of plan) {
    const now = new Date(); const normalizedName = item.name.toUpperCase();
    departments.updateOne({ OrganizationId: organizationId, NormalizedName: normalizedName }, {
      $setOnInsert: { Name: item.name, CreatedAtUtc: now, UpdatedAtUtc: now }
    }, { upsert: true });
    const department = departments.findOne({ OrganizationId: organizationId, NormalizedName: normalizedName });
    const result = employees.updateOne({ _id: item.employee._id, ...unassigned }, {
      $set: { OrganizationId: organizationId, DepartmentId: department._id.toString(),
        EmployeeNumber: item.number, Email: item.email, UpdatedAtUtc: now }
    });
    if (result.matchedCount !== 1) throw new Error('Employee changed during migration; stop and inspect before retrying.');
  }
  print('Migration completed. Legacy Department text was retained. No account access was granted.');
}
