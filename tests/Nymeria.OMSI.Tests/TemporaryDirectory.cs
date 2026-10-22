using System.Text;

namespace Nymeria.OMSI.Tests;

/// <summary>
/// A temporary folder for synthetic, independently authored test files.
/// </summary>
/// <remarks>
/// These fixtures are minimal strings written by the tests themselves. They are
/// not OMSI content and are not represented as evidence of real map
/// compatibility.
/// </remarks>
internal sealed class TemporaryDirectory : IDisposable
{
    private const string Prefix = "nymeria-omsi-tests-";

    internal TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    internal string WriteUtf8(string fileName, string content, bool byteOrderMark = false)
    {
        string filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(
            filePath,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: byteOrderMark));
        return filePath;
    }

    internal string WriteUtf16Le(string fileName, string content)
    {
        string filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(
            filePath,
            content,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        return filePath;
    }

    internal string WriteRaw(string fileName, params byte[] bytes)
    {
        string filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }

    internal string CreateEmptyFile(string fileName)
    {
        string filePath = System.IO.Path.Combine(Path, fileName);
        using (File.Create(filePath))
        {
        }

        return filePath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is harmless; never fail a test on cleanup.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
