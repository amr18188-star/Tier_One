using System.Reflection;
using Tier1.Application.Abstractions;

namespace Tier1.Tests;

/// <summary>
/// BE-14: module boundaries in the data-access ports. Each module writes through its own store and reads others through
/// <see cref="IReadModel"/>. The shared <see cref="IAppDb"/> is the migration path: the list of services still on it may only shrink.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Application = typeof(IAppDb).Assembly;

    /// <summary>Services not yet moved to module stores. Remove a name when its service moves; never add one.</summary>
    private static readonly HashSet<string> StillOnSharedDb =
    [
        "AccessService", "AuditLog", "ConflictsService", "DictionaryService", "HomeService", "IamPolicy", "LogsService", "MeService",
        "OrgUnitsService", "RbacAdminService", "RoleAssignmentService", "SessionService", "SignInService", "StrategyService",
        "StructureService", "TargetsService", "UnitIndicatorsService", "UserAccountsService", "UserOverridesService", "UsersAdminService",
    ];

    /// <summary>Each module store, and the namespace of the only module that may write through it.</summary>
    private static readonly Dictionary<Type, string> Owners = new()
    {
        [typeof(INotificationStore)] = "Tier1.Application.Notifications",
        [typeof(ISettingsStore)] = "Tier1.Application.Settings",
        [typeof(IEventStore)] = "Tier1.Application.Integration",
        [typeof(IScorecardStore)] = "Tier1.Application.Scorecard",
        [typeof(ICollectionStore)] = "Tier1.Application.Collection",
    };

    private static IEnumerable<(Type Type, Type Parameter)> ConstructorDependencies() =>
        Application.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters()).Select(p => (t, p.ParameterType)))
            .Distinct();

    [Fact]
    public void Application_does_not_reference_the_database_or_log_drivers()
    {
        var drivers = Application.GetReferencedAssemblies().Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("Npgsql", StringComparison.Ordinal) || n.StartsWith("MongoDB", StringComparison.Ordinal)).ToList();
        Assert.True(drivers.Count == 0, $"Application references {string.Join(", ", drivers)}; put that code behind a port in Infrastructure.");
    }

    [Fact]
    public void Services_on_the_shared_database_port_only_shrink()
    {
        var onShared = ConstructorDependencies().Where(d => d.Parameter == typeof(IAppDb)).Select(d => d.Type.Name).ToHashSet();
        var added = onShared.Except(StillOnSharedDb).Order(StringComparer.Ordinal).ToList();
        Assert.True(added.Count == 0,
            $"New services on IAppDb: {string.Join(", ", added)}. Write through the module's store and read other modules through IReadModel.");
        var moved = StillOnSharedDb.Except(onShared).Order(StringComparer.Ordinal).ToList();
        Assert.True(moved.Count == 0, $"No longer on IAppDb, so remove them from the list: {string.Join(", ", moved)}.");
    }

    [Fact]
    public void A_module_store_is_used_only_by_its_own_module()
    {
        var foreign = ConstructorDependencies()
            .Where(d => Owners.TryGetValue(d.Parameter, out var owner) && !(d.Type.Namespace ?? "").StartsWith(owner, StringComparison.Ordinal))
            .Select(d => $"{d.Type.FullName} uses {d.Parameter.Name}").ToList();
        Assert.True(foreign.Count == 0, "A module writes only its own tables:\n" + string.Join("\n", foreign));
    }
}
