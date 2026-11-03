"""Live API verification using NEW isolated SQLite/MongoDB databases. Never resets existing data.
Requires a built API and local MongoDB; retains fixtures for inspection.
"""
import json, os, secrets, socket, subprocess, time, urllib.request, urllib.error, uuid
from pathlib import Path
root = Path(__file__).resolve().parents[1]
run = uuid.uuid4().hex
sql = root / "EmployeeApi" / ("identity.verification." + run + ".db")
password = "Verify!" + secrets.token_hex(16)
changed_password = "Changed!" + secrets.token_hex(16)
with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0)); port = sock.getsockname()[1]
base = "http://127.0.0.1:" + str(port)
env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=base,
           ConnectionStrings__DefaultConnection="Data Source=" + str(sql),
           MongoDb__ConnectionString="mongodb://localhost:27017/", MongoDb__DatabaseName="Hrm_test_" + run + "_development",
           Jwt__Key=secrets.token_hex(32), Jwt__Issuer="verification", Jwt__Audience="verification",
           Logging__LogLevel__Default="Warning", Authentication__RequireConfirmedEmail="false", Notifications__EmailEnabled="false", PLATFORM_ADMIN_INITIAL_PASSWORD=password)
process = subprocess.Popen(["dotnet", str(root / "EmployeeApi/bin/Debug/net10.0/EmployeeApi.dll")], cwd=root / "EmployeeApi", env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
checks = 0
def call(method, path, body=None, token=None, expected=200):
    global checks
    headers = {"Content-Type": "application/json", "Idempotency-Key": str(uuid.uuid4())}
    if token: headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=15) as response: status, data = response.status, response.read()
    except urllib.error.HTTPError as error: status, data = error.code, error.read()
    if status != expected:
        # Do not print request bodies, tokens, or account responses.
        raise AssertionError(method + " " + path + ": expected " + str(expected) + ", received " + str(status) + "; " + (data.decode() if status >= 400 else "unexpected success"))
    checks += 1
    return json.loads(data) if data else None
