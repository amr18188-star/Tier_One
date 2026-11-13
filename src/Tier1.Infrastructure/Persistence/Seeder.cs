using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;
using Tier1.Domain.Organization;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// بذر البيانات الأساسية: أدوار الكتالوج بمنحها الافتراضية مرة واحدة (تعديلات المصفوفة اللاحقة لا تُستبدل)،
/// والبيانات المرجعية لـ Tier 1 على قاعدة فارغة، وفي التطوير فقط كليات وحسابات تجريبية لكل دور.
/// </summary>
public sealed class Seeder(Tier1DbContext db, IPasswordHasher hasher, ISecretProtector protector, IConfiguration config, ILogger<Seeder> log)
{
    public const string DemoDomain = "tier1.local";

    /// <summary>كليات تجريبية للتطوير فقط؛ الكليات الحقيقية تدخل من شاشة الجهات أو باستيراد قائمتها الرسمية.</summary>
    private static readonly (string Code, string NameAr, (string Code, string NameAr)[] Departments)[] DemoColleges =
    [
        ("ENG", "كلية الهندسة (تجريبية)", [("ENG-CIV", "قسم الهندسة المدنية"), ("ENG-ELE", "قسم الهندسة الكهربية"), ("ENG-MEC", "قسم الهندسة الميكانيكية")]),
        ("CS", "كلية الحاسبات والذكاء الاصطناعي (تجريبية)", [("CS-SE", "قسم هندسة البرمجيات"), ("CS-AI", "قسم الذكاء الاصطناعي")]),
        ("COM", "كلية التجارة (تجريبية)", [("COM-ACC", "قسم المحاسبة"), ("COM-BUS", "قسم إدارة الأعمال")]),
    ];

    /// <summary>جهات مركزية تجريبية تملك مؤشرات مستوى الجامعة، فيصير لكل منها نموذج «الإدخال ← الجودة المركزية».</summary>
    private static readonly (string Code, string NameAr, OrgUnitKind Kind)[] DemoCentralUnits =
    [
        ("QAC", "وحدة التخطيط الاستراتيجي والجودة (تجريبية)", OrgUnitKind.Administration),
        ("STU", "الإدارة العامة لشؤون الطلاب والمدن الجامعية (تجريبية)", OrgUnitKind.Administration),
        ("DIG", "مركز التحول الرقمي (تجريبي)", OrgUnitKind.Center),
        ("INTL", "مركز العلاقات الدولية (تجريبي)", OrgUnitKind.Center),
    ];

