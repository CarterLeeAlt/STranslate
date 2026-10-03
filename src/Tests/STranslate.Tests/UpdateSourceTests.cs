using STranslate.Core;

namespace STranslate.Tests;

public class UpdateSourceTests
{
    [Fact]
    public void AutoCheckUpdate_IsOffByDefault()
    {
        Assert.False(new Settings().AutoCheckUpdate);
    }

    [Fact]
    public void UpdateSourceAndChangelog_PointToForkWhileAboutLinkStaysUpstream()
    {
        Assert.Equal("https://github.com/CarterLeeAlt/STranslate", Constant.UpdateRepository);
        Assert.StartsWith("https://raw.githubusercontent.com/CarterLeeAlt/STranslate/", Constant.UpdateChangelogUrl);
        Assert.EndsWith("/CHANGELOG.md", Constant.UpdateChangelogUrl);
        Assert.Equal("https://github.com/STranslate/STranslate", Constant.Github);
    }
}
