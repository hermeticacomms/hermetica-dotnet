#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hermetica
{
    /// <summary>Hermetica API client. <c>new Client("hm_sk_...", "https://api.example.com")</c>. Thread-safe.</summary>
    public partial class Client
    {
        private static readonly Regex PathParam = new Regex(@"\{(\w+)\}");
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string? _workspaceId;
        private readonly HttpClient _http;

        /// <param name="apiKey">API key, sent as a Bearer token.</param>
        /// <param name="baseUrl">API origin, e.g. https://api.example.com.</param>
        /// <param name="workspaceId">Sent as X-Workspace-Id when set.</param>
        /// <param name="http">The HttpClient to use; a default one with a 30 s timeout when null.</param>
        public Client(string apiKey, string baseUrl = "http://localhost:4000", string? workspaceId = null, HttpClient? http = null)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _baseUrl = (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).TrimEnd('/');
            _workspaceId = workspaceId;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        /// <summary>Runs one operation and returns the parsed JSON answer, or null for an empty body.</summary>
        public async Task<JsonElement?> RequestAsync(string method, string path, Params? p = null, CancellationToken ct = default)
        {
            var prm = p ?? new Params();
            var resolved = PathParam.Replace(path, m =>
            {
                if (!prm.Path.TryGetValue(m.Groups[1].Value, out var v)) throw new ArgumentException("missing path param " + m.Groups[1].Value);
                return Uri.EscapeDataString(Format(v));
            });
            var q = new List<string>();
            foreach (var kv in prm.Query)
            {
                if (kv.Value == null) continue;
                IEnumerable values = kv.Value is IEnumerable e && !(kv.Value is string) ? e : new[] { kv.Value };
                foreach (var x in values)
                {
                    if (x != null) q.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(Format(x)));
                }
            }
            using var req = new HttpRequestMessage(new HttpMethod(method), _baseUrl + resolved + (q.Count > 0 ? "?" + string.Join("&", q) : ""));
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (!string.IsNullOrEmpty(_workspaceId)) req.Headers.TryAddWithoutValidation("X-Workspace-Id", _workspaceId);
            if (prm.Body != null) req.Content = new StringContent(JsonSerializer.Serialize(prm.Body, prm.Body.GetType()), Encoding.UTF8, "application/json");
            if (method != "GET")
            {
                // a retry with the same key never repeats the action
                var key = string.IsNullOrEmpty(prm.IdempotencyKey) ? Guid.NewGuid().ToString() : prm.IdempotencyKey;
                req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            }
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = res.Content == null ? "" : await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var status = (int)res.StatusCode;
            if (status >= 300) throw ErrorFor(status, text);
            if (text.Length == 0) return null;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }

        /// <summary>Calls <paramref name="onItem"/> for every item of a list operation, following nextCursor.</summary>
        /// <param name="list">A generated list method, e.g. <c>hm.GetV1ProfilesAsync</c>.</param>
        /// <param name="p">The first page's params (not modified).</param>
        /// <param name="onItem">Called with each entry of items.</param>
        /// <param name="ct">Cancels the paging.</param>
        public async Task PaginateAsync(Func<Params, CancellationToken, Task<JsonElement?>> list, Params? p, Action<JsonElement> onItem, CancellationToken ct = default)
        {
            var q = (p ?? new Params()).Copy();
            while (true)
            {
                var page = await list(q, ct).ConfigureAwait(false);
                if (page == null || page.Value.ValueKind != JsonValueKind.Object) return;
                if (page.Value.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray()) onItem(item);
                }
                if (!page.Value.TryGetProperty("nextCursor", out var next) || next.ValueKind != JsonValueKind.String) return;
                var cursor = next.GetString();
                if (string.IsNullOrEmpty(cursor)) return;
                q.Query["cursor"] = cursor;
            }
        }

        private static string Format(object? v)
        {
            if (v == null) return "";
            if (v is bool b) return b ? "true" : "false";
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return v.ToString() ?? "";
        }

        private static HermeticaException ErrorFor(int status, string text)
        {
            var code = "http_error";
            var message = text.Length > 200 ? text.Substring(0, 200) : text;
            JsonElement? details = null;
            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                    {
                        if (err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString() ?? code;
                        if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString() ?? message;
                        if (err.TryGetProperty("details", out var d) && d.ValueKind != JsonValueKind.Null) details = d.Clone();
                    }
                }
                catch (JsonException)
                {
                    // not JSON after all: keep the raw text
                }
            }
            return new HermeticaException(status, code, message, details);
        }
    }
}
