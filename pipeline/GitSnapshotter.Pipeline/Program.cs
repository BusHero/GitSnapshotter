using GitSnapshotter.Pipeline;
using GitSnapshotter.Pipeline.Modules;

using Microsoft.Extensions.DependencyInjection;

using ModularPipelines;
using ModularPipelines.Extensions;

var builder = Pipeline.CreateBuilder(args);

builder.Services.Configure<BuildSettings>(builder.Configuration.GetSection(BuildSettings.Section));

builder.Services
    .AddModule<VerifyFormattingModule>()
    .AddModule<CompileModule>()
    .AddModule<RunUnitTestsModule>()
    .AddModule<ThirdPartyNoticesModule>()
    .AddModule<PackModule>()
    .AddModule<PublishConsoleModule>()
    .AddModule<PackToolModule>()
    .AddModule<PushPackageModule>();

await builder.Build().RunAsync();
