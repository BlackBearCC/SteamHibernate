// src/SteamHibernate.Core/Vdf/VdfParser.cs
using System.Text;

namespace SteamHibernate.Core.Vdf;

public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        int pos = 0;
        var root = new VdfNode();
        ParseBody(text, ref pos, root);
        return root;
    }

    private static void ParseBody(string s, ref int pos, VdfNode parent)
    {
        while (true)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] == '}') return;

            string key = ReadToken(s, ref pos);
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) return;

            if (s[pos] == '{')
            {
                pos++; // consume {
                var child = new VdfNode();
                ParseBody(s, ref pos, child);
                if (pos < s.Length && s[pos] == '}') pos++; // consume }
                parent.Add(key, child);
            }
            else
            {
                string value = ReadToken(s, ref pos);
                parent.Add(key, new VdfNode { Value = value });
            }
        }
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            if (char.IsWhiteSpace(s[pos])) { pos++; continue; }
            if (pos + 1 < s.Length && s[pos] == '/' && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            break;
        }
    }

    private static string ReadToken(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length) return string.Empty;

        if (s[pos] == '"')
        {
            pos++; // opening quote
            var sb = new StringBuilder();
            while (pos < s.Length && s[pos] != '"')
            {
                if (s[pos] == '\\' && pos + 1 < s.Length) pos++; // escape
                sb.Append(s[pos++]);
            }
            if (pos < s.Length) pos++; // closing quote
            return sb.ToString();
        }

        int start = pos;
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}')
            pos++;
        return s[start..pos];
    }
}
