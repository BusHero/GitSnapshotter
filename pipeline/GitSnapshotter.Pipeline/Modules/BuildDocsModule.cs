using ModularPipelines.Context;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using ModularPipelines.Options;

namespace GitSnapshotter.Pipeline.Modules;

/// <summary>Builds the documentation site into docs/dist with npm (Node.js must be installed).</summary>
public sealed class BuildDocsModule : Module<CommandResult>
{
    protected override async Task<CommandResult?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await Npm("ci");
        return await Npm("run", "build");

        Task<CommandResult> Npm(params string[] arguments) =>
            context.Shell.Command.ExecuteCommandLineTool(
                // On Windows npm is a batch script (npm.cmd), which only finds its own files when cmd starts it.
                OperatingSystem.IsWindows()
                    ? new GenericCommandLineToolOptions("cmd") { Arguments = ["/c", "npm", .. arguments] }
                    : new GenericCommandLineToolOptions("npm") { Arguments = arguments },
                new CommandExecutionOptions { WorkingDirectory = Paths.Docs(context) },
                cancellationToken);
    }
}
