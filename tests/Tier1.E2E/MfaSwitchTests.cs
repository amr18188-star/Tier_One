using Xunit.Abstractions;

namespace Tier1.E2E;

/// <summary>
/// D11 (ADR-0007): the "two-step verification for decision holders" switch is a system setting only a super administrator
/// changes, with a reason. Development turns MFA off as a whole, so this checks the switch, not the enforcement (unit-tested).
/// It puts the switch back as it found it.
/// </summary>
public sealed class MfaSwitchTests(ITestOutputHelper output)
{
    private const string Key = "security_mfa_for_decisions";

    [E2EFact]
    public async Task Only_a_super_administrator_flips_the_mfa_switch()
    {
        var t = new Scenario(output);
        var president = await Env.As("president");
        var admin = await Env.As("sa1");

        t.Step("1. The switch is listed, off by default, and the president cannot change it");
        var listed = (await president.Get("/api/admin/settings")).Items().First(s => s["key"].Str() == Key);
        var original = listed["value"].Num();
        t.Check(listed["isSwitch"].Truthy() && listed["superAdminOnly"].Truthy() && listed["defaultValue"].Num() == 0,
            "The switch is a super-admin-only on/off setting, off by default");
        t.Check(!listed["editable"].Truthy(), "It is read-only for the president, who edits other settings");
        t.Check((await president.Get("/api/admin/settings")).Items().Any(s => s["key"].Str() != Key && s["editable"].Truthy()),
            "The president still edits the other settings");
        var (status, err) = await president.Call("PUT", $"/api/admin/settings/{Key}", Scenario.O(("value", 1), ("reason", "تجربة صلاحية التغيير")));
        t.Check(status == 403 && err.Code() == "sod_sod_10", $"The president is refused ({status} {err.Code()})");

        t.Step("2. The super administrator turns it on with a reason, then back");
        t.Check((await admin.Get("/api/admin/settings")).Items().First(s => s["key"].Str() == Key)["editable"].Truthy(), "Editable for the super administrator");
        (status, _) = await admin.Call("PUT", $"/api/admin/settings/{Key}", Scenario.O(("value", 1), ("reason", "")));
        t.Check(status == 400, "A change needs a reason");
        try
        {
            var on = await admin.Ok("PUT", $"/api/admin/settings/{Key}", Scenario.O(("value", 1), ("reason", "تفعيل المصادقة الثنائية لأصحاب القرارات")));
            t.Check(on["value"].Num() == 1, "Turned on");
            t.Check((await president.Get("/api/admin/settings")).Items().First(s => s["key"].Str() == Key)["value"].Num() == 1, "Everyone sees it on");
        }
        finally
        {
            await admin.Ok("PUT", $"/api/admin/settings/{Key}", Scenario.O(("value", original ?? 0), ("reason", "إعادة المفتاح كما كان بعد الاختبار")));
        }
        t.Finish();
    }
}
