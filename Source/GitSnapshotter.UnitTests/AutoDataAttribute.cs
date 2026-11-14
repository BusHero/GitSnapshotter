using AutoFixture;
using AutoFixture.Kernel;

namespace GitSnapshotter.UnitTests;

/// <summary>Fills a test's parameters with anonymous values from AutoFixture, like AutoFixture.Xunit2's <c>[AutoData]</c>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AutoDataAttribute : UntypedDataSourceGeneratorAttribute
{
    protected override IEnumerable<Func<object?[]?>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata)
    {
        yield return () =>
        {
            var context = new SpecimenContext(new Fixture());

            return dataGeneratorMetadata.MembersToGenerate
                .Select(member => context.Resolve(member switch
                {
                    ParameterMetadata parameter => parameter.Type,
                    PropertyMetadata property => property.Type,
                    _ => throw new NotSupportedException($"Cannot generate a value for {member.Name}."),
                }))
                .ToArray();
        };
    }
}
