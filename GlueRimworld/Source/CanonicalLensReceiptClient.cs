using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GlueRimworld;

/// Transport-only forwarding boundary. Payload ownership stays with definition://lens-renderer-projection.
public static class CanonicalLensReceiptClient
{
    public const string TemplateKey = "functional/server/lens-renderer-receipt";
    public static async Task<JsonElement> RequestAsync(HttpClient client, string baseUrl, object? input = null)
    {
        var body = JsonSerializer.Serialize(new { templateKey = TemplateKey, input = input ?? new { } });
        using var response = await client.PostAsync(baseUrl.TrimEnd('/') + "/api/execute", new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            throw new HttpRequestException("invalid lens renderer projection receipt");
        return result.Clone();
    }
}
