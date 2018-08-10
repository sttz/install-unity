using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace sttz.InstallUnity.Tests
{

public class ArgumentsTests
{
    public InstallUnityCLI Parse(params string[] args)
    {
        return InstallUnityCLI.Parse(args);
    }

    class Args
    {
        public string optionalArgument;
        public string requiredPositional;

        public static Args Parse(params string[] args)
        {
            var parsed = new Args();
            var def = new Arguments<Args>()
                .Option((Args t, string v) => t.optionalArgument = v, "a", "arg").OptionalArgument(true)
                .Option((Args t, string v) => t.requiredPositional = v, 0).Required();
            def.Parse(parsed, args);
            return parsed;
        }
    }

    [Fact]
    public void TestGlobalShortOptions()
    {
        Assert.Equal(
            " --help",
            Parse("-?").ToString()
        );
    }

    [Fact]
    public void TestGlobalLongOptions()
    {
        Assert.Equal(
            " --help",
            Parse("--help").ToString()
        );
    }

    [Fact]
    public void TestCombinedShortOptions()
    {
        Assert.Equal(
            " --help --verbose",
            Parse("-vh").ToString()
        );
    }

    [Fact]
    public void TestRepeatedOption()
    {
        Assert.Equal(
            " --verbose --verbose",
            Parse("-vv").ToString()
        );
    }

    [Fact]
    public void TestAction()
    {
        Assert.Equal(
            "list",
            Parse("list").ToString()
        );
    }

    [Fact]
    public void TestPositionalArg()
    {
        Assert.Equal(
            "list --verbose 2018",
            Parse("list", "-v", "2018").ToString()
        );
    }

    [Fact]
    public void TestLocalOptions()
    {
        Assert.Equal(
            "list 2018 --installed",
            Parse("list", "2018", "-i").ToString()
        );
    }

    [Fact]
    public void TestListOption()
    {
        Assert.Equal(
            "install f --packages Mac Linux Windows --download",
            Parse("install", "f", "-p", "Mac", "Linux", "Windows", "--download").ToString()
        );

        Assert.Equal(
            "install f --packages Mac Linux Windows --download",
            Parse("install", "f", "-p", "Mac,Linux,Windows", "--download").ToString()
        );

        Assert.Equal(
            "install f --packages Mac Linux Windows --download",
            Parse("install", "f", "--packages=Mac,Linux,Windows", "--download").ToString()
        );

        Assert.Equal(
            "install f --packages Mac Linux Windows --download",
            Parse("install", "f", "--packages:Mac,Linux,Windows", "--download").ToString()
        );
    }

    [Fact]
    public void TestCommaListOption()
    {
        Assert.Equal(
            "install f --packages Mac Linux Windows",
            Parse("install", "--packages", "Mac,Linux,Windows", "f").ToString()
        );
    }

    [Fact]
    public void TestTerinatedListOption()
    {
        Assert.Equal(
            "install f --packages Mac Linux Windows",
            Parse("install", "--packages", "Mac", "Linux", "Windows", "--", "f").ToString()
        );
    }

    [Fact]
    public void TestWindowsOption()
    {
        Assert.Equal(
            "install --verbose f --packages Mac Linux Windows",
            Parse("/v", "install", "f", "/packages", "Mac", "Linux", "Windows").ToString()
        );
    }

    [Fact]
    public void TestRepeatedListOption()
    {
        Assert.Equal(
            "install --packages Mac Linux Windows",
            Parse("install", "-p", "Mac", "-p", "Linux", "-p", "Windows").ToString()
        );
    }

    [Fact]
    public void TestPathArgument()
    {
        Assert.Equal(
            "uninstall /Applications/Unity",
            Parse("uninstall", "/Applications/Unity").ToString()
        );
    }

    [Fact]
    public void TestInvalidAction()
    {
        var ex = Assert.Throws<ArgumentsException>(() => Parse("liist"));
        Assert.Equal("Unexpected argument at position #0: liist", ex.Message);
    }

    [Fact]
    public void TestInvalidPositional()
    {
        var ex = Assert.Throws<ArgumentsException>(() => Parse("list", "2018", "2019"));
        Assert.Equal("Unexpected argument at position #1: 2019", ex.Message);
    }

    [Fact]
    public void TestInvalidShortOption()
    {
        var ex = Assert.Throws<ArgumentsException>(() => Parse("-vz"));
        Assert.Equal("Unknown short option: z", ex.Message);
    }

    [Fact]
    public void TestInvalidLongOption()
    {
        var ex = Assert.Throws<ArgumentsException>(() => Parse("--blah"));
        Assert.Equal("Unknown option: blah", ex.Message);
    }

    [Fact]
    public void TestInvalidLocalOptionUsedAsGlobal()
    {
        var ex = Assert.Throws<ArgumentsException>(() => Parse("--installed"));
        Assert.Equal("Unknown option: installed", ex.Message);
    }

    [Fact]
    public void TestInvalidListOption()
    {
        var ex = Assert.Throws<ArgumentsException>(() => Parse("install", "--packages"));
        Assert.Equal("Missing arguments for option: packages", ex.Message);

        ex = Assert.Throws<ArgumentsException>(() => Parse("install", "--packages", "--install"));
        Assert.Equal("Missing arguments for option: packages", ex.Message);

        ex = Assert.Throws<ArgumentsException>(() => Parse("install", "-pv Mac"));
        Assert.Equal("Missing arguments for option: p", ex.Message);
    }

    [Fact]
    public void TestRepeatablePositionalArgument()
    {
        Assert.Equal(
            "run 2018 --detach -- -batchmode -nographics -quit",
            Parse("run", "-d", "2018", "--", "-batchmode", "-nographics", "-quit").ToString()
        );
    }

    [Fact]
    public void TestPathsAndOptions()
    {
        Assert.Equal(
            "install --data-path /tmp/test 2018 --download",
            Parse("install", "2018", "--data-path",  "/tmp/test",  "--download").ToString()
        );

        Assert.Equal(
            "install --data-path /tmp/test 2018 --download",
            Parse("install", "/dataPath", "/tmp/test", "/download", "2018").ToString()
        );

        var ex = Assert.Throws<ArgumentsException>(() => Parse("install", "/dataPath", "/download", "2018"));
        Assert.Equal("Missing argument for option: dataPath", ex.Message);

        Assert.Equal(
            "uninstall /Applications/Unity",
            Parse("uninstall", "/Applications/Unity").ToString()
        );
    }

    [Fact]
    public void TestMissingPositionalArgument()
    {
        var ex = Assert.Throws<ArgumentsException>(() => Args.Parse("-a"));
        Assert.Equal("Required argument #0 not set.", ex.Message);
    }

    [Fact]
    public void TestValidMissingArgument()
    {
        Args.Parse("test -a");
    }
}

}