def login(email, secret=password): return call("POST", "/api/auth/login", {"email":email,"password":secret})["token"]
try:
    for attempt in range(60):
        if process.poll() is not None:
            print(process.stdout.read().decode(errors="replace")); raise RuntimeError("Verification API failed during initialization.")
        try:
            urllib.request.urlopen(base, timeout=1); break
        except (OSError, urllib.error.URLError): time.sleep(.5)
    else: raise RuntimeError("Verification API did not become ready.")
    platform = login("platform@peopleops.com")
    call("GET", "/api/platform/organizations", token=platform, expected=403)
    platform = call("POST", "/api/auth/change-password", {"currentPassword":password,"newPassword":changed_password}, platform)["token"]
    def organization(name, email):
        return call("POST", "/api/organizations", {"name":name,"owner":{"fullName":"Owner", "email":email,"password":password}}, platform, 201)["id"]
    org = organization("Verification A", "owner-a@example.test")
    other = organization("Verification B", "owner-b@example.test")
    owner = login("owner-a@example.test"); other_owner = login("owner-b@example.test")
    url = "/api/organizations/" + org
    call("GET", url + "/employees", token=other_owner, expected=403)
    call("GET", url + "/employees", token=platform, expected=403)
    call("POST", "/api/organizations", {"name":"Forbidden", "owner":{"fullName":"No", "email":"no@example.test", "password":password}}, owner, 403)
    department = call("POST", url + "/departments", {"name":"Engineering"}, owner, 201)["id"]
    accounts = {}
    for role, email in [("HRAdmin", "hr@example.test"), ("Manager", "manager@example.test")]:
        accounts[role] = call("POST", url + "/accounts", {"fullName":role,"email":email,"password":password,"role":role}, owner, 201)["id"]
    hr = login("hr@example.test"); manager = login("manager@example.test")
    call("POST", url + "/accounts", {"fullName":"No", "email":"no@example.test", "password":password,"role":"OrganizationOwner"}, hr, 409)
    def employee(number,email,manager_id=None,secret=None):
        body={"employeeNumber":number,"firstName":number,"lastName":"Test","email":email,"departmentId":department,"jobTitle":"Engineer","salary":999,"hireDate":"2026-01-01T00:00:00Z","isActive":True,"provisionAppAccess":True,"managerEmployeeId":manager_id}
        if secret: body["password"]=secret
        return call("POST", url + "/employees", body, owner, 201)
    mgr = employee("M1","manager@example.test")
    emp = employee("E1","employee@example.test",mgr["id"],password)
    unrelated = employee("E2","unrelated@example.test",None,password)
    staff=login("employee@example.test")
    call("GET",url+"/employees/"+unrelated["id"],token=manager,expected=404)
    call("GET",url+"/employees/"+mgr["id"],token=staff,expected=404)
    call("POST",url+"/attendance/check-in",{"employeeId":emp["id"]},manager,403)
    attendance=call("POST",url+"/attendance/check-in",{"employeeId":emp["id"]},staff)
    call("POST",url+"/attendance/check-in",{"employeeId":emp["id"]},staff,409)
    call("POST",url+"/attendance/"+emp["id"]+"/"+attendance["workDate"]+"/check-out",{},staff)
    leave=call("POST",url+"/leaves",{"from":"2027-01-01","to":"2027-01-02","reason":"Vacation"},staff)
    decision=url+"/leaves/"+leave["id"]+"/decision"
    call("POST",decision,{"approved":True},owner,403)
    call("POST",decision,{"approved":True},staff,403)
    call("POST",decision,{"approved":True,"comment":"Manager approved"},manager)
    call("POST",decision,{"approved":True},manager,403)
    call("POST",decision,{"approved":True},hr)
    approved=call("POST",decision,{"approved":True},owner)
    assert approved["status"]=="Approved" and len(approved["decisions"])==3
    call("POST",decision,{"approved":True},owner,409)
    shift_body={"name":"Day","startTime":"09:00","endTime":"17:00","breakMinutes":30,"workingDays":[1,2,3,4,5]}
    call("POST",url+"/shifts",shift_body,hr,403)
    shift=call("POST",url+"/shifts",shift_body,owner)
    assignment={"employeeId":emp["id"],"shiftId":shift["id"],"effectiveFrom":"2026-01-01","effectiveTo":"2026-12-31"}
    call("POST",url+"/shift-assignments",assignment,hr)
    call("POST",url+"/shift-assignments",assignment,hr,409)
    salary=call("POST",url+"/salaries",{"employeeId":emp["id"],"amount":50000,"currency":"PKR","effectiveFrom":"2026-01-01"},hr)
    assert call("GET",url+"/salaries",token=staff)==[]
    call("POST",url+"/salaries/"+salary["id"]+"/approve",{},hr,403)
    call("POST",url+"/salaries/"+salary["id"]+"/approve",{},owner,204)
    assert len(call("GET",url+"/salaries",token=staff))==1
    assert call("GET",url+"/salaries",token=manager)==[]
    payroll=call("POST",url+"/payroll",{"period":"2026-11"},hr)
    assert call("GET",url+"/payslips",token=staff)==[]
    call("POST",url+"/payroll/"+payroll["id"]+"/approve",{},hr,403)
    call("POST",url+"/payroll/"+payroll["id"]+"/approve",{},owner,204)
    call("POST",url+"/payroll/"+payroll["id"]+"/approve",{},owner,204)
    slips=call("GET",url+"/payslips",token=staff)
    assert len(slips)==1 and slips[0]["employeeId"]==emp["id"]
    call("GET",url+"/payroll",token=staff,expected=403)
    call("PUT",url+"/status",{"status":"Suspended"},platform,204)
    call("GET",url+"/leaves",token=staff,expected=403)
    # Restart with the same databases: migrations, indexes and seed must be idempotent.
    process.terminate(); process.wait(timeout=10)
    process = subprocess.Popen(["dotnet", str(root / "EmployeeApi/bin/Debug/net10.0/EmployeeApi.dll")], cwd=root / "EmployeeApi", env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    for attempt in range(60):
        if process.poll() is not None: raise RuntimeError("Repeat initialization failed.")
        try: urllib.request.urlopen(base, timeout=1); break
        except (OSError, urllib.error.URLError): time.sleep(.5)
    else: raise RuntimeError("Restart did not become ready.")
    platform = login("platform@peopleops.com", changed_password)
    assert len(call("GET", "/api/platform/organizations", token=platform)) == 2
    print("PASS:",checks,"live API checks, including tenant isolation, leave stages, payroll privacy and attendance sequencing.")
    print("Retained isolated verification databases:", sql.name, env["MongoDb__DatabaseName"])
finally:
    process.terminate()
    try: process.wait(timeout=10)
    except subprocess.TimeoutExpired: process.kill(); process.wait()

