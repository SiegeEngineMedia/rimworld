using System;
using System.Net.Http;
using System.Text;
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

        public GlueBridgeClient(string baseUrl = "http://127.0.0.1:8765")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        /// <summary>Liveness probe against glue-runtime-host's GET /ping route.</summary>
        public bool Ping()
        {
            try
            {
                var resp = _http.GetAsync(_baseUrl + "/ping").GetAwaiter().GetResult();
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Renders a glue template by key against the given input args and returns the parsed
        /// result, or null on failure (see LastError). Synchronous/blocking: this foothold calls
        /// it from a throttled GameComponentTick well off any per-frame render path, which is
        /// acceptable for a small number of colonists at a multi-second cadence -- a full
        /// build-out should move this onto RimWorld's long-event queue instead (NEXT_STEPS.md).
        /// </summary>
        public JObject? Execute(string templateKey, JObject args)
        {
            var payload = new JObject { ["template"] = templateKey, ["args"] = args };
            try
            {
                var content = new StringContent(
                    payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                var resp = _http.PostAsync(_baseUrl + "/api/execute", content).GetAwaiter().GetResult();
                var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var parsed = JObject.Parse(body);

                if (parsed["error"] != null)
                {
                    LastCallSucceeded = false;
                    LastError = parsed["error"]!.Value<string>();
                    return null;
                }

                LastCallSucceeded = true;
                LastError = null;
                return parsed;
            }
            catch (Exception ex)
            {
                LastCallSucceeded = false;
                LastError = ex.Message;
                return null;
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
