using GitSnapshotter.Pipeline;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModularPipelines;

var builder = Pipeline.CreateBuilder(args);

var section = builder.Configuration.GetSection(BuildSettings.Section);
builder.Services.Configure<BuildSettings>(section);

var target = (section.Get<BuildSettings>() ?? new BuildSettings()).Target;
builder.AddModules(target);

var pipeline = await builder.BuildAsync();
await pipeline.RunAsync();
