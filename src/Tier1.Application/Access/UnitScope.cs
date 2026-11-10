using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;

namespace Tier1.Application.Access;

/// <summary>
/// نطاق المستخدم على الهيكل لإمكانية في خاصية: الكل، أو رموز جهات تغطي الجهة وما تحتها.
/// «المسند إليه» و«سجلاته» لا معنى لهما على الجهات، فلا يغطيان شيئًا.
/// </summary>
public sealed record UnitScope(bool All, IReadOnlyCollection<string> Codes)
{
    public static readonly UnitScope Nothing = new(false, []);

    public static UnitScope For(EffectiveAccess access, string feature, Cap cap) => access.ScopeFor(feature, cap) switch
    {
        AccessScope.All => new(true, []),
        AccessScope.Unit => new(false, access.ScopeValues(feature, cap)),
        _ => Nothing,
    };

    public bool Covers(string unitPath) => All || OrgScope.Covers(Codes, unitPath);

    public bool Covers(OrgUnit unit) => Covers(unit.Path);

    /// <summary>الجهة خارج النطاق تعامل كأنها غير موجودة (404)، فلا تتسرب معلومة عن وجودها.</summary>
    public void Require(OrgUnit unit)
    {
        if (!Covers(unit)) throw DomainException.NotFound();
    }
}
