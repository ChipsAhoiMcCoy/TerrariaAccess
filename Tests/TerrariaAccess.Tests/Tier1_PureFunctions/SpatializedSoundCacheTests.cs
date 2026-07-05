#nullable enable

using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class SpatializedSoundCacheTests
{
    [Fact]
    public void GetOrCreate_WhenFactoryReturnsNull_ThrowsWithoutCaching()
    {
        using var cache = new SpatializedSoundCache();

        Action act = () => cache.GetOrCreate(0f, _ => null!);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Spatialized sound factory returned an unusable SoundEffect.");
    }

    [Fact]
    public void GetOrCreate_WithNullFactory_ThrowsArgumentNullException()
    {
        using var cache = new SpatializedSoundCache();

        Action act = () => cache.GetOrCreate(0f, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("factory");
    }

    [Fact]
    public void GenericGetOrCreate_WhenFactoryReturnsNull_ThrowsWithoutCaching()
    {
        using var cache = new SpatializedSoundCache<string>();

        Action act = () => cache.GetOrCreate("cue", 0f, _ => null!);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Spatialized sound factory returned an unusable SoundEffect.");
    }

    [Fact]
    public void GenericGetOrCreate_WithNullFactory_ThrowsArgumentNullException()
    {
        using var cache = new SpatializedSoundCache<string>();

        Action act = () => cache.GetOrCreate("cue", 0f, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("factory");
    }
}
