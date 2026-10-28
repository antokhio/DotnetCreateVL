using System.Text;
using ConsoleAppFramework;
using DotnetCreateVL.Cli;

// Windows consoles often default to an OEM code page, which mangles ✓ and — in the transcript.
Console.OutputEncoding = Encoding.UTF8;

var app = ConsoleApp.Create();
app.Add<VlCommand>();
await app.RunAsync(args);
