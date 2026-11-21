using System.Security.Cryptography;
using System.Text;
using Tier1.Api.Startup;
using Tier1.Application.Abstractions;
using Tier1.Domain.Common;
using Tier1.Infrastructure.Security;

namespace Tier1.Api.Security;

/// <summary>Marks an endpoint whose POST is never replayed from a stored answer (sign-in and sign-out set cookies; ADR-0018).</summary>
public sealed class SkipIdempotencyAttribute : Attribute;

public static class IdempotencyEndpoints
{
    public static TBuilder WithoutIdempotency<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SkipIdempotencyAttribute());
}

/// <summary>
/// BE-8 (ADR-0018): a POST sent with an <c>Idempotency-Key</c> header runs once. A retry with the same key and the same request
/// (after a lost answer, for example) gets the first answer back, marked <c>Idempotent-Replayed: true</c>, and nothing is done
/// twice. The same key with another request is refused (422 <c>idempotency_key_reused</c>); while the first request still runs,
/// a retry is refused (409 <c>request_in_progress</c>). Only a successful answer is kept: a refusal or failure frees the key, so
/// a corrected request may use it. Without the header nothing changes. Keys are per signed-in user.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, IIdempotencyStore store, IClock clock)
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>An answer larger than this is not kept (the key is freed); answers to creates and decisions are far smaller.</summary>
    internal const int MaxStoredBytes = 256 * 1024;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!HttpMethods.IsPost(ctx.Request.Method) || !ctx.Request.Headers.TryGetValue(Header, out var values)
            || ctx.GetEndpoint() is not { } endpoint || endpoint.Metadata.GetMetadata<SkipIdempotencyAttribute>() is not null
            || !Guid.TryParse(ctx.User.FindFirst(ClaimNames.UserId)?.Value, out var user))
        {
            await next(ctx);
            return;
        }

        var key = values.ToString();
        if (values.Count != 1 || !ValidKey(key))
        {
            await ApiErrors.WriteAsync(ctx, StatusCodes.Status400BadRequest, "invalid_idempotency_key",
                Lang.Pick("مفتاح منع التكرار في الطلب غير صالح.", "The request's Idempotency-Key is not valid."));
            return;
        }

        var target = ctx.Request.Path.Value + ctx.Request.QueryString.Value;
        var route = target.Length > 400 ? target[..400] : target;
        var hash = await RequestHashAsync(ctx.Request, ctx.RequestAborted);
        switch (await store.ClaimAsync(user, key, route, hash, clock.UtcNow, ctx.RequestAborted))
        {
            case IdempotencyClaim.Replay replay:
                ctx.Response.StatusCode = replay.StatusCode;
                if (replay.ContentType is { } type) ctx.Response.ContentType = type;
                ctx.Response.Headers[ReplayedHeader] = "true";
                await ctx.Response.Body.WriteAsync(replay.Body, ctx.RequestAborted);
                return;
            case IdempotencyClaim.Mismatch:
                await ApiErrors.WriteAsync(ctx, StatusCodes.Status422UnprocessableEntity, "idempotency_key_reused",
                    Lang.Pick("هذا الطلب يحمل مفتاحًا استُخدم من قبل لطلب مختلف. حدّث الصفحة وأعد المحاولة.",
                        "This request carries a key already used for a different request. Refresh the page and try again."));
                return;
            case IdempotencyClaim.InProgress:
                await ApiErrors.WriteAsync(ctx, StatusCodes.Status409Conflict, "request_in_progress",
                    Lang.Pick("الطلب نفسه ما زال قيد التنفيذ، أو لم تُعرف نتيجته بعد. انتظر قليلًا ثم حدّث الصفحة وتحقق قبل إعادة المحاولة.",
                        "The same request is still running, or its outcome is not known yet. Wait a moment, refresh the page and check before trying again."));
                return;
        }

        var original = ctx.Response.Body;
        var capture = new CaptureStream(original, MaxStoredBytes, ctx.RequestAborted);
        ctx.Response.Body = capture;
        var finished = false;
        try
        {
            await next(ctx);
            finished = true;
        }
        finally
        {
            ctx.Response.Body = original;
            // The work is committed by now, so recording its answer must not depend on the caller still listening.
            var status = ctx.Response.StatusCode;
            if (finished && status is >= 200 and < 300 && !capture.Overflowed)
                await store.CompleteAsync(user, key, status, ctx.Response.ContentType, capture.Captured, clock.UtcNow, CancellationToken.None);
            else
                await store.ReleaseAsync(user, key, CancellationToken.None);
        }
    }

    /// <summary>1–100 characters of letters, digits, '-', '_', '.' and ':' (a UUID, as the web client sends).</summary>
    internal static bool ValidKey(string key) =>
        key.Length is >= 1 and <= 100 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');

    /// <summary>
    /// SHA-256 of the method, path, query and body. A form upload is hashed by its fields and files, not its bytes, because the
    /// multipart boundary changes on every send of the same form.
    /// </summary>
    internal static async Task<string> RequestHashAsync(HttpRequest request, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(text));
            sha.AppendData([0]);
        }

        Add(request.Method);
        Add(request.Path.Value + request.QueryString.Value);
        var buffer = new byte[16 * 1024];
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            foreach (var name in form.Keys.Order(StringComparer.Ordinal))
            {
                Add(name);
                foreach (var value in form[name]) Add(value ?? "");
            }
            foreach (var file in form.Files.OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.FileName, StringComparer.Ordinal))
            {
                Add(file.Name);
                Add(file.FileName);
                await using var content = file.OpenReadStream();
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0) sha.AppendData(buffer, 0, read);
                sha.AppendData([0]);
            }
        }
        else
        {
            request.EnableBuffering();
            int read;
            while ((read = await request.Body.ReadAsync(buffer, ct)) > 0) sha.AppendData(buffer, 0, read);
            request.Body.Position = 0;
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>
    /// Passes the answer through to the caller while keeping a copy (up to a limit). A caller that has gone away (the lost answer
    /// this guards against) does not fail the request: its answer is still recorded, for the retry to receive.
    /// </summary>
    private sealed class CaptureStream(Stream inner, int limit, CancellationToken aborted) : Stream
    {
        private MemoryStream? copy = new();

        public bool Overflowed => copy is null;

        public byte[] Captured => copy?.ToArray() ?? [];

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        private void Keep(ReadOnlySpan<byte> data)
        {
            if (copy is null) return;
            if (copy.Length + data.Length > limit) copy = null;
            else copy.Write(data);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Keep(buffer);
            try { inner.Write(buffer); }
            catch (Exception e) when (Gone(e)) { }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            Keep(buffer.Span);
            try { await inner.WriteAsync(buffer, ct); }
            catch (Exception e) when (Gone(e)) { }
        }

        public override void Flush()
        {
            try { inner.Flush(); }
            catch (Exception e) when (Gone(e)) { }
        }

        public override async Task FlushAsync(CancellationToken ct)
        {
            try { await inner.FlushAsync(ct); }
            catch (Exception e) when (Gone(e)) { }
        }

        private bool Gone(Exception e) => aborted.IsCancellationRequested && e is IOException or OperationCanceledException or ObjectDisposedException;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
