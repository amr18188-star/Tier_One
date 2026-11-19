using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Collection;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;
using Tier1.Domain.Organization;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// بذر البيانات الأساسية: أدوار الكتالوج بمنحها الافتراضية مرة واحدة (تعديلات المصفوفة اللاحقة لا تُستبدل)،
/// والبيانات المرجعية لـ Tier 1 على قاعدة فارغة، وفي التطوير فقط كليات وحسابات تجريبية لكل دور.
/// </summary>
public sealed class Seeder(Tier1DbContext db, IPasswordHasher hasher, ISecretProtector protector, IConfiguration config, ILogger<Seeder> log, IAuditLog audit)
{
    public const string DemoDomain = "tier1.local";
    private const string FlowSeedKey = "seed:flow";
    private const string LegacyFlowCollegeKey = "seed:flow:college-indicators";

    private static readonly HashSet<string> FlowUsers = new(StringComparer.Ordinal)
    {
        "sa1", "sa2", "president", "planner", "committee", "quality", "dean", "college", "deptrep", "entry", "entry.uni", "tier1",
        "rep.ele", "rep.mec", "entry.cs", "college.cs", "dean.cs", "rep.se", "rep.ai",
    };

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
        var flow = config.GetValue<bool>("Seed:Flow");
        var flowSeeded = await db.Settings.AnyAsync(s => s.Key == FlowSeedKey, ct);
        if (flow != flowSeeded && (await db.OrgUnits.AnyAsync(ct) || flow && await db.Users.AnyAsync(ct)))
            throw new InvalidOperationException("Keep Seed:Flow on its own database. Start the flow profile with an empty database.");
        if (flow)
        {
            await SeedRolesAsync(now, ct);
            await SeedFlowAsync(flowSeeded, now, ct);
            return;
        }
        await SeedRolesAsync(now, ct);
        var structure = await UniversityStructure.LoadedAsync(db, ct);
        if (!structure && config.GetValue<bool>("Seed:Structure") && !await db.OrgUnits.AnyAsync(ct))
        {
            // بذر الإطار والهيكل الرسمي معًا على قاعدة جديدة، حتى لو كانت الحسابات التجريبية معطلة.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await ReferenceSeed.ApplyAsync(db, now, log, ct);
            await UniversityStructure.ApplyAsync(db, now, ct);
            await transaction.CommitAsync(ct);
            structure = true;
            log.LogInformation("Loaded the official university structure.");
        }
        else
        {
            await ReferenceSeed.ApplyAsync(db, now, log, ct);
        }
        if (config.GetValue<bool>("Seed:Demo"))
        {
            if (!structure) await SeedDemoCollegesAsync(now, ct);
            await SeedDemoUsersAsync(structure, now, ct);
        }
    }

    private async Task SeedFlowAsync(bool seeded, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Settings.AnyAsync(s => s.Key == LegacyFlowCollegeKey, ct))
            throw new InvalidOperationException("Start the two-college Flow profile with an empty database. The current database uses the previous college-only definitions.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!seeded)
        {
            await ReferenceSeed.ApplyAsync(db, now, log, ct);
            await SeedDemoCollegesAsync(now, ct, flow: true);
            db.Settings.Add(new SystemSetting { Key = FlowSeedKey, Value = "1", UpdatedAt = now });
        }
        await SeedDemoUsersAsync(false, now, ct, flow: true);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
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
        if (existing.Count == 0)
        {
            // قاعدة جديدة: الأدوار أنشئت الآن من الكتالوج الحالي، وفيه كل الإصلاحات، فتسجل خط أساس لا تطبيقًا.
            await MarkDataFixesAsBaselineAsync(now, ct);
            return;
        }
        // D10: الإصلاحات تغير بيانات قائمة (صلاحيات الأدوار)، فلا تطبق تلقائيًا إلا في التطوير.
        // في الإنتاج: --data-fixes يعرض المعلق، و--apply-data-fixes --confirm --operator يطبقه بموافقة ويسجله.
        if (config.GetValue<bool>("Seed:AutoDataFixes"))
            await ApplyDataFixesAsync("تطبيق تلقائي في التطوير (Seed:AutoDataFixes)", now, ct);
        else if ((await PendingDataFixesAsync(ct)).Count is > 0 and var pending)
            log.LogWarning("{Count} data fix(es) await approval. List them with --data-fixes, apply with --apply-data-fixes --confirm --operator \"<name>\".", pending);
    }

    /// <summary>إصلاح بيانات لمرة واحدة: مفتاحه في الإعدادات، ووصف ما يغيره.</summary>
    public sealed record DataFixInfo(string Key, string DescriptionAr, string DescriptionEn);

    /// <summary>إصلاح وتطبيقه: يغير الكيانات ويعيد عدد ما تغير، والحفظ على المطبق.</summary>
    private sealed record DataFix(string Key, string DescriptionAr, string DescriptionEn, Func<CancellationToken, Task<int>> ApplyAsync)
    {
        public DataFixInfo Info => new(Key, DescriptionAr, DescriptionEn);
    }

    /// <summary>
    /// سجل إصلاحات البيانات (D10، ADR-0010)، بالترتيب الذي تطبق به. المفاتيح كما كانت، فما طبق قبلًا لا يعاد.
    /// كل إصلاح يغير صلاحيات أدوار الكتالوج القائمة مرة واحدة؛ ما يغيره المدير بعدها من المصفوفة يبقى.
    /// </summary>
    private IEnumerable<DataFix> AllDataFixes()
    {
        foreach (var f in PermissionCatalog.Features)
            yield return new($"catalog:{f.Key}", $"منح الخاصية «{f.Name.Ar}» لأدوار الكتالوج القائمة بحسب منحها الافتراضية",
                $"Grant '{f.Name.En}' to the existing catalog roles by its defaults", ct => GrantFeatureToExistingRolesAsync(f, ct));
        yield return new("catalog-fix:org.units-unit-scope", "تقييد دليل الجهات في الأدوار المرتبطة بجهة (VP، DEAN، CTC، DREP، DE، VIEW) بنطاقها",
            "Limit the unit directory of unit-bound roles to their own units", ScopeUnitDirectoryAsync);
        yield return new("catalog-fix:dash.home-all-roles", "منح الصفحة الرئيسية لمسئول ملف Tier 1 ومسؤول النظام",
            "Grant the home page to the Tier 1 officer and the system administrator", GrantHomeToAllRolesAsync);
        yield return new($"catalog:{Features.Forms}:{ApprovalRules.ApproveDepartmentDirectly}", "منح لجنة الكلية الاعتماد المباشر لنموذج القسم (D2)",
            "Grant the college committee direct approval of department forms (D2)",
            ct => AddSpecialAsync(Features.Forms, "CTC", ApprovalRules.ReviewCollege, ApprovalRules.ApproveDepartmentDirectly, ct));
        yield return new($"catalog:{Features.Forms}:{ApprovalRules.ApproveAsDeanDirectly}", "منح العميد الاعتماد المباشر وإحالة النموذج للجودة المركزية (D2)",
            "Grant the dean direct approval to central quality (D2)",
            ct => AddSpecialAsync(Features.Forms, "DEAN", ApprovalRules.ApproveCollege, ApprovalRules.ApproveAsDeanDirectly, ct));
        yield return new($"catalog:{Features.Cycles}:{ApprovalRules.ApproveReopen}", "منح اللجنة العليا اعتماد طلبات إعادة فتح النماذج المعتمدة (D4)",
            "Grant the higher committee approval of reopen requests (D4)",
            ct => AddSpecialAsync(Features.Cycles, "HC", null, ApprovalRules.ApproveReopen, ct));
    }

    /// <summary>الإصلاحات التي لم تطبق بعد على هذه القاعدة.</summary>
    public async Task<IReadOnlyList<DataFixInfo>> PendingDataFixesAsync(CancellationToken ct)
    {
        var done = (await db.Settings.AsNoTracking().Where(s => s.Key.StartsWith("catalog")).Select(s => s.Key).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        return [.. AllDataFixes().Where(f => !done.Contains(f.Key)).Select(f => f.Info)];
    }

    /// <summary>
    /// يطبق الإصلاحات المعلقة بالترتيب، كل إصلاح في معاملته: التغيير، ومفتاحه، وقيد في سجل التدقيق باسم من وافق وعدد ما تغير.
    /// يعيد ما طبق وكم تغير في كل منها. إعادة التشغيل لا تعيد ما طبق.
    /// </summary>
    public async Task<IReadOnlyList<(DataFixInfo Fix, int Changed)>> ApplyDataFixesAsync(string approvedBy, DateTimeOffset now, CancellationToken ct)
    {
        var pending = (await PendingDataFixesAsync(ct)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var applied = new List<(DataFixInfo, int)>();
        foreach (var fix in AllDataFixes().Where(f => pending.Contains(f.Key)))
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var changed = await fix.ApplyAsync(ct);
            db.Settings.Add(new SystemSetting { Key = fix.Key, Value = "1", UpdatedAt = now });
            audit.Record(AuditEvents.Audit("adm", Features.Roles, "seed.data_fix_applied", "تطبيق إصلاح بيانات معتمد", "Applied an approved data fix") with
            {
                ActorName = approvedBy,
                Target = new AuditTarget("data_fix", fix.Key, fix.DescriptionAr),
                Details = new { changed, description = fix.DescriptionEn },
            });
            await db.SaveChangesAsync(ct);
            if (changed > 0)
                await db.AccessStates.Where(s => s.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), ct);
            await tx.CommitAsync(ct);
            applied.Add((fix.Info, changed));
            log.LogInformation("Applied data fix {Key} ({Changed} change(s)), approved by {By}.", fix.Key, changed, approvedBy);
        }
        return applied;
    }

    /// <summary>قاعدة جديدة: الأدوار من الكتالوج الحالي، فكل الإصلاحات متضمنة؛ تسجل مفاتيحها «خط أساس» دون تغيير.</summary>
    private async Task MarkDataFixesAsBaselineAsync(DateTimeOffset now, CancellationToken ct)
    {
        foreach (var fix in await PendingDataFixesAsync(ct))
            db.Settings.Add(new SystemSetting { Key = fix.Key, Value = "baseline", UpdatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>إجراء خاص جديد في الكتالوج لدور قائم، وللمدير الأعلى. إن أعطي existingSpecial فلمن يملك تلك المراجعة في الخاصية فقط.</summary>
    private async Task<int> AddSpecialAsync(string feature, string roleCode, string? existingSpecial, string newSpecial, CancellationToken ct)
    {
        var grants = await (from g in db.RoleGrants
                            join r in db.Roles on g.RoleId equals r.Id
                            where r.IsSystem && g.Feature == feature
                               && (r.Code == PermissionCatalog.SuperAdminRole || r.Code == roleCode && (existingSpecial == null || g.Specials.Contains(existingSpecial)))
                            select g).ToListAsync(ct);
        var changed = 0;
        foreach (var grant in grants.Where(g => !g.Specials.Contains(newSpecial)))
        {
            grant.Specials.Add(newSpecial);
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// قرار الجامعة (نوفمبر 2026): مستخدم الكلية أو الإدارة لا يرى إلا جهته، ومنها دليل الجهات نفسه.
    /// تحول منح «الجهات» في أدوار الكتالوج المقيدة بجهة من «الكل» إلى «نطاقه».
    /// </summary>
    private async Task<int> ScopeUnitDirectoryAsync(CancellationToken ct)
    {
        string[] unitRoles = ["VP", "DEAN", "CTC", "DREP", "DE", "VIEW"];
        var grants = await (from g in db.RoleGrants
                            join r in db.Roles on g.RoleId equals r.Id
                            where r.IsSystem && unitRoles.Contains(r.Code) && g.Feature == Features.Units && g.Scope == AccessScope.All
                            select g).ToListAsync(ct);
        foreach (var g in grants) g.Scope = AccessScope.Unit;
        return grants.Count;
    }

    /// <summary>
    /// الصفحة الرئيسية نقطة دخول كل مستخدم، ومهامها تُرشَّح بصلاحياته هو، فتمنح لكل الأدوار.
    /// كان مسؤول ملف Tier 1 ومسؤول النظام بلا «الصفحة الرئيسية» فتفتح لهما صفحة «لا تملك صلاحية».
    /// </summary>
    private async Task<int> GrantHomeToAllRolesAsync(CancellationToken ct)
    {
        string[] roleCodes = ["T1O", "SYS"];
        var roles = await db.Roles.Include(r => r.Grants).Where(r => r.IsSystem && roleCodes.Contains(r.Code)).ToListAsync(ct);
        var added = 0;
        foreach (var role in roles.Where(r => r.Grants.All(g => g.Feature != Features.Dashboard)))
        {
            db.RoleGrants.Add(new RoleGrant { RoleId = role.Id, Feature = Features.Dashboard, Caps = Cap.View, Scope = AccessScope.All, Specials = [] });
            added++;
        }
        return added;
    }

    /// <summary>خاصية أضيفت إلى الكتالوج في إصدار لاحق تأخذ منحها الافتراضية في أدوار الكتالوج القائمة التي لا تملكها.</summary>
    private async Task<int> GrantFeatureToExistingRolesAsync(FeatureDefinition f, CancellationToken ct)
    {
        var roles = await db.Roles.Include(r => r.Grants).Where(r => r.IsSystem).ToListAsync(ct);
        var added = 0;
        foreach (var role in roles.Where(r => r.Grants.All(g => g.Feature != f.Key)))
        {
            if (role.Code == PermissionCatalog.SuperAdminRole)
            {
                db.RoleGrants.Add(new RoleGrant { RoleId = role.Id, Feature = f.Key, Caps = f.AllowedCaps, Scope = AccessScope.All, Specials = [.. f.Specials.Select(s => s.Key)] });
                added++;
                continue;
            }
            var caps = f.Defaults.TryGetValue(role.Code, out var d) ? d.Caps : Cap.None;
            var specials = f.Specials.Where(s => s.DefaultRoles.Contains(role.Code)).Select(s => s.Key).ToList();
            if (caps == Cap.None && specials.Count == 0) continue;
            db.RoleGrants.Add(new RoleGrant { RoleId = role.Id, Feature = f.Key, Caps = caps, Scope = d?.Scope ?? AccessScope.All, Specials = specials });
            added++;
        }
        return added;
    }

    private async Task SeedDemoCollegesAsync(DateTimeOffset now, CancellationToken ct, bool flow = false)
    {
        var root = await db.OrgUnits.FirstAsync(u => u.Kind == OrgUnitKind.University, ct);
        var order = 0;
        foreach (var (code, name, departments) in DemoColleges)
        {
            if (flow && code is not ("ENG" or "CS")) continue;
            order++;
            if (await db.OrgUnits.AnyAsync(u => u.Code == code, ct)) continue;
            var college = new OrgUnit { Code = code, NameAr = name, Kind = OrgUnitKind.College, ParentId = root.Id, Path = OrgUnit.PathOf(root.Path, code), SortOrder = order, CreatedAt = now };
            db.OrgUnits.Add(college);
            var d = 0;
            foreach (var (dc, dn) in departments)
                db.OrgUnits.Add(new OrgUnit { Code = dc, NameAr = dn, Kind = OrgUnitKind.Department, ParentId = college.Id, Path = OrgUnit.PathOf(college.Path, dc), SortOrder = ++d, CreatedAt = now });
        }
        foreach (var (code, name, kind) in flow ? [] : DemoCentralUnits)
        {
            order++;
            if (await db.OrgUnits.AnyAsync(u => u.Code == code, ct)) continue;
            db.OrgUnits.Add(new OrgUnit { Code = code, NameAr = name, Kind = kind, ParentId = root.Id, Path = OrgUnit.PathOf(root.Path, code), SortOrder = order, CreatedAt = now });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDemoUsersAsync(bool structure, DateTimeOffset now, CancellationToken ct, bool flow = false)
    {
        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new InvalidOperationException("Seed:DemoPassword (>= 12 chars) is required for demo accounts.");
        var totp = config["Seed:DemoTotpSecret"];
        var roles = await db.Roles.ToDictionaryAsync(r => r.Code, ct);
        foreach (var (email, demoName, nameEn, roleCode, demoScope) in DemoUsers)
        {
            if (flow && !FlowUsers.Contains(email.Split('@', 2)[0])) continue;
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
