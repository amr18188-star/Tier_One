using Xunit.Abstractions;

namespace Tier1.IntegrationTests;

/// <summary>
/// Account and role administration (UsersAdminService, RbacAdminService) through the API:
/// who may act, duplicate and invalid requests, and the two-person rules (SOD-9, SOD-10).
/// By the catalog defaults the system administrator manages accounts, the president assigns roles,
/// and role permissions change only by a super administrator's request that another super administrator approves.
/// </summary>
[Collection("app")]
public sealed class AccessAdminTests(Tier1App app, ITestOutputHelper output)
{
    private async Task<string> RoleIdAsync(string code) =>
        (await (await app.As("sa1")).Get("/api/iam/roles")).Items().Single(r => r["code"].Str() == code)["id"].Str()!;

    [Fact]
    public async Task Accounts_are_managed_by_administrators_and_reserved_roles_need_two_super_administrators()
    {
        var t = new Scenario(output);
        var sys = await app.As("sysadmin");
        var president = await app.As("president");
        var sa1 = await app.As("sa1");
        var sa2 = await app.As("sa2");
        var email = $"it.{Calls.Tag()}@tier1.local";
        var body = Scenario.O(("email", email), ("fullNameAr", "حساب اختبار التكامل"), ("fullNameEn", null));

        t.Step("Creating an account");
        t.Check((await (await app.As("viewer")).Call("POST", "/api/iam/users", body)).Status == 403, "A viewer cannot create accounts (403)");
        t.Check((await sys.Call("POST", "/api/iam/users", Scenario.With(body, ("email", "not-an-email")))).Status == 400, "An invalid email is refused (400)");
        var created = await sys.Ok("POST", "/api/iam/users", body);
        var userId = created["userId"].Str();
        t.Check(created["temporaryPassword"].Str() is { Length: >= 12 }, "A temporary password is returned once");
        var (status, error) = await sys.Call("POST", "/api/iam/users", Scenario.With(body, ("email", email.ToUpperInvariant())));
        t.Check(status == 409 && error.Code() == "duplicate_email", $"The same email twice, in any letter case, is a conflict (409), got {status} {error.Code()}");

        t.Step("Assigning roles");
        var entryRole = await RoleIdAsync("DE");
        var assign = Scenario.O(("roleId", entryRole), ("scopeValues", new JsonArray("ENG")), ("validFrom", null), ("validTo", null), ("reason", "مدخل بيانات جديد للهندسة"));
        t.Check((await sys.Call("POST", $"/api/iam/users/{userId}/roles", assign)).Status == 403, "The system administrator views role assignments but does not make them (403)");
        t.Check((await president.Call("POST", $"/api/iam/users/{userId}/roles", Scenario.With(assign, ("reason", "x")))).Status == 400, "An assignment without a clear reason is refused (400)");
        await president.Ok("POST", $"/api/iam/users/{userId}/roles", assign);
        t.Check((await sys.Get($"/api/iam/users/{userId}/effective"))!["roles"].Items().Any(r => r.Str() == "DE"), "The data entry role is in force");
        (status, error) = await president.Call("POST", $"/api/iam/users/{userId}/roles", assign);
        t.Check(status == 409 && error.Code() == "already_assigned", "The same role twice is a conflict (409)");

        t.Step("The super administrator role (SOD-9, SOD-10)");
        var assignSa = Scenario.O(("roleId", await RoleIdAsync("SA")), ("scopeValues", null), ("validFrom", null), ("validTo", null), ("reason", "مدير أعلى احتياطي للاختبار"));
        (status, error) = await president.Call("POST", $"/api/iam/users/{userId}/roles", assignSa);
        t.Check(status == 403 && error.Code() == "sod_sod_9", $"Only a super administrator assigns the super administrator role (SOD-9), got {status} {error.Code()}");
        var pending = await sa1.Ok("POST", $"/api/iam/users/{userId}/roles", assignSa);
        t.Check(pending["roleCode"].Str() == "SA", "The super administrator assignment was requested");
        t.Check(!(await sys.Get($"/api/iam/users/{userId}/effective"))!["isSuperAdmin"].Truthy(), "It waits for approval before it takes effect");
        (status, error) = await sa1.Call("POST", $"/api/iam/assignments/{pending["id"].Str()}/approve");
        t.Check(status == 403 && error.Code() == "sod_sod_10", $"The one who assigned it cannot approve it (SOD-10), got {status} {error.Code()}");
        await sa2.Ok("POST", $"/api/iam/assignments/{pending["id"].Str()}/approve", expect: 204);
        t.Check((await sys.Get($"/api/iam/users/{userId}/effective"))!["isSuperAdmin"].Truthy(), "Approved by an independent super administrator, it takes effect");

        t.Step("Deactivation");
        var deactivate = Scenario.O(("reason", "انتهاء اختبار التكامل"));
        (status, error) = await sys.Call("POST", $"/api/iam/users/{userId}/deactivate", deactivate);
        t.Check(status == 403 && error.Code() == "sod_sod_9", $"An account holding a reserved role is managed by a super administrator only (SOD-9), got {status} {error.Code()}");
        await sa2.Ok("POST", $"/api/iam/users/{userId}/deactivate", deactivate, expect: 204);
        (status, error) = await sa2.Call("POST", $"/api/iam/users/{userId}/roles", Scenario.With(assign, ("roleId", await RoleIdAsync("VIEW"))));
        t.Check(status == 409 && error.Code() == "inactive", $"A deactivated account takes no new role (409), got {status} {error.Code()}");
        t.Finish();
    }

