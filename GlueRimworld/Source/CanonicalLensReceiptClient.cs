using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GlueRimworld;

/// Transport-only forwarding boundary. Payload ownership stays with
/// functional/server/lens-renderer-receipt and definition://lens-renderer-projection.
public static class CanonicalLensReceiptClient
{
    public const string TemplateKey = "functional/server/lens-renderer-receipt";
    public static async Task<JObject> RequestAsync(HttpClient client, string baseUrl, JObject? input = null)
    {
        var body = new JObject
        {
            ["template"] = TemplateKey,
            ["args"] = input ?? new JObject()
        };
        using var response = await client.PostAsync(
            baseUrl.TrimEnd('/') + "/api/execute",
            new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var envelope = JObject.Parse(await response.Content.ReadAsStringAsync());
        if (envelope["error"] != null)
            throw new HttpRequestException(envelope["error"]!.Value<string>() ?? "lens renderer execution failed");

        var output = envelope["output"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(output))
            throw new HttpRequestException("invalid lens renderer projection receipt: missing output");
        return JObject.Parse(output);
    }
}
