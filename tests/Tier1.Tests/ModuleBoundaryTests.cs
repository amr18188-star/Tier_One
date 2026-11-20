using System.Reflection;
using Tier1.Application.Abstractions;

namespace Tier1.Tests;

/// <summary>
/// BE-14: module boundaries in the data-access ports. Each module writes only its own tables through its store, and reads other
/// modules through <see cref="IReadModel"/> (untracked, so what it reads cannot be saved). There is no port to the whole database.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Application = typeof(IUnitOfWork).Assembly;

    /// <summary>Each module store, and the namespaces of the module that may write through it.</summary>
    private static readonly Dictionary<Type, string[]> Owners = new()
    {
        [typeof(INotificationStore)] = ["Tier1.Application.Notifications"],
        [typeof(ISettingsStore)] = ["Tier1.Application.Settings"],
        [typeof(IEventStore)] = ["Tier1.Application.Integration"],
        [typeof(IScorecardStore)] = ["Tier1.Application.Scorecard"],
        [typeof(ICollectionStore)] = ["Tier1.Application.Collection"],
        [typeof(IKpiStore)] = ["Tier1.Application.Kpi"],
        [typeof(IOrgStore)] = ["Tier1.Application.Organization"],
        [typeof(IStructureStore)] = ["Tier1.Application.Structure"],
        [typeof(IIdentityStore)] = ["Tier1.Application.Access", "Tier1.Application.Accounts"],
        [typeof(IAuditStore)] = ["Tier1.Application.Auditing", "Tier1.Application.Logs"],
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
    public void Every_module_store_has_an_owner_and_the_whole_database_has_no_port()
    {
        var stores = Application.GetTypes().Where(t => t.IsInterface && t != typeof(IUnitOfWork) && typeof(IUnitOfWork).IsAssignableFrom(t)).ToList();
        Assert.Empty(stores.Except(Owners.Keys).Select(t => t.Name));
        Assert.Null(Application.GetType("Tier1.Application.Abstractions.IAppDb"));
    }

    [Fact]
    public void A_module_store_is_used_only_by_its_own_module()
    {
        var foreign = ConstructorDependencies()
            .Where(d => Owners.TryGetValue(d.Parameter, out var owners) && !owners.Any(o => (d.Type.Namespace ?? "").StartsWith(o, StringComparison.Ordinal)))
            .Select(d => $"{d.Type.FullName} uses {d.Parameter.Name}").ToList();
        Assert.True(foreign.Count == 0, "A module writes only its own tables:\n" + string.Join("\n", foreign));
    }

    [Fact]
    public void A_service_writes_through_one_module_store()
    {
        var many = ConstructorDependencies().Where(d => Owners.ContainsKey(d.Parameter)).GroupBy(d => d.Type)
            .Where(g => g.Select(d => d.Parameter).Distinct().Count() > 1)
            .Select(g => $"{g.Key.FullName}: {string.Join(", ", g.Select(d => d.Parameter.Name))}").ToList();
        Assert.True(many.Count == 0, "A service writing two modules' tables belongs to neither; split it or raise an event:\n" + string.Join("\n", many));
    }
}
