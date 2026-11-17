namespace GrayCat.Service;

using GrayCat.Core.Constants;

internal static class ServiceProbe
{
    /// <summary>True when a GrayCat service already answers on <paramref name="baseUrl"/>.</summary>
    public static async Task<bool> IsGrayCatServiceRunningAsync(string baseUrl)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(1) };
            var body = await client.GetStringAsync(AppConstants.ServiceEndpoints.Health);
            return body.Contains("\"status\":\"ok\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
