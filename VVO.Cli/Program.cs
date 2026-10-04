using System.Text;
using VVO.Cli;

// File names are not ASCII, and a console left on its legacy code page would mangle them
Console.OutputEncoding = Encoding.UTF8;

using var services = ServiceConfiguration.ConfigureServices();

return await CliApp.RunAsync(CliApp.BuildRoot(services), args, Console.Out, Console.Error);
