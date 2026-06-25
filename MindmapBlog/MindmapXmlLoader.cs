using System.Text;
using System.Xml.Linq;

namespace MindmapBlog;

/// <summary>
/// 容错加载 .mm：Docear/FreeMind 常把 emoji 写成两个 UTF-16 代理项实体（如 &#xd83d;&#xdccd;），
/// XML 1.0 不允许单独出现代理项，会导致 <see cref="XDocument.Load"/> 失败。
/// </summary>
internal static class MindmapXmlLoader
{
    public static XDocument Load(string mmFilePath)
    {
        var fullPath = Path.GetFullPath(mmFilePath);
        var raw = ReadAllText(fullPath);
        var repaired = Preprocess(raw);
        return XDocument.Parse(repaired, LoadOptions.PreserveWhitespace);
    }

    private static string ReadAllText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return Encoding.UTF8.GetString(bytes);
    }

    internal static string Preprocess(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (TryReadEntity(text, i, out var cp1, out var len1)
                && cp1 is >= 0xD800 and <= 0xDBFF
                && i + len1 < text.Length
                && TryReadEntity(text, i + len1, out var cp2, out var len2)
                && cp2 is >= 0xDC00 and <= 0xDFFF)
            {
                var merged = char.ConvertToUtf32((char)cp1, (char)cp2);
                sb.Append("&#x").Append(merged.ToString("X")).Append(';');
                i += len1 + len2;
                continue;
            }

            if (TryReadEntity(text, i, out var cp, out var len))
            {
                if (cp is >= 0xD800 and <= 0xDFFF || !IsLegalXmlCodePoint(cp))
                {
                    i += len;
                    continue;
                }

                sb.Append(text, i, len);
                i += len;
                continue;
            }

            var c = text[i];
            if (c == '\0')
            {
                i++;
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                i++;
                continue;
            }

            if (IsLegalXmlChar(c))
                sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static bool TryReadEntity(string text, int start, out int codePoint, out int length)
    {
        codePoint = 0;
        length = 0;
        if (start >= text.Length || text[start] != '&')
            return false;

        var i = start + 1;
        if (i >= text.Length || text[i] != '#')
            return false;
        i++;

        if (i >= text.Length)
            return false;

        if (text[i] is 'x' or 'X')
        {
            i++;
            var value = 0;
            var hasDigit = false;
            while (i < text.Length && IsHexDigit(text[i]))
            {
                value = value * 16 + HexValue(text[i]);
                hasDigit = true;
                i++;
            }

            if (!hasDigit || i >= text.Length || text[i] != ';')
                return false;

            codePoint = value;
            length = i - start + 1;
            return true;
        }

        if (!char.IsDigit(text[i]))
            return false;

        long value10 = 0;
        while (i < text.Length && char.IsDigit(text[i]))
        {
            value10 = value10 * 10 + (text[i] - '0');
            i++;
        }

        if (i >= text.Length || text[i] != ';')
            return false;

        codePoint = (int)value10;
        length = i - start + 1;
        return true;
    }

    private static bool IsHexDigit(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int HexValue(char c) =>
        c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => c - 'A' + 10,
        };

    private static bool IsLegalXmlChar(char c) =>
        c is '\t' or '\n' or '\r'
            or (>= '\u0020' and <= '\uD7FF')
            or (>= '\uE000' and <= '\uFFFD');

    private static bool IsLegalXmlCodePoint(int cp) =>
        cp is 0x9 or 0xA or 0xD
            or (>= 0x20 and <= 0xD7FF)
            or (>= 0xE000 and <= 0xFFFD)
            or (>= 0x10000 and <= 0x10FFFF);
}
