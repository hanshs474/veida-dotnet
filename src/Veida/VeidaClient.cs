using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Veida
{
    /// <summary>One finished picture.</summary>
    public sealed class VeidaImage
    {
        /// <summary>A permanent, cacheable CDN link.</summary>
        public string Url { get; }
        /// <summary>Whether a mark was drawn on it. Signing in on veida.ai removes it.</summary>
        public bool Watermarked { get; }
        /// <summary>Creates an image result.</summary>
        public VeidaImage(string url, bool watermarked) { Url = url; Watermarked = watermarked; }
        /// <inheritdoc />
        public override string ToString() => Url;
    }

    /// <summary>Why a call failed, split by what the caller should do next.</summary>
    public enum VeidaErrorKind
    {
        /// <summary>The free allowance is spent for this machine today. Wait, or sign in.</summary>
        Quota,
        /// <summary>The content filter refused the prompt. Reword it.</summary>
        Rejected,
        /// <summary>The deadline passed while the job was still queued. Safe to retry.</summary>
        Timeout,
        /// <summary>The service answered with an error.</summary>
        Service,
    }

    /// <summary>The exception this library throws on purpose. Branch on <see cref="Kind"/>.</summary>
    public sealed class VeidaException : Exception
    {
        /// <summary>What went wrong.</summary>
        public VeidaErrorKind Kind { get; }
        /// <summary>Creates an exception of a given kind.</summary>
        public VeidaException(VeidaErrorKind kind, string message) : base("veida: " + message) { Kind = kind; }
    }

    /// <summary>
    /// Generates images with no API key and no account, via the free tier of https://veida.ai.
    /// Measured against the running service: 4 credits per client id, 4 per image, 30 per IP per day.
    /// Free output is 1K and watermarked.
    /// </summary>
    public sealed class VeidaClient
    {
        /// <summary>The service this library talks to.</summary>
        public const string DefaultBaseUrl = "https://veida.ai";
        /// <summary>Credits one machine may spend per day.</summary>
        public const int IpDailyCeiling = 30;
        /// <summary>The free text-to-image engine.</summary>
        public const string Model = "veida-image-v1";
        /// <summary>Aspect ratios the free engine accepts.</summary>
        public static readonly string[] AspectRatios = { "1:1", "16:9", "9:16", "4:3", "3:4" };

        private readonly HttpClient _http;
        private readonly string _base;
        private readonly TimeSpan _pollEvery;
        private readonly TimeSpan _timeout;

        /// <summary>Creates a client. Pass an <see cref="HttpClient"/> to share a connection pool or in tests.</summary>
        public VeidaClient(HttpClient? http = null, string baseUrl = DefaultBaseUrl, TimeSpan? pollEvery = null, TimeSpan? timeout = null)
        {
            _http = http ?? new HttpClient();
            _base = baseUrl;
            _pollEvery = pollEvery ?? TimeSpan.FromSeconds(4);
            _timeout = timeout ?? TimeSpan.FromMinutes(4);
        }

        /// <summary>A fresh client id. The grant pays for one image, so ids are never reused.</summary>
        public static string AnonId()
        {
            var b = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return "cs-" + BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Turns a prompt into an image. Name the light, the material and the composition.</summary>
        public async Task<VeidaImage> GenerateAsync(string prompt, string aspectRatio = "1:1", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("prompt is required", nameof(prompt));
            if (!AspectRatios.Contains(aspectRatio))
                throw new ArgumentException("aspectRatio must be one of " + string.Join(", ", AspectRatios), nameof(aspectRatio));
            var deadline = DateTime.UtcNow + _timeout;
            var id = AnonId();
            var payload = new
            {
                provider = "kie", mediaType = "image", model = Model, scene = "text-to-image", prompt,
                options = new { aspect_ratio = aspectRatio },
            };
            var req = new HttpRequestMessage(HttpMethod.Post, _base + "/api/ai/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-anon-id", id);
            string task;
            using (var res = await _http.SendAsync(req, ct).ConfigureAwait(false))
            using (var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false)))
                task = ParseSubmit(doc.RootElement);

            var query = $"{_base}/api/ai/anon-query?taskId={Uri.EscapeDataString(task)}&provider=kie&mediaType=image";
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(_pollEvery, ct).ConfigureAwait(false);
                string body;
                try
                {
                    var poll = new HttpRequestMessage(HttpMethod.Get, query);
                    poll.Headers.Add("x-anon-id", id);
                    using var pres = await _http.SendAsync(poll, ct).ConfigureAwait(false);
                    body = await pres.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    continue; // a dropped poll is not a failed generation
                }
                using var pdoc = JsonDocument.Parse(body);
                var img = ParsePoll(pdoc.RootElement);
                if (img != null) return img;
            }
            throw new VeidaException(VeidaErrorKind.Timeout, "still queued when the deadline passed");
        }

        /// <summary>
        /// Interprets the submit response. The quota wall answers HTTP 200 with code 0,
        /// so it can only be recognised by a field.
        /// </summary>
        public static string ParseSubmit(JsonElement env)
        {
            var code = env.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
            if (code != 0)
                throw new VeidaException(VeidaErrorKind.Service, env.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "request refused");
            if (!env.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object)
                throw new VeidaException(VeidaErrorKind.Service, "the service returned no task id");
            if (d.TryGetProperty("wall", out var w) && w.ValueKind == JsonValueKind.True)
            {
                var reason = d.TryGetProperty("reason", out var r) ? r.GetString() : null;
                throw new VeidaException(VeidaErrorKind.Quota, reason == "anon_ip_daily"
                    ? $"this machine has used its {IpDailyCeiling} free credits for today; sign in at {DefaultBaseUrl}/pricing"
                    : $"free allowance spent; sign in at {DefaultBaseUrl}/pricing");
            }
            var id = d.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrEmpty(id)) throw new VeidaException(VeidaErrorKind.Service, "the service returned no task id");
            return id!;
        }

        /// <summary>Interprets one poll. Status is not monotonic, so only a terminal failure ends the wait.</summary>
        public static VeidaImage? ParsePoll(JsonElement env)
        {
            if (!env.TryGetProperty("code", out var pc) || pc.ValueKind != JsonValueKind.Number || pc.GetInt32() != 0) return null;
            if (!env.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return null;
            if (d.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array && imgs.GetArrayLength() > 0)
            {
                var wm = d.TryGetProperty("watermarked", out var w) && w.ValueKind == JsonValueKind.Array
                         && w.GetArrayLength() > 0 && w[0].ValueKind == JsonValueKind.True;
                return new VeidaImage(imgs[0].GetString()!, wm);
            }
            var status = (d.TryGetProperty("status", out var s) ? s.GetString() : null)?.ToLowerInvariant();
            if (status == "failed" || status == "error")
                throw new VeidaException(VeidaErrorKind.Rejected, "the prompt was refused by the content filter; reword it");
            return null;
        }
    }
}