    public static readonly IReadOnlyList<(string Email, string NameAr, string NameEn, string Role, string[] Scope)> DemoUsers =
    [
        ("sa1@" + DemoDomain, "مدير أعلى أول", "First super admin", "SA", []),
        ("sa2@" + DemoDomain, "مدير أعلى ثانٍ", "Second super admin", "SA", []),
        ("president@" + DemoDomain, "رئيس الجامعة", "University president", "PRES", []),
        ("vp@" + DemoDomain, "نائب رئيس الجامعة", "Vice president", "VP", ["ENG", "CS"]),
        ("committee@" + DemoDomain, "عضو اللجنة العليا", "Higher committee member", "HC", []),
        ("planner@" + DemoDomain, "مدير متابعة الخطة", "Plan monitoring manager", "PM", []),
        ("quality@" + DemoDomain, "الجودة المركزية", "Central quality", "QA", []),
        ("dean@" + DemoDomain, "عميد كلية الهندسة", "Dean of engineering", "DEAN", ["ENG"]),
        ("college@" + DemoDomain, "لجنة Tier 1 بكلية الهندسة", "Engineering Tier 1 committee", "CTC", ["ENG"]),
        ("deptrep@" + DemoDomain, "ممثل Tier 1 بقسم المدنية", "Civil department representative", "DREP", ["ENG-CIV"]),
        ("entry@" + DemoDomain, "مدخل بيانات كلية الهندسة", "Engineering data entry", "DE", ["ENG"]),
        ("viewer@" + DemoDomain, "مستخدم اطلاع", "Viewer", "VIEW", ["CS"]),
        ("sysadmin@" + DemoDomain, "مسؤول النظام", "System administrator", "SYS", []),
        ("auditor@" + DemoDomain, "المراجع الرقابي", "Oversight auditor", "AUD", []),
        // بقية سلسلة الاعتماد لكل كلية وقسم، ليمر كل نموذج تجريبي بمساره كاملًا بفصل المهام.
        ("rep.ele@" + DemoDomain, "ممثل Tier 1 بقسم الكهربية", "Electrical department representative", "DREP", ["ENG-ELE"]),
        ("rep.mec@" + DemoDomain, "ممثل Tier 1 بقسم الميكانيكية", "Mechanical department representative", "DREP", ["ENG-MEC"]),
        ("entry.cs@" + DemoDomain, "مدخل بيانات كلية الحاسبات", "Computing data entry", "DE", ["CS"]),
        ("college.cs@" + DemoDomain, "لجنة Tier 1 بكلية الحاسبات", "Computing Tier 1 committee", "CTC", ["CS"]),
        ("dean.cs@" + DemoDomain, "عميد كلية الحاسبات", "Dean of computing", "DEAN", ["CS"]),
        ("rep.se@" + DemoDomain, "ممثل Tier 1 بقسم هندسة البرمجيات", "Software engineering representative", "DREP", ["CS-SE"]),
        ("rep.ai@" + DemoDomain, "ممثل Tier 1 بقسم الذكاء الاصطناعي", "AI department representative", "DREP", ["CS-AI"]),
        ("entry.com@" + DemoDomain, "مدخل بيانات كلية التجارة", "Commerce data entry", "DE", ["COM"]),
        ("college.com@" + DemoDomain, "لجنة Tier 1 بكلية التجارة", "Commerce Tier 1 committee", "CTC", ["COM"]),
        ("dean.com@" + DemoDomain, "عميد كلية التجارة", "Dean of commerce", "DEAN", ["COM"]),
        ("rep.acc@" + DemoDomain, "ممثل Tier 1 بقسم المحاسبة", "Accounting representative", "DREP", ["COM-ACC"]),
        ("rep.bus@" + DemoDomain, "ممثل Tier 1 بقسم إدارة الأعمال", "Business administration representative", "DREP", ["COM-BUS"]),
        ("entry.central@" + DemoDomain, "مدخل بيانات الجهات المركزية", "Central units data entry", "DE", ["QAC", "STU", "DIG", "INTL"]),
        // مؤشرات الإطار التي تقاس على مستوى الجامعة كلها (المالية والحوكمة والتحول الرقمي...) تدخل في نموذج الجامعة نفسها.
        ("tier1@" + DemoDomain, "مسئول ملف Tier 1", "Tier 1 file officer", "T1O", []),
        ("entry.uni@" + DemoDomain, "مدخل بيانات الجامعة (المؤشرات المركزية)", "University-level data entry", "DE", ["CU"]),
        // على الهيكل الرسمي: وحدة التخطيط الاستراتيجي (QAC ← SPU) ومركز الاتصالات وتكنولوجيا المعلومات (DIG ← ICT)، يطلعان على الجامعة كاملة.
        // الوحدة تعد الخطة الاستراتيجية، فحسابها بدور مدير متابعة الخطة (يرفع وثيقة الخطة ويدير شجرتها).
        ("spu@" + DemoDomain, "مسئول وحدة التخطيط الاستراتيجي", "Strategic planning unit", "PM", ["QAC"]),
        ("ict@" + DemoDomain, "مسئول مركز الاتصالات وتكنولوجيا المعلومات", "ICT centre", "DE", ["DIG"]),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await SeedRolesAsync(now, ct);
        await ReferenceSeed.ApplyAsync(db, now, log, ct);
        if (config.GetValue<bool>("Seed:Demo"))
        {
            // النسخة التجريبية تعمل على هيكل الجامعة الرسمي (Seed:Structure)؛ قاعدة التطوير والاختبارات على الكليات التجريبية الثلاث.
            var structure = await UniversityStructure.LoadedAsync(db, ct);
            if (!structure && config.GetValue<bool>("Seed:Structure"))
            {
                await UniversityStructure.ApplyAsync(db, now, ct);
                structure = true;
                log.LogInformation("Loaded the official university structure.");
            }
            if (!structure) await SeedDemoCollegesAsync(now, ct);
            await SeedDemoUsersAsync(structure, now, ct);
        }
    }

