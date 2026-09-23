// CLeARINET FiddlerScript smoke test -- a small, standalone way to see the
// Jint-based FiddlerScript engine (Clearinet.Compatibility.FiddlerScript)
// actually do something, without needing your own CustomRules.js or
// InterceptingProxyListener wired up yet (that wiring doesn't exist yet --
// see the FiddlerScript Compatibility Design doc's "Phase A2").
//
// This tool doesn't touch the network at all: it builds a synthetic
// request and response entirely in memory, runs a script's
// OnBeforeRequest/OnBeforeResponse against them, and prints what changed.
// Throwaway tooling, same spirit as Clearinet.DevHost -- not a preview of
// product UI or CLI.
//
// Usage:
//   dotnet run --project tools\Clearinet.FiddlerScriptDemo
//     Runs the bundled SampleCustomRules.js (an original demo script
//     written for this tool -- see that file's own header comment).
//   dotnet run --project tools\Clearinet.FiddlerScriptDemo -- path\to\CustomRules.js
//     Runs a script of your own instead.

using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Http;

var scriptPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "SampleCustomRules.js");

Console.WriteLine("CLeARINET FiddlerScript smoke test");
Console.WriteLine();

if (!File.Exists(scriptPath))
{
    Console.WriteLine($"Couldn't find the script at: {scriptPath}");
    return 1;
}

Console.WriteLine($"Loading: {scriptPath}");
var scriptSource = File.ReadAllText(scriptPath);

FiddlerScriptHost host;
try
{
    host = new FiddlerScriptHost(scriptSource, new AppObject(log: message => Console.WriteLine($"  [script] {message}")));
}
catch (FiddlerScriptException ex)
{
    Console.WriteLine();
    Console.WriteLine($"Script failed to load: {ex.Message}");
    return 1;
}

Console.WriteLine("Script loaded OK.");
Console.WriteLine($"  Defines OnBeforeRequest:          {host.HasHandler("OnBeforeRequest")}");
Console.WriteLine($"  Defines OnBeforeResponse:         {host.HasHandler("OnBeforeResponse")}");
Console.WriteLine($"  Defines OnPeekAtResponseHeaders:  {host.HasHandler("OnPeekAtResponseHeaders")}");
Console.WriteLine();

// A made-up request -- nothing about this came off a real wire. Deliberately
// shaped to trip the bundled sample script's "slow-endpoint" check; if
// you're running your own script instead, this is a reasonable generic
// request to react to, but feel free to edit it to match what your script
// actually looks for.
var request = new CapturedRequest(
    "GET",
    "/api/slow-endpoint?id=42",
    "HTTP/1.1",
    [("Host", "api.example.com"), ("User-Agent", "Clearinet-FiddlerScript-Demo/1.0")],
    []);

Console.WriteLine("=== Before OnBeforeRequest ===");
PrintRequest(request);

var requestExchange = Exchange.ForRequest(1, "api.example.com", request);
try
{
    host.InvokeOnBeforeRequest(requestExchange);
}
catch (FiddlerScriptException ex)
{
    Console.WriteLine($"OnBeforeRequest threw: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== After OnBeforeRequest ===");
PrintExchangeRequestState(requestExchange);
Console.WriteLine();

// A made-up response to go with it -- again, nothing came off a real wire.
var response = new CapturedResponse(
    "HTTP/1.1",
    200,
    "OK",
    [("Content-Type", "text/plain; charset=utf-8")],
    "before body: placeholder value here"u8.ToArray());

Console.WriteLine("=== Before OnBeforeResponse ===");
PrintResponse(response);

var responseExchange = Exchange.ForResponse(1, "api.example.com", requestExchange.ToRequest(), response);
try
{
    host.InvokeOnBeforeResponse(responseExchange);
}
catch (FiddlerScriptException ex)
{
    Console.WriteLine($"OnBeforeResponse threw: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== After OnBeforeResponse ===");
PrintExchangeResponseState(responseExchange);

Console.WriteLine();
Console.WriteLine("Done. Nothing here touched a real network or file beyond the script itself --");
Console.WriteLine("safe to run as many times as you like, and safe to edit SampleCustomRules.js");
Console.WriteLine("and re-run to see how the output changes.");
return 0;

static void PrintRequest(CapturedRequest request)
{
    Console.WriteLine($"  {request.Method} {request.Target} {request.HttpVersion}");
    foreach (var (name, value) in request.Headers)
    {
        Console.WriteLine($"  {name}: {value}");
    }
}

static void PrintResponse(CapturedResponse response)
{
    Console.WriteLine($"  {response.HttpVersion} {response.StatusCode} {response.ReasonPhrase}");
    foreach (var (name, value) in response.Headers)
    {
        Console.WriteLine($"  {name}: {value}");
    }
    Console.WriteLine($"  Body: {System.Text.Encoding.UTF8.GetString(response.Body)}");
}

static void PrintExchangeRequestState(Exchange exchange)
{
    PrintRequest(exchange.ToRequest());
    PrintFlags(exchange);
}

static void PrintExchangeResponseState(Exchange exchange)
{
    PrintResponse(exchange.ToResponse());
    PrintFlags(exchange);
}

static void PrintFlags(Exchange exchange)
{
    // oFlags/the indexer only expose "does this name have a value", not
    // "list every name that's been set" -- real Fiddler's own oFlags has
    // the same shape (see ExchangeFlags's own remarks). Checking a fixed
    // list of the flag names this demo's sample script and the cookbook
    // patterns it's modeled on actually use is the pragmatic way to show
    // what changed without needing a full enumerable flags bag.
    string[] flagNames = ["ui-color", "ui-customcolumn", "ui-hide", "ui-bold", "x-breakrequest", "x-breakresponse"];
    foreach (var name in flagNames)
    {
        var value = exchange[name];
        if (value is not null)
        {
            Console.WriteLine($"  oSession[\"{name}\"] = \"{value}\"");
        }
    }
}
