using Tier1.Domain.Access;
using Tier1.Domain.Common;

namespace Tier1.Application.Access;

public sealed record NamedDto(string Key, string Name);

public sealed record CapMeaningDto(string Cap, string Meaning);

public sealed record FeatureDto(string Key, string Module, string Name, IReadOnlyList<CapMeaningDto> Caps, IReadOnlyList<NamedDto> Specials);

public sealed record RoleDefinitionDto(string Code, string Name, string Description, bool IsNew);

public sealed record CatalogDto(IReadOnlyList<NamedDto> Modules, IReadOnlyList<FeatureDto> Features, IReadOnlyList<NamedDto> Scopes, IReadOnlyList<RoleDefinitionDto> DefaultRoles);

public sealed record FeatureAccessDto(IReadOnlyDictionary<string, string> Caps, IReadOnlyList<string> Specials, IReadOnlyList<string> ScopeValues);

/// <summary>الصلاحيات الفعلية بصيغة مختصرة للواجهة: لكل خاصية حرف الإمكانية ← رمز النطاق.</summary>
public sealed record EffectiveDto(bool IsSuperAdmin, IReadOnlyList<string> Roles, IReadOnlyDictionary<string, FeatureAccessDto> Features)
{
    public static EffectiveDto From(EffectiveAccess a) => new(
        a.IsSuperAdmin,
        a.RoleCodes,
        a.Features.ToDictionary(
            kv => kv.Key,
            kv => new FeatureAccessDto(
                kv.Value.Caps.OrderBy(c => c.Key).ToDictionary(c => PermissionCatalog.Letters(c.Key), c => GrantView.ScopeCode(c.Value)),
                [.. kv.Value.Specials.Order(StringComparer.Ordinal)],
                [.. kv.Value.ScopeValues.Order(StringComparer.Ordinal)]),
            StringComparer.Ordinal));
}

public sealed record PersonDto(Guid Id, string Name);

public sealed record RoleSummaryDto(Guid Id, string Code, string Name, string? NameEn, string? Description, bool IsSystem, bool IsLocked, int GrantsVersion, int Holders, Guid? PendingChangeId);

public sealed record RoleDetailDto(RoleSummaryDto Role, IReadOnlyDictionary<string, GrantView> Grants);

public sealed record RbacChangeDto(
    Guid Id,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    PersonDto RequestedBy,
    DateTimeOffset RequestedAt,
    string Reason,
    int BaseVersion,
    RbacChangeStatus Status,
    PersonDto? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    IReadOnlyList<GrantDiff> Diff,
    bool CanDecide,
    bool CanCancel);

public sealed record ProposeChangeRequest(int BaseVersion, string Reason, IReadOnlyDictionary<string, GrantView> Grants);

public sealed record CreateRoleRequest(string Code, string NameAr, string? NameEn, string? DescriptionAr, Guid? CopyFromRoleId);

public sealed record DecisionRequest(string? Note);

public sealed record ReasonRequest(string Reason);

public static class ScopeNames
{
    public static IReadOnlyList<NamedDto> All() =>
    [
        new("A", Lang.Pick("الكل", "All")),
        new("U", Lang.Pick("نطاقه", "Their unit")),
        new("S", Lang.Pick("المسند إليه", "Assigned to them")),
        new("O", Lang.Pick("سجلاته", "Their own")),
    ];
}
