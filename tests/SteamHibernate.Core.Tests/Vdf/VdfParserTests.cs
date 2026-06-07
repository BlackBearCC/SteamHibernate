// tests/.../Vdf/VdfParserTests.cs
using SteamHibernate.Core.Vdf;
using Xunit;

public class VdfParserTests
{
    [Fact]
    public void Parses_nested_keyvalues_with_quoted_strings()
    {
        var text = """
        "AppState"
        {
            "appid"     "1091500"
            "name"      "Cyberpunk 2077"
            "UserConfig"
            {
                "language"  "schinese"
            }
        }
        """;

        var root = VdfParser.Parse(text);

        Assert.Equal("1091500", root["AppState"]["appid"].Value);
        Assert.Equal("Cyberpunk 2077", root["AppState"]["name"].Value);
        Assert.Equal("schinese", root["AppState"]["UserConfig"]["language"].Value);
    }

    [Fact]
    public void Missing_key_returns_empty_node_not_throw()
    {
        var root = VdfParser.Parse("\"A\" { \"b\" \"1\" }");
        Assert.Null(root["A"]["zzz"].Value);
        Assert.True(root["A"]["zzz"].IsEmpty);
    }

    [Fact]
    public void Comment_lines_are_ignored()
    {
        var text = "\"A\" {\n    // a comment\n    \"k\" \"v\"\n}";
        var root = VdfParser.Parse(text);
        Assert.Equal("v", root["A"]["k"].Value);
        Assert.True(root["A"]["//"].IsEmpty); // no spurious comment key
    }
}
