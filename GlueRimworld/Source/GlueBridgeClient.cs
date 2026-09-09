using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GlueRimworld
{
    /// <summary>
    /// Thin loopback HTTP client for the glue-runtime-host HTTP transport
    /// (glue monorepo: runtimes/csharp/glue-server/src/Transports/HttpTransport.cs):
    ///   POST /api/execute  { "template": "&lt;template key&gt;", "args": {...} }  -&gt;  rendered JObject
    ///   GET  /ping                                                               -&gt;  { ok: true }
    ///
    /// This is the sanctioned, ALREADY-EXISTING bridge boundary used here instead of an
    /// in-process GlueCore/GlueFp/GlueServer assembly reference (the vintage-actors pattern).
    /// Reason: RimWorld hosts mod assemblies at the netstandard2.1 API level (Unity's Mono
    /// runtime), while glue's runtimes/csharp multi-targets net8.0;net10.0 (modern CoreCLR).
    /// Vintage Story's own server process is itself modern .NET, so vintage-actors can load
    /// GlueCore.dll/GlueFp.dll in-process; RimWorld's host cannot load a net8.0/net10.0
    /// assembly at all, so the same in-process pattern does not port. glue-runtime-host
    /// already runs those assemblies out-of-process and exposes exactly the render call this
    /// bridge needs over HTTP -- reused as-is, not reinvented.
    /// </summary>
    public sealed class GlueBridgeClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public bool LastCallSucceeded { get; private set; }
        public string? LastError { get; private set; }

        public GlueBridgeClient(string? baseUrl = null)
        {
            var configuredUrl = baseUrl
                ?? Environment.GetEnvironmentVariable("GLUE_RIMWORLD_HOST_URL")
                ?? "http://127.0.0.1:8765";
            _baseUrl = configuredUrl.TrimEnd('/');
            // The first request can include cold template discovery/validation on the
            // out-of-process host. Keep the hook asynchronous, but allow that legitimate
            // cold-start work to finish before the game-side fail-closed path records a
            // transport failure.
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        public string BaseUrl => _baseUrl;

        /// <summary>Liveness probe against glue-runtime-host's GET /ping route.</summary>
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var resp = await _http.GetAsync(_baseUrl + "/ping", cancellationToken).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Renders a glue template by key without blocking the RimWorld simulation thread.
        /// The caller owns completion scheduling and must only consume the result on the game
        /// thread. The response carries its own error so concurrent pawn requests cannot race
        /// through the shared LastError diagnostic properties.
        /// </summary>
        public async Task<GlueBridgeResult> ExecuteAsync(
            string templateKey,
            JObject args,
            CancellationToken cancellationToken = default)
        {
            var payload = new JObject { ["template"] = templateKey, ["args"] = args };
            try
            {
                using var content = new StringContent(
                    payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(_baseUrl + "/api/execute", content, cancellationToken).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parsed = JObject.Parse(body);

                if (parsed["error"] != null)
                {
                    LastCallSucceeded = false;
                    var error = parsed["error"]!.Value<string>() ?? "remote execution error";
                    LastError = error;
                    return GlueBridgeResult.Fail(error);
                }

                LastCallSucceeded = true;
                LastError = null;

                // /api/execute returns an envelope. The C# host's wildcard
                // projection is serialized as the envelope's JSON string so
                // text transports and the existing lens client remain
                // compatible; the game-side observation boundary consumes the
                // projected binding object itself.
                var outputText = parsed["output"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(outputText))
                {
                    var projected = JObject.Parse(outputText);
                    return GlueBridgeResult.Success(projected);
                }

                return GlueBridgeResult.Success(parsed);
            }
            catch (Exception ex)
            {
                LastCallSucceeded = false;
                LastError = ex.Message;
                return GlueBridgeResult.Fail(ex.Message);
            }
        }

        public void Dispose() => _http.Dispose();
    }

    public sealed class GlueBridgeResult
    {
        private GlueBridgeResult(JObject? payload, string? error)
        {
            Payload = payload;
            Error = error;
        }

        public JObject? Payload { get; }
        public string? Error { get; }
        public bool Succeeded => Payload != null && Error == null;

        public static GlueBridgeResult Success(JObject payload) => new GlueBridgeResult(payload, null);
        public static GlueBridgeResult Fail(string error) => new GlueBridgeResult(null, error);
    }
}
