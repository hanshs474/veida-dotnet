using System.Text.Json;
using Veida;

// Unit checks on the response parsers, then one real generation.
string Env(string json) => json;
var wall = JsonDocument.Parse("{\"code\":0,\"data\":{\"wall\":true,\"reason\":\"anon_ip_daily\"}}").RootElement;
try { VeidaClient.ParseSubmit(wall); throw new System.Exception("wall not detected"); }
catch (VeidaException e) when (e.Kind == VeidaErrorKind.Quota) { System.Console.WriteLine("ok   wall is quota"); }
if (VeidaClient.ParseSubmit(JsonDocument.Parse("{\"code\":0,\"data\":{\"id\":\"t1\"}}").RootElement) != "t1") throw new System.Exception("task id");
if (VeidaClient.ParsePoll(JsonDocument.Parse("{\"code\":0,\"data\":{\"status\":\"processing\"}}").RootElement) != null) throw new System.Exception("pending");
System.Console.WriteLine("ok   parsers");

var prompt = args.Length > 0 ? string.Join(" ", args) : "a paper boat on a calm lake at sunrise, soft light";
var img = await new VeidaClient().GenerateAsync(prompt, "16:9");
System.Console.WriteLine($"live: {img.Url} (watermarked: {img.Watermarked})");
