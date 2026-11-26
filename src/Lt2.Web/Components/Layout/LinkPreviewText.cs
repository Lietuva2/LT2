using System.Text;

namespace Lt2.Web.Components.Layout;

/// <summary>The rules of a link preview's text and address (design 8.22.5), pure.</summary>
internal static class LinkPreviewText
{
    /// <summary>The default image, a static file under wwwroot: the logo and the name on the brand's colours.</summary>
    public const string Image = "img/link-preview.png";

    /// <summary>The start page's image: the logo and the name above the slogan and its subline, all inside the centred square.</summary>
    public const string HomeImage = "img/link-preview-home.png";

    public const int ImageWidth = 1200;

    public const int ImageHeight = 630;

    /// <summary>About as much as the previews show before they cut the text themselves.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// A text as one line for a preview: whitespace and line breaks as single spaces, a list's markers dropped, and whole sentences
    /// while they fit in <see cref="MaxLength"/>; a first sentence longer than that is cut at a word with "…".
    /// </summary>
    public static string Excerpt(string? text)
    {
        var line = OneLine(text);
        if (line.Length <= MaxLength)
        {
            return line;
        }

        var end = -1;
        for (var i = 0; i < MaxLength; i++)
        {
            if (line[i] is '.' or '!' or '?' && (i + 1 == line.Length || line[i + 1] == ' '))
            {
                end = i + 1;
            }
        }

        if (end > 0)
        {
            return line[..end];
        }

        var space = line.LastIndexOf(' ', MaxLength - 1);
        return (space > 0 ? line[..space] : line[..(MaxLength - 1)]).TrimEnd(',', ';', ':', ' ', '–', '-') + "…";
    }

    /// <summary>
    /// The page's own address for <c>og:url</c>: the path alone, without a query or a fragment (a token, a code or a page's state
    /// never goes into a preview), always with one leading slash.
    /// </summary>
    public static string PagePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var bare = path.Split('?', '#')[0];
        return "/" + bare.TrimStart('/');
    }

    private static string OneLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var words = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            // A typed list's markers (design 8.2, "A text's layout") say nothing in one line.
            if (line.Length > 1 && line[0] is '-' or '*' or '•' or '–' && line[1] == ' ')
            {
                line = line[2..].Trim();
            }

            foreach (var word in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (words.Length > 0)
                {
                    words.Append(' ');
                }

                words.Append(word);
            }
        }

        return words.ToString();
    }
}
