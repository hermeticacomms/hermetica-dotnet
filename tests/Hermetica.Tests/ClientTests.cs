using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Hermetica.Tests
{
    internal sealed class SeenRequest
    {
        public SeenRequest(HttpRequestMessage request, string? body, string? contentType)
        {
            Method = request.Method.Method;
            Uri = request.RequestUri!;
            Body = body;
            ContentType = contentType;
            foreach (var h in request.Headers) Headers[h.Key] = string.Join(",", h.Value);
        }

        public string Method { get; }
        public Uri Uri { get; }
        public string? Body { get; }
        public string? ContentType { get; }
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
    }

    /// <summary>Records each request and answers from a queue instead of the network.</summary>
    internal sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<KeyValuePair<int, string>> _answers = new Queue<KeyValuePair<int, string>>();
        public List<SeenRequest> Seen { get; } = new List<SeenRequest>();

        public FakeHandler Answer(int status, string body)
        {
            _answers.Enqueue(new KeyValuePair<int, string>(status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            Seen.Add(new SeenRequest(request, body, request.Content?.Headers.ContentType?.MediaType));
            var answer = _answers.Dequeue();
            return new HttpResponseMessage((HttpStatusCode)answer.Key) { Content = new StringContent(answer.Value) };
        }
    }

    public class ClientTests
    {
        private static Client NewClient(FakeHandler h, string? workspaceId = null) =>
            new Client("hm_sk_test", "https://api.example.com/", workspaceId, new HttpClient(h));

        [Fact]
        public async Task GetSendsAuthQueryAndNoIdempotencyKey()
        {
            var h = new FakeHandler().Answer(200, "{\"items\":[]}");
            var res = await NewClient(h, "ws_1").RequestAsync("GET", "/v1/things",
                new Params { Query = { ["limit"] = 5, ["skip"] = null, ["tag"] = new[] { "a", "b" }, ["on"] = true } });
            Assert.Equal(0, res!.Value.GetProperty("items").GetArrayLength());
            var s = h.Seen[0];
            Assert.Equal("GET", s.Method);
            Assert.Equal("https://api.example.com/v1/things?limit=5&tag=a&tag=b&on=true", s.Uri.AbsoluteUri);
            Assert.Equal("Bearer hm_sk_test", s.Header("Authorization"));
            Assert.Equal("ws_1", s.Header("X-Workspace-Id"));
            Assert.Null(s.Header("Idempotency-Key"));
            Assert.Null(s.Body);
        }

        [Fact]
        public async Task PostSendsJsonBodyAndIdempotencyKey()
        {
            var h = new FakeHandler().Answer(201, "").Answer(201, "");
            var hm = NewClient(h);
            Assert.Null(await hm.RequestAsync("POST", "/v1/things/{id}",
                new Params { Path = { ["id"] = "a b/c" }, Body = new Dictionary<string, object> { ["x"] = 1 } }));
            await hm.RequestAsync("POST", "/v1/things/{id}", new Params { Path = { ["id"] = "1" }, Body = new { }, IdempotencyKey = "key-1" });
            var first = h.Seen[0];
            Assert.Equal("/v1/things/a%20b%2Fc", first.Uri.AbsolutePath);
            Assert.Equal("{\"x\":1}", first.Body);
            Assert.Equal("application/json", first.ContentType);
            Assert.Null(first.Header("X-Workspace-Id"));
            Assert.True(Guid.TryParse(first.Header("Idempotency-Key"), out _));
            Assert.Equal("key-1", h.Seen[1].Header("Idempotency-Key"));
            Assert.Equal("{}", h.Seen[1].Body);
        }

        [Fact]
        public async Task MissingPathParamThrows()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => NewClient(new FakeHandler()).RequestAsync("GET", "/v1/things/{id}"));
        }

        [Fact]
        public async Task ErrorCarriesStatusCodeMessageDetails()
        {
            var h = new FakeHandler().Answer(404, "{\"error\":{\"code\":\"not_found\",\"message\":\"No such thing\",\"details\":{\"id\":\"x\"}}}");
            var e = await Assert.ThrowsAsync<HermeticaException>(() => NewClient(h).RequestAsync("GET", "/v1/things"));
            Assert.Equal(404, e.Status);
            Assert.Equal("not_found", e.Code);
            Assert.Equal("No such thing", e.ErrorMessage);
            Assert.Equal("x", e.Details!.Value.GetProperty("id").GetString());
            Assert.Equal("404 not_found: No such thing", e.Message);
        }

        [Fact]
        public async Task NonJsonErrorFallsBackToHttpError()
        {
            var h = new FakeHandler().Answer(502, "Bad gateway");
            var e = await Assert.ThrowsAsync<HermeticaException>(() => NewClient(h).RequestAsync("DELETE", "/v1/x"));
            Assert.Equal(502, e.Status);
            Assert.Equal("http_error", e.Code);
            Assert.Equal("Bad gateway", e.ErrorMessage);
            Assert.Null(e.Details);
            Assert.NotNull(h.Seen[0].Header("Idempotency-Key"));
        }

        [Fact]
        public async Task PaginateFollowsNextCursor()
        {
            var h = new FakeHandler().Answer(200, "{\"items\":[1,2],\"nextCursor\":\"c2\"}").Answer(200, "{\"items\":[3],\"nextCursor\":null}");
            var hm = NewClient(h);
            var first = new Params { Query = { ["limit"] = 2 } };
            var items = new List<int>();
            await hm.PaginateAsync((p, ct) => hm.RequestAsync("GET", "/v1/p", p, ct), first, item => items.Add(item.GetInt32()));
            Assert.Equal(new[] { 1, 2, 3 }, items);
            Assert.Equal("?limit=2", h.Seen[0].Uri.Query);
            Assert.Equal("?limit=2&cursor=c2", h.Seen[1].Uri.Query);
            Assert.False(first.Query.ContainsKey("cursor"));
        }

        [Fact]
        public async Task GeneratedOperationCallsItsPath()
        {
            var h = new FakeHandler().Answer(200, "{\"ok\":true}");
            var res = await NewClient(h).GetHealthzAsync();
            Assert.True(res!.Value.GetProperty("ok").GetBoolean());
            Assert.Equal("/healthz", h.Seen[0].Uri.AbsolutePath);
        }
    }
}
