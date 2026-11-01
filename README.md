# Hermetica .NET client

Generated from the Hermetica OpenAPI spec (2038 operations). netstandard2.0 and net8.0; System.Net.Http and System.Text.Json only.

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
serialized with System.Text.Json. Pass `workspaceId` to send `X-Workspace-Id`, and `http` to bring your own `HttpClient`
(the default one has a 30 s timeout).

## Paging

```csharp
await hm.PaginateAsync(hm.GetV1ProfilesAsync, new Params { Query = { ["limit"] = 100 } },
    profile => Console.WriteLine(profile.GetProperty("id").GetString()));
```

`PaginateAsync` follows `nextCursor` until it is empty and calls you with every entry of `items`.

## Idempotency

Every non-GET call sends an `Idempotency-Key` (a random GUID unless you set `IdempotencyKey`). The client does not retry on its own;
when you retry a failed call, pass the same key so the action never runs twice.

## Errors

Any non-2xx answer throws `HermeticaException` with `Status`, `Code`, `ErrorMessage` and `Details` (a `JsonElement?`);
`Message` is "status code: ErrorMessage". Network failures throw `HttpRequestException` / `TaskCanceledException`.

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
