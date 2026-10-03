using VVO.Cli;

using var services = ServiceConfiguration.ConfigureServices();

return await CliApp.BuildRoot(services).Parse(args).InvokeAsync();
