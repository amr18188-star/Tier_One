using Tier1.Api.Endpoints;
using Tier1.Api.Security;
using Tier1.Api.Startup;
using Tier1.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tier1.Tests;

/// <summary>
/// كل مسار /api يعلن صلاحيته صراحة: مفتوح عن قصد، أو سياسة جلسة، أو بند من المصفوفة يوجد في الكتالوج.
/// يمنع إضافة شاشة أو عملية بلا حماية.
/// </summary>
public class EndpointPolicyTests
{
    private static readonly HashSet<string> OpenOnPurpose = new(StringComparer.Ordinal)
    {
        "GET /api/auth/csrf", "POST /api/auth/login",
    };

    private static IReadOnlyList<RouteEndpoint> Endpoints()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Tier1"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none",
            ["ConnectionStrings:Mongo"] = "mongodb://127.0.0.1:1",
            ["BackgroundJobs:Enabled"] = "false",
        });
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddApi(builder.Configuration);
        var app = builder.Build();
        var api = app.MapGroup("/api");
        api.MapAuth();
        api.MapIam();
        api.MapLogs();
        api.MapSettings();
        api.MapTier1();
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
    }

    private static string Name(RouteEndpoint e) =>
        $"{e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault()} {e.RoutePattern.RawText}".Replace("//", "/", StringComparison.Ordinal).TrimEnd('/');

    [Fact]
    public void Every_api_endpoint_declares_its_access()
    {
        var endpoints = Endpoints();
        Assert.True(endpoints.Count > 30, $"only {endpoints.Count} endpoints found");
        foreach (var e in endpoints)
        {
            var name = Name(e);
            if (e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                Assert.Contains(name, OpenOnPurpose);
                continue;
            }
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Where(p => p is not null).ToList();
            Assert.True(policies.Count > 0, $"{name} has no explicit policy");
        }
    }

    /// <summary>الاعتماد وحسم التعارضات بإجراء خاص للجنة العليا، لا بصلاحية التعديل.</summary>
    [Theory]
    [InlineData("POST /api/kpi/indicators/{code}/approve", "perm:kpi.dictionary:approve")]
    [InlineData("POST /api/kpi/indicators/{code}/return", "perm:kpi.dictionary:approve")]
    [InlineData("POST /api/kpi/targets/{id:guid}/approve", "perm:kpi.targets:approve")]
    [InlineData("POST /api/kpi/conflicts/{code}/decide", "perm:kpi.conflicts:decide")]
    public void Committee_actions_require_their_special_action(string route, string expected)
    {
        var endpoint = Endpoints().Single(e => Name(e) == route);
        var policy = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Single(p => p!.StartsWith("perm:", StringComparison.Ordinal));
        Assert.Equal(expected, policy);
    }

    [Fact]
    public async Task Every_permission_policy_resolves_against_the_catalog()
    {
        var endpoints = Endpoints();
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
        var names = endpoints.SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>()).Select(a => a.Policy)
            .Where(p => p?.StartsWith(PermissionPolicyProvider.Prefix, StringComparison.Ordinal) == true).Distinct().ToList();
        Assert.NotEmpty(names);
        foreach (var name in names)
            Assert.NotNull(await provider.GetPolicyAsync(name!));
    }

    [Fact]
    public void Matrix_approval_requires_the_special_action_not_edit()
    {
        var approve = Endpoints().Single(e => Name(e) == "POST /api/iam/changes/{id:guid}/approve");
        var policy = approve.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Single(p => p!.StartsWith("perm:", StringComparison.Ordinal));
        Assert.Equal("perm:adm.roles:approve", policy);
    }
}
