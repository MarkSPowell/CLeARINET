// FiddlerScript demo: runs a script's OnBeforeRequest/OnBeforeResponse
// against a made-up request and response in memory, and prints what
// changed. No network.
//
// Usage:
//   dotnet run --project tools\Violens.FiddlerScriptDemo
//     Runs the bundled SampleCustomRules.js.
//   dotnet run --project tools\Violens.FiddlerScriptDemo -- path\to\CustomRules.js
//     Runs your own script.

using Violens.Compatibility.FiddlerScript;
using Violens.ProxyCore.Http;

var scriptPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "SampleCustomRules.js");

Console.WriteLine("VioLens FiddlerScript smoke test");
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

// Made up to trigger the sample script's "slow-endpoint" rule.
var request = new CapturedRequest(
    "GET",
    "/api/slow-endpoint?id=42",
    "HTTP/1.1",
    [("Host", "api.example.com"), ("User-Agent", "Violens-FiddlerScript-Demo/1.0")],
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
    // Flags can't be listed, so check the names the sample script uses.
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
