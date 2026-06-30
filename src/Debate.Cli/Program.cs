using System.Text;
using Debate.Cli;
using Debate.Models.FoundryLocal;
using Spectre.Console.Cli;

// Model-host mode: the same executable, re-invoked by the parent to serve a single
// model over stdin/stdout. This branch must run before anything writes to stdout
// (Spectre, encoding probes, banners) so the protocol channel stays clean.
if (args.Length > 0 && args[0] == FoundryModelHost.ModeArgument)
{
    using var hostCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        hostCts.Cancel();
    };

    return await FoundryModelHost.RunAsync(args[1..], hostCts.Token);
}

// Ensure box-drawing and Unicode render correctly on consoles whose default
// code page isn't UTF-8 (notably Windows).
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // Output is redirected to something that rejects the change; ignore.
}

var app = new CommandApp<RunCommand>();
app.Configure(config =>
{
    config.SetApplicationName("debate");
});

return await app.RunAsync(args);
