using System.Reflection;

// Composition root. Commands (install, project, doctor, statusline, mcp-launch) arrive in later units.
if (args is ["--version"])
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    Console.Out.WriteLine($"nexer-ai {version}");
    return 0;
}

Console.Error.WriteLine("Usage: nexer-ai --version");
return 2;
