# Hermetica .NET client

Generated from the Hermetica OpenAPI spec (2524 operations). netstandard2.0 and net10.0; System.Net.Http and System.Text.Json only.

## Install

```sh
dotnet add package Hermeticacomms.Hermetica
```

## Quick start

```csharp
using Hermetica;

var hm = new Client("hm_sk_...", "https://api.example.com");
var page = await hm.GetV1ProfilesAsync(new Params { Query = { ["limit"] = 25 } });
await hm.PostV1EventsAsync(new Params
{
    Body = new { name = "signed_up", profile = new { email = "ada@example.com" } },
    IdempotencyKey = "signup-ada-1",
});
```

One async method per operationId, PascalCase with an `Async` suffix. Each takes an optional `Params` (`Path`, `Query`, `Body`,
`IdempotencyKey`) and a `CancellationToken`, and returns the answer as a `JsonElement?` (`null` for an empty body). The body is
serialized with System.Text.Json. Pass `workspaceId` to send `X-Workspace-Id`, `http` to bring your own `HttpClient`
(the default one has a 30 s timeout), and `maxRetries` to change how often a call is retried.

## Paging

```csharp
await hm.PaginateAsync(hm.GetV1ProfilesAsync, new Params { Query = { ["limit"] = 100 } },
    profile => Console.WriteLine(profile.GetProperty("id").GetString()));
```

`PaginateAsync` follows `nextCursor` until it is empty and calls you with every entry of `items`.

## Idempotency

Every non-GET call sends an `Idempotency-Key` (a random GUID unless you set `IdempotencyKey`).

## Retries

Network errors, timeouts, 429 and 5xx are retried automatically with exponential backoff and jitter, honouring `Retry-After`. Every
attempt of a call sends the same `Idempotency-Key`, so a retried write never runs twice. `maxRetries` defaults to 3; `0` turns
retries off. When you retry a failed call yourself, pass the same `IdempotencyKey`.

## Errors

Any non-2xx answer throws `HermeticaException` with `Status`, `Code`, `ErrorMessage` and `Details` (a `JsonElement?`);
`Message` is "status code: ErrorMessage". Network failures, once retries run out, throw `HttpRequestException` / `TaskCanceledException`.

```csharp
try
{
    await hm.GetV1ProfilesByIdAsync(new Params { Path = { ["id"] = "missing" } });
}
catch (HermeticaException e)
{
    Console.Error.WriteLine($"{e.Status} {e.Code}: {e.ErrorMessage}");
}
```

Consent and suppression are enforced by the API; the client cannot bypass them.