    private async Task SeedRolesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Roles.Select(r => r.Code).ToListAsync(ct);
        foreach (var def in PermissionCatalog.Roles.Where(r => !existing.Contains(r.Code)))
        {
            var role = new Role
            {
                Code = def.Code,
                NameAr = def.Name.Ar,
                NameEn = def.Name.En,
                DescriptionAr = def.DescriptionAr,
                IsSystem = true,
                CreatedAt = now,
            };
            if (def.Code == PermissionCatalog.SuperAdminRole)
            {
                role.Grants = RoleRules.FullGrants().Select(g => new RoleGrant { Feature = g.Feature, Caps = g.Caps, Scope = g.Scope, Specials = [.. g.Specials] }).ToList();
            }
            else
            {
                foreach (var f in PermissionCatalog.Features)
                {
                    var caps = f.Defaults.TryGetValue(def.Code, out var d) ? d.Caps : Cap.None;
                    var specials = f.Specials.Where(s => s.DefaultRoles.Contains(def.Code)).Select(s => s.Key).ToList();
                    if (caps == Cap.None && specials.Count == 0) continue;
                    role.Grants.Add(new RoleGrant { Feature = f.Key, Caps = caps, Scope = d?.Scope ?? AccessScope.All, Specials = specials });
                }
            }
            db.Roles.Add(role);
            log.LogInformation("Seeded role {Code} with {Grants} grants.", def.Code, role.Grants.Count);
        }
        await db.SaveChangesAsync(ct);
        await SeedNewFeatureGrantsAsync(now, ct);
        await ScopeUnitDirectoryOnceAsync(now, ct);
    }

    /// <summary>
    /// قرار الجامعة (نوفمبر 2026): مستخدم الكلية أو الإدارة لا يرى إلا جهته، ومنها دليل الجهات نفسه.
    /// تحول منح «الجهات» في أدوار الكتالوج المقيدة بجهة من «الكل» إلى «نطاقه» مرة واحدة؛ ما يغيره المدير بعدها من المصفوفة يبقى.
    /// </summary>
    private async Task ScopeUnitDirectoryOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        const string key = "catalog-fix:org.units-unit-scope";
        if (await db.Settings.AnyAsync(s => s.Key == key, ct)) return;
        string[] unitRoles = ["VP", "DEAN", "CTC", "DREP", "DE", "VIEW"];
        var grants = await (from g in db.RoleGrants
                            join r in db.Roles on g.RoleId equals r.Id
                            where r.IsSystem && unitRoles.Contains(r.Code) && g.Feature == Features.Units && g.Scope == AccessScope.All
                            select g).ToListAsync(ct);
        foreach (var g in grants) g.Scope = AccessScope.Unit;
        db.Settings.Add(new SystemSetting { Key = key, Value = "1", UpdatedAt = now });
        await db.SaveChangesAsync(ct);
        if (grants.Count > 0)
        {
            await db.AccessStates.Where(s => s.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), ct);
            log.LogInformation("Unit-bound roles now see only their own units in the unit directory ({Count} grants).", grants.Count);
        }
    }

    /// <summary>
    /// خاصية أضيفت إلى الكتالوج في إصدار لاحق تأخذ منحها الافتراضية في أدوار الكتالوج القائمة، مرة واحدة لكل خاصية:
    /// تسجل في الإعدادات فلا تعاد إن سحبها المدير بعد ذلك من المصفوفة.
    /// </summary>
    private async Task SeedNewFeatureGrantsAsync(DateTimeOffset now, CancellationToken ct)
    {
        const string prefix = "catalog:";
        var seeded = (await db.Settings.Where(s => s.Key.StartsWith(prefix)).Select(s => s.Key).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var roles = await db.Roles.Include(r => r.Grants).Where(r => r.IsSystem).ToListAsync(ct);
        var changed = false;
        foreach (var f in PermissionCatalog.Features.Where(f => !seeded.Contains(prefix + f.Key)))
        {
            foreach (var role in roles.Where(r => r.Grants.All(g => g.Feature != f.Key)))
            {
                if (role.Code == PermissionCatalog.SuperAdminRole)
                {
                    db.RoleGrants.Add(new RoleGrant { RoleId = role.Id, Feature = f.Key, Caps = f.AllowedCaps, Scope = AccessScope.All, Specials = [.. f.Specials.Select(s => s.Key)] });
                    changed = true;
                    continue;
                }
                var caps = f.Defaults.TryGetValue(role.Code, out var d) ? d.Caps : Cap.None;
                var specials = f.Specials.Where(s => s.DefaultRoles.Contains(role.Code)).Select(s => s.Key).ToList();
                if (caps == Cap.None && specials.Count == 0) continue;
                db.RoleGrants.Add(new RoleGrant { RoleId = role.Id, Feature = f.Key, Caps = caps, Scope = d?.Scope ?? AccessScope.All, Specials = specials });
                changed = true;
            }
            db.Settings.Add(new SystemSetting { Key = prefix + f.Key, Value = "1", UpdatedAt = now });
        }
        await db.SaveChangesAsync(ct);
        if (changed)
        {
            await db.AccessStates.Where(s => s.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), ct);
            log.LogInformation("Granted newly added catalog features to the existing roles.");
        }
    }

    private async Task SeedDemoCollegesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var root = await db.OrgUnits.FirstAsync(u => u.Kind == OrgUnitKind.University, ct);
        var order = 0;
        foreach (var (code, name, departments) in DemoColleges)
        {
            order++;
            if (await db.OrgUnits.AnyAsync(u => u.Code == code, ct)) continue;
            var college = new OrgUnit { Code = code, NameAr = name, Kind = OrgUnitKind.College, ParentId = root.Id, Path = OrgUnit.PathOf(root.Path, code), SortOrder = order, CreatedAt = now };
            db.OrgUnits.Add(college);
            var d = 0;
            foreach (var (dc, dn) in departments)
                db.OrgUnits.Add(new OrgUnit { Code = dc, NameAr = dn, Kind = OrgUnitKind.Department, ParentId = college.Id, Path = OrgUnit.PathOf(college.Path, dc), SortOrder = ++d, CreatedAt = now });
        }
        foreach (var (code, name, kind) in DemoCentralUnits)
        {
            order++;
            if (await db.OrgUnits.AnyAsync(u => u.Code == code, ct)) continue;
            db.OrgUnits.Add(new OrgUnit { Code = code, NameAr = name, Kind = kind, ParentId = root.Id, Path = OrgUnit.PathOf(root.Path, code), SortOrder = order, CreatedAt = now });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDemoUsersAsync(bool structure, DateTimeOffset now, CancellationToken ct)
    {
        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new InvalidOperationException("Seed:DemoPassword (>= 12 chars) is required when Seed:Demo is true.");
        var totp = config["Seed:DemoTotpSecret"];
        var roles = await db.Roles.ToDictionaryAsync(r => r.Code, ct);
        foreach (var (email, demoName, nameEn, roleCode, demoScope) in DemoUsers)
        {
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) continue;
            // على الهيكل الرسمي: نطاق الحساب واسمه بالجهة الحقيقية المقابلة للجهة التجريبية.
            var scope = structure ? demoScope.Select(UniversityStructure.MapDemoCode).ToArray() : demoScope;
            var nameAr = structure ? UniversityStructure.MapDemoName(demoName) : demoName;
            var user = new AppUser
            {
                Email = email,
                FullNameAr = nameAr,
                FullNameEn = nameEn,
                PasswordHash = hasher.Hash(password),
                CreatedAt = now,
                MfaEnabled = !string.IsNullOrWhiteSpace(totp),
                MfaSecretProtected = string.IsNullOrWhiteSpace(totp) ? null : protector.Protect(totp),
            };
            db.Users.Add(user);
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = roles[roleCode].Id,
                ScopeValues = [.. scope],
                ValidFrom = now.AddMinutes(-1),
                GrantedAt = now,
                ApprovedAt = now,
                Reason = "حساب تجريبي لبيئة التطوير",
            });
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("Demo accounts are ready (*@{Domain}).", DemoDomain);
    }

    /// <summary>أول مدير أعلى في بيئة جديدة: كلمة مرور مؤقتة تطبع مرة واحدة ويلزم تغييرها، ثم التحقق بخطوتين إلزامي.</summary>
    public async Task<string> CreateSuperAdminAsync(string email, string nameAr, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct)) throw new InvalidOperationException($"{normalized} already exists.");
        var sa = await db.Roles.FirstAsync(r => r.Code == PermissionCatalog.SuperAdminRole, ct);
        var temporary = Application.Security.Tokens.NewTemporaryPassword();
        var now = DateTimeOffset.UtcNow;
        var user = new AppUser
        {
            Email = normalized,
            FullNameAr = nameAr,
            PasswordHash = hasher.Hash(temporary),
            MustChangePassword = true,
            CreatedAt = now,
        };
        db.Users.Add(user);
        // التهيئة من سطر الأوامر على الخادم نفسه هي الاعتماد هنا؛ بعدها يمر كل إسناد للمدير الأعلى باعتماد مدير أعلى آخر.
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = sa.Id, ValidFrom = now, GrantedAt = now, ApprovedAt = now, Reason = "إنشاء المدير الأعلى من سطر الأوامر" });
        await db.SaveChangesAsync(ct);
        return temporary;
    }
}
