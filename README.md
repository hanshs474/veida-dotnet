# Veida for .NET

Generate images from C# and .NET with **no API key, no account and no card**.

```csharp
using Veida;

var img = await new VeidaClient().GenerateAsync("a paper boat on a calm lake at sunrise, soft light", "16:9");
Console.WriteLine(img.Url);         // https://cdn.veida.ai/uploads/....webp
Console.WriteLine(img.Watermarked); // True on the free tier
```

Install:

```bash
dotnet add package Veida
```

Most image libraries want a key from OpenAI, fal or Replicate before they run once.
This one talks to the anonymous tier of [Veida](https://veida.ai/?utm_source=nuget&utm_medium=package), an online AI image generator,
using a client id the library invents rather than an account you register, so it works on a
fresh machine with nothing configured.

## What the free tier covers

Measured against the running service:

| | |
|---|---|
| Anonymous grant per client id | 4 credits |
| One text-to-image run | 4 credits, so one image per id |
| Per-IP ceiling | 30 credits a day, roughly 7 images |
| Output | 1K, watermarked |

The library mints a fresh client id for every call, so a loop is not limited to one image;
the per-IP ceiling is what stops it. When that is spent you get a `VeidaException` with `Kind == VeidaErrorKind.Quota` saying so.

Editing an existing photo is **not** offered here. The editing model costs 30 credits against
a 4-credit grant, so an anonymous edit could never be paid for. Signed in, the
[image editor](https://veida.ai/image/chatgpt-image-editor?utm_source=nuget&utm_medium=package) does that in a browser.

## Past the free tier

Signing in removes the watermark, lifts the cap and opens the larger models:
[GPT Image 2](https://veida.ai/image/gpt-image-2?utm_source=nuget&utm_medium=package) when the picture must contain readable text,
[Nano Banana 2](https://veida.ai/image/nano-banana-2?utm_source=nuget&utm_medium=package) for instruction following and
[Seedream 5.0 Lite](https://veida.ai/image/seedream-5-lite?utm_source=nuget&utm_medium=package) for colour and composition.
What each costs is on the [pricing page](https://veida.ai/pricing?utm_source=nuget&utm_medium=package).

## Prompts that work

Name the light, the material and the composition. "A product photo of a mug" gives the model
nothing; "matte black ceramic mug on pale oak, soft window light from the left, shallow depth
of field" gives it a picture. Worked examples live in the
[prompt library](https://veida.ai/image/prompts?utm_source=nuget&utm_medium=package). The same engine backs the
[ChatGPT image generator](https://veida.ai/image/chatgpt-image-generator?utm_source=nuget&utm_medium=package) and the
[free AI image generator](https://veida.ai/image?utm_source=nuget&utm_medium=package) in a browser.

## API

```csharp
new VeidaClient(HttpClient? http = null, TimeSpan? pollEvery = null, TimeSpan? timeout = null)
Task<VeidaImage> GenerateAsync(string prompt, string aspectRatio = "1:1", CancellationToken ct = default)

VeidaErrorKind: Quota (wait or sign in) · Rejected (reword) · Timeout (retry) · Service
```

Aspect ratio is one of `1:1`, `16:9`, `9:16`, `4:3`, `3:4`.

Targets `net8.0` and `netstandard2.0`, so it runs on .NET Framework 4.6.2+, Unity and Xamarin too. No dependency beyond `System.Text.Json`.

Other clients: [Python](https://pypi.org/project/veida/) ·
[Go](https://pkg.go.dev/github.com/hanshs474/veida-go) ·
[MCP server](https://www.npmjs.com/package/veida-mcp).

MIT.
