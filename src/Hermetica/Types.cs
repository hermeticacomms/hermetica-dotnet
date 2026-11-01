#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Hermetica
{
    /// <summary>The inputs of one call: path params, query params, a JSON body and an optional Idempotency-Key.</summary>
    public sealed class Params
    {
        /// <summary>Values for the {name} placeholders of the path.</summary>
        public Dictionary<string, object?> Path { get; } = new Dictionary<string, object?>();

        /// <summary>Query parameters; null values are skipped and a collection value repeats the key.</summary>
        public Dictionary<string, object?> Query { get; } = new Dictionary<string, object?>();

        /// <summary>The body, serialized with System.Text.Json; null for none.</summary>
        public object? Body { get; set; }

        /// <summary>Idempotency-Key for a non-GET call; a random one is sent when null.</summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>A copy whose dictionaries can be changed independently.</summary>
        public Params Copy()
        {
            var p = new Params { Body = Body, IdempotencyKey = IdempotencyKey };
            foreach (var kv in Path) p.Path[kv.Key] = kv.Value;
            foreach (var kv in Query) p.Query[kv.Key] = kv.Value;
            return p;
        }
    }

    /// <summary>Thrown for any non-2xx answer. <see cref="Exception.Message"/> is "status code: ErrorMessage".</summary>
    public class HermeticaException : Exception
    {
        public HermeticaException(int status, string code, string errorMessage, JsonElement? details)
            : base(status + " " + code + ": " + errorMessage)
        {
            Status = status;
            Code = code;
            ErrorMessage = errorMessage;
            Details = details;
        }

        /// <summary>HTTP status.</summary>
        public int Status { get; }

        /// <summary>Error code from the API, or "http_error".</summary>
        public string Code { get; }

        /// <summary>Error message from the API (or the start of the raw body).</summary>
        public string ErrorMessage { get; }

        /// <summary>Error details, or null.</summary>
        public JsonElement? Details { get; }
    }
}