    [Fact]
    public async Task Role_permissions_change_only_when_another_super_administrator_approves()
    {
        var t = new Scenario(output);
        var sa1 = await app.As("sa1");
        var sa2 = await app.As("sa2");
        var code = "IT_" + Calls.Tag(4).ToUpperInvariant();

        t.Step("Creating a role as a copy");
        var create = Scenario.O(("code", code), ("nameAr", "دور اختبار التكامل"), ("nameEn", null), ("descriptionAr", null), ("copyFromRoleId", await RoleIdAsync("VIEW")));
        t.Check((await (await app.As("viewer")).Call("POST", "/api/iam/roles", create)).Status == 403, "A viewer cannot create roles (403)");
        t.Check((await sa1.Call("POST", "/api/iam/roles", Scenario.With(create, ("code", "bad code")))).Status == 400, "An invalid role code is refused (400)");
        t.Check((await sa1.Call("POST", "/api/iam/roles", Scenario.With(create, ("code", "IT_SYS" + Calls.Tag(2).ToUpperInvariant()), ("copyFromRoleId", await RoleIdAsync("SYS"))))).Status == 403,
            "A reserved role cannot be copied (SOD-9)");
        var role = await sa1.Ok("POST", "/api/iam/roles", create);
        var roleId = role["id"].Str();
        var (status, error) = await sa1.Call("POST", "/api/iam/roles", create);
        t.Check(status == 409 && error.Code() == "duplicate_code", "The same role code twice is a conflict (409)");
        t.Check(role["pendingChangeId"].Str() is not null && (await sa1.Get($"/api/iam/roles/{roleId}"))!["grants"]!.AsObject().Count == 0,
            "The copy grants nothing yet: it is a change request waiting for a decision");

        t.Step("Deciding the copy");
        var copyId = role["pendingChangeId"].Str();
        (status, error) = await sa1.Call("POST", $"/api/iam/changes/{copyId}/approve", Scenario.O(("note", null)));
        t.Check(status == 403 && error.Code() == "sod_sod_10", $"The requester cannot approve their own change (SOD-10), got {status} {error.Code()}");
        t.Check((await sa2.Ok("POST", $"/api/iam/changes/{copyId}/approve", Scenario.O(("note", "موافق"))))["status"].Str() == "Approved", "Another super administrator approved the copy");
        var detail = (await sa1.Get($"/api/iam/roles/{roleId}"))!;
        var grants = detail["grants"]!.AsObject();
        var version = detail["role"]!["grantsVersion"].Num();
        t.Check(grants.Count > 1, $"The role now holds the copied permissions ({grants.Count} features)");

        t.Step("Changing the role");
        var dropped = grants.Select(g => g.Key).First(k => k != "dash.home");
        var proposed = new JsonObject();
        foreach (var (key, value) in grants) if (key != dropped) proposed[key] = value!.DeepClone();
        var change = await sa1.Ok("PUT", $"/api/iam/roles/{roleId}/grants", Scenario.O(("baseVersion", version), ("reason", "إزالة خاصية للاختبار"), ("grants", proposed)));
        t.Check(change["status"].Str() == "Pending", "The change waits for a decision");
        (status, error) = await sa1.Call("PUT", $"/api/iam/roles/{roleId}/grants", Scenario.O(("baseVersion", version), ("reason", "طلب ثان للدور نفسه"), ("grants", proposed)));
        t.Check(status == 409 && error.Code() == "change_pending", $"A second request while one waits is a conflict (409), got {status} {error.Code()}");
        t.Check((await sa1.Get($"/api/iam/roles/{roleId}"))!["grants"]![dropped] is not null, "Nothing changed before approval");
        await sa2.Ok("POST", $"/api/iam/changes/{change["id"].Str()}/approve", Scenario.O(("note", "موافق")));
        var after = (await sa1.Get($"/api/iam/roles/{roleId}"))!;
        t.Check(after["grants"]![dropped] is null && after["role"]!["grantsVersion"].Num() == version + 1, "The change is in force and the role version moved on");
        t.Finish();
    }

