namespace TodayChecklist.ViewModels;

internal static class JsonDefaults
{
    public static JsonSerializerOptions Options => Services.JsonDefaults.Create();
}
