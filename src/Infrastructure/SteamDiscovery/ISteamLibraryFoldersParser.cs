using System.Text;
using ValveFormatParser;

namespace SteamDiscovery;

public interface ISteamLibraryFoldersParser
{
    IReadOnlyList<string> Parse(string content);

    IReadOnlyList<string> Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var content = new StringBuilder();
        var chunk = new char[8192];
        int read;
        while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (content.Length + read > ValveTextVdfParser.MaxDocumentCharacters)
            {
                throw new InvalidDataException("Library folders document is too large.");
            }
            content.Append(chunk, 0, read);
        }
        return Parse(content.ToString());
    }
}