    /// <summary>
    /// ADR-0019: the account's title comes from a closed list and goes before the two-part name wherever the name is shown.
    /// AC-N5: the list is served; AC-N6: a title outside it is refused; AC-N7: title and two-part name in the accounts list and
    /// the account's page; AC-N8: clearing the title shows the full name again.
    /// </summary>
    [Fact]
    public async Task AC_N5_to_N8_an_account_title_goes_before_the_two_part_name()
    {
        var t = new Scenario(output);
        var sys = await app.As("sysadmin");

        t.Step("The list of titles");
        var titles = (await sys.Get("/api/iam/users/titles")).Items().ToList();
        t.Check(titles.Any(x => x["title"].Str() == "أ.د." && x["group"].Str() == "Faculty") && titles.Any(x => x["title"].Str() == "السيدة" && x["group"].Str() == "Staff")
            && titles.Any(x => x["title"].Str() == "لواء" && x["group"].Str() == "Officer"), "Faculty titles, Mr and Ms, and military ranks are offered (AC-N5)");
        t.Check((await (await app.As("entry")).Call("GET", "/api/iam/users/titles")).Status == 403, "Someone who does not manage accounts does not read the list (403)");
        // The value sent and saved is the Arabic title; the label beside it is what the interface language shows.
        t.Check(titles.All(x => x["title"].Str() is { Length: > 0 } && x["label"].Str() is { Length: > 0 } && x["meaning"].Str() is { Length: > 0 }),
            "Each title carries its saved value, the label shown and its meaning");

        t.Step("Creating an account with a title");
        var email = $"it.{Calls.Tag()}@tier1.local";
        var body = Scenario.O(("email", email), ("fullNameAr", "أحمد عبد الرحمن علي حسن"), ("fullNameEn", null), ("title", "أ.د."));
        var (status, error) = await sys.Call("POST", "/api/iam/users", Scenario.With(body, ("title", "فخامة")));
        t.Check(status == 400 && error.Code() == "invalid_title", $"A title outside the list is refused (AC-N6), got {status} {error.Code()}");
        var userId = (await sys.Ok("POST", "/api/iam/users", body))["userId"].Str();
        var listed = (await sys.Get($"/api/iam/users?q={Uri.EscapeDataString(email)}")).Items().Single();
        t.Check(listed["name"].Str() == "أ.د. أحمد عبد الرحمن", $"The accounts list shows the title and the two-part name (AC-N7), got {listed["name"].Str()}");
        var detail = (await sys.Get($"/api/iam/users/{userId}"))!;
        t.Check(detail["title"].Str() == "أ.د." && detail["displayName"].Str() == "أ.د. أحمد عبد الرحمن" && detail["fullNameAr"].Str() == "أحمد عبد الرحمن علي حسن",
            "The account keeps its full name and its title beside the shown name");

        t.Step("Changing and clearing the title");
        var update = Scenario.O(("fullNameAr", "أحمد عبد الرحمن علي حسن"), ("fullNameEn", null), ("title", "عقيد"));
        t.Check((await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.With(update, ("title", "أستاذ")))).Status == 400, "A free-text title is refused on edit too");
        t.Check((await sys.Call("PUT", $"/api/iam/users/{userId}", update)).Status == 204, "The title is changed");
        t.Check((await sys.Get($"/api/iam/users/{userId}"))!["displayName"].Str() == "عقيد أحمد عبد الرحمن", "The new title is shown");
        t.Check((await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.With(update, ("title", null)))).Status == 204, "The title is cleared");
        t.Check((await sys.Get($"/api/iam/users/{userId}"))!["displayName"].Str() == "أحمد عبد الرحمن علي حسن", "Without a title the full name is shown as written (AC-N8)");
        t.Finish();
    }
}
