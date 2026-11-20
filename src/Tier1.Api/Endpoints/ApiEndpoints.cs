namespace Tier1.Api.Endpoints;

/// <summary>
/// كل مجموعات مسارات /api في مكان واحد، يستدعيه Program.cs واختبار سياسات المسارات معًا،
/// فلا تضاف مجموعة للخادم وتفلت من فحص «كل مسار يعلن صلاحيته». المجموعة الجديدة تضاف هنا فقط.
/// </summary>
public static class ApiEndpoints
{
    public static RouteGroupBuilder MapApi(this RouteGroupBuilder api)
    {
        api.MapAuth();
        api.MapIam();
        api.MapLogs();
        api.MapSettings();
        api.MapSecurity();
        api.MapTier1();
        api.MapCollection();
        api.MapNotifications();
        return api;
    }
}
