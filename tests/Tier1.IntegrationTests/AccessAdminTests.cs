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
        var body = Calls.Account(email, "حساب اختبار التكامل");

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
        var body = Scenario.With(Calls.Account(email, "أحمد عبد الرحمن علي حسن", "Ahmed Abdelrahman Ali Hassan"), ("title", "أ.د."));
        var (status, error) = await sys.Call("POST", "/api/iam/users", Scenario.With(body, ("title", "فخامة")));
        t.Check(status == 400 && error.Code() == "invalid_title", $"A title outside the list is refused (AC-N6), got {status} {error.Code()}");
        var userId = (await sys.Ok("POST", "/api/iam/users", body))["userId"].Str();
        var listed = (await sys.Get($"/api/iam/users?q={Uri.EscapeDataString(email)}")).Items().Single();
        t.Check(listed["name"].Str() == "أ.د. أحمد عبد الرحمن", $"The accounts list shows the title and the two-part name (AC-N7), got {listed["name"].Str()}");
        var detail = (await sys.Get($"/api/iam/users/{userId}"))!;
        t.Check(detail["title"].Str() == "أ.د." && detail["displayName"].Str() == "أ.د. أحمد عبد الرحمن" && detail["fullNameAr"].Str() == "أحمد عبد الرحمن علي حسن",
            "The account keeps its full name and its title beside the shown name");

        t.Step("Changing and clearing the title");
        var update = Scenario.O(("fullNameAr", "أحمد عبد الرحمن علي حسن"), ("fullNameEn", "Ahmed Abdelrahman Ali Hassan"), ("title", "عقيد"), ("phone", body["phone"]));
        t.Check((await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.With(update, ("title", "أستاذ")))).Status == 400, "A free-text title is refused on edit too");
        t.Check((await sys.Call("PUT", $"/api/iam/users/{userId}", update)).Status == 204, "The title is changed");
        t.Check((await sys.Get($"/api/iam/users/{userId}"))!["displayName"].Str() == "عقيد أحمد عبد الرحمن", "The new title is shown");
        t.Check((await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.With(update, ("title", null)))).Status == 204, "The title is cleared");
        t.Check((await sys.Get($"/api/iam/users/{userId}"))!["displayName"].Str() == "أحمد عبد الرحمن علي حسن", "Without a title the full name is shown as written (AC-N8)");
        t.Finish();
    }

    /// <summary>
    /// ADR-0021: every account carries its holder's full data. AC-D4: an account is not created without the English name, the national
    /// ID and the mobile number, and each refusal names its field; AC-D5: the national ID is on one account only; AC-D6: it never leaves
    /// the server in full; AC-D7: an edit keeps the national ID on file when none is sent, and needs the rest; AC-D8: an account from
    /// before the rule is marked incomplete and is completed the first time it is edited.
    /// </summary>
    [Fact]
    public async Task AC_D4_to_D8_an_account_carries_its_holders_full_data()
    {
        static (string? Field, string? Code) First(JsonNode? error) =>
            error?["errors"]?.AsArray().FirstOrDefault() is { } e ? (e["field"].Str(), e["code"].Str()) : (null, null);

        var t = new Scenario(output);
        var sys = await app.As("sysadmin");
        var email = $"it.{Calls.Tag()}@tier1.local";
        var body = Calls.Account(email, "منى السيد محمود علي", "Mona Elsayed Mahmoud Ali");
        var nationalId = body["nationalId"].Str()!;

        t.Step("AC-D4: nothing of the required data may be left out or wrong");
        async Task RefusedAsync(string field, object? value, string code, string what)
        {
            var (status, error) = await sys.Call("POST", "/api/iam/users", Scenario.With(body, (field, value)));
            t.Check(status == 400 && error.Code() == code && First(error) == (field, code), $"{what} ({code}), got {status} {error.Code()} on {First(error).Field}");
        }
        await RefusedAsync("fullNameEn", null, "name_en_required", "No English name");
        await RefusedAsync("fullNameEn", "منى السيد", "invalid_name_en", "An English name in Arabic letters");
        await RefusedAsync("nationalId", null, "national_id_required", "No national ID");
        await RefusedAsync("nationalId", nationalId[..13], "invalid_national_id", "A national ID of 13 digits");
        await RefusedAsync("nationalId", "2" + "991340" + nationalId[7..], "invalid_national_id", "A national ID with month 13");
        await RefusedAsync("nationalId", nationalId[..7] + "99" + nationalId[9..], "invalid_national_id", "A national ID with an unknown governorate");
        await RefusedAsync("phone", null, "phone_required", "No mobile number");
        await RefusedAsync("phone", "0225551234", "invalid_phone", "A landline number");

        t.Step("Created with complete data");
        var userId = (await sys.Ok("POST", "/api/iam/users", Scenario.With(body, ("phone", "+20 10 1234 5678"))))["userId"].Str();
        var detail = (await sys.Get($"/api/iam/users/{userId}"))!;
        t.Check(detail["phone"].Str() == "01012345678", $"The mobile number is kept in one form, got {detail["phone"].Str()}");
        t.Check(detail["fullNameEn"].Str() == "Mona Elsayed Mahmoud Ali" && !detail["missingData"].Items().Any(), "Both names are kept and nothing is missing");
        var listed = (await sys.Get($"/api/iam/users?q={Uri.EscapeDataString(email)}")).Items().Single();
        t.Check(!listed["incomplete"].Truthy(), "The accounts list does not mark it incomplete");

        t.Step("AC-D6: the national ID never leaves the server in full");
        t.Check(detail["nationalIdMasked"].Str() == new string('*', 10) + nationalId[^4..], $"The account shows the last four digits only, got {detail["nationalIdMasked"].Str()}");
        t.Check(!detail.ToJsonString().Contains(nationalId, StringComparison.Ordinal) && !listed.ToJsonString().Contains(nationalId, StringComparison.Ordinal),
            "Neither the account nor the list carries the full number");

        t.Step("AC-D9: the database holds the national ID encrypted, never in the clear");
        t.Check(await app.CountAsync("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'iam' AND table_name = 'users' AND column_name = 'national_id'") == 0,
            "There is no clear-text national ID column");
        t.Check(await app.CountAsync("SELECT count(*) FROM iam.users WHERE email = @e AND length(national_id_protected) > 40 AND national_id_hash ~ '^[0-9a-f]{64}$'", ("e", email)) == 1,
            "The account holds an encrypted value and a 64-digit fingerprint");
        t.Check(await app.CountAsync("SELECT count(*) FROM iam.users u WHERE u::text LIKE @id", ("id", $"%{nationalId}%")) == 0, "No row of the accounts table holds the number");
        t.Check(await app.CountAsync("SELECT count(*) FROM logs.outbox WHERE payload::text LIKE @id", ("id", $"%{nationalId}%")) == 0, "Nor does the audit log's outbox");

        t.Step("AC-D5: one account per national ID");
        var other = Scenario.With(Calls.Account($"it.{Calls.Tag()}@tier1.local", "حساب آخر"), ("nationalId", nationalId));
        var (status, error) = await sys.Call("POST", "/api/iam/users", other);
        t.Check(status == 409 && error.Code() == "duplicate_national_id" && First(error).Field == "nationalId", $"The same national ID on a second account is a conflict (409), got {status} {error.Code()}");
        var otherId = (await sys.Ok("POST", "/api/iam/users", Scenario.With(other, ("nationalId", Calls.Person().NationalId))))["userId"].Str();

        t.Step("AC-D7: an edit keeps the national ID on file, and needs the rest");
        var update = Scenario.O(("fullNameAr", "منى السيد محمود علي"), ("fullNameEn", "Mona Elsayed Mahmoud Ali"), ("phone", "٠١١٢٣٤٥٦٧٨٩"));
        t.Check((await sys.Call("PUT", $"/api/iam/users/{userId}", update)).Status == 204, "An edit without a national ID is saved");
        detail = (await sys.Get($"/api/iam/users/{userId}"))!;
        t.Check(detail["nationalIdMasked"].Str()!.EndsWith(nationalId[^4..], StringComparison.Ordinal) && detail["phone"].Str() == "01123456789",
            $"The national ID on file is kept, and the new number is saved in Latin digits, got {detail["phone"].Str()}");
        (status, error) = await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.With(update, ("phone", null)));
        t.Check(status == 400 && error.Code() == "phone_required", $"An edit without the mobile number is refused, got {status} {error.Code()}");
        (status, error) = await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.With(update, ("fullNameEn", null)));
        t.Check(status == 400 && error.Code() == "name_en_required", $"An edit without the English name is refused, got {status} {error.Code()}");
        (status, error) = await sys.Call("PUT", $"/api/iam/users/{otherId}", Scenario.With(update, ("nationalId", nationalId)));
        t.Check(status == 409 && error.Code() == "duplicate_national_id", $"An edit cannot take another account's national ID, got {status} {error.Code()}");
        var replacement = Calls.Person().NationalId;
        t.Check((await sys.Call("PUT", $"/api/iam/users/{userId}", Scenario.With(update, ("nationalId", replacement)))).Status == 204, "A new national ID replaces the one on file");
        t.Check((await sys.Get($"/api/iam/users/{userId}"))!["nationalIdMasked"].Str() == new string('*', 10) + replacement[^4..], "The account shows the new number's last four digits");
        t.Check((await sys.Call("POST", "/api/iam/users", Scenario.With(Calls.Account($"it.{Calls.Tag()}@tier1.local", "حساب ثالث"), ("nationalId", nationalId)))).Status == 200,
            "The number it gave up is free for another account");

        t.Step("AC-D8: an account from before the rule is marked incomplete until it is edited with full data");
        var old = (await sys.Get("/api/iam/users?q=viewer@tier1.local")).Items().Single();
        t.Check(old["incomplete"].Truthy(), "The accounts list marks it incomplete");
        var oldDetail = (await sys.Get($"/api/iam/users/{old["id"].Str()}"))!;
        var missing = oldDetail["missingData"].Items().Select(m => m.Str()).ToList();
        t.Check(missing.Contains("nationalId") && missing.Contains("phone") && oldDetail["nationalIdMasked"].Str() is null, $"Its page names what is missing: {string.Join(", ", missing)}");
        (status, error) = await sys.Call("PUT", $"/api/iam/users/{old["id"].Str()}",
            Scenario.O(("fullNameAr", oldDetail["fullNameAr"]), ("fullNameEn", "Viewer Account"), ("title", oldDetail["title"]), ("phone", "01055555555")));
        t.Check(status == 400 && error.Code() == "national_id_required", $"Editing it without its national ID is refused, got {status} {error.Code()}");
        t.Finish();
    }
}
