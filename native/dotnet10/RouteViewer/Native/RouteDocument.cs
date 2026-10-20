using System;
using System.IO;
using System.Linq;
using System.Text;
namespace RouteViewer;

// Keep the source encoding and BOM, and refuse to overwrite external edits.
internal sealed class RouteDocument
{
    internal string Path { get; private set; }
    internal string Text { get; private set; }
    private byte[] original, preamble;
    private Encoding encoding;
    private bool crlf;
    internal static RouteDocument Open(string path, Encoding fallback)
    {
        path = File.ResolveLinkTarget(path, true)?.FullName ?? System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), fallback, true);
        string text = reader.ReadToEnd();
        var encoding = (Encoding)reader.CurrentEncoding.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        byte[] bom = encoding.GetPreamble();
        byte[] preamble=bytes.AsSpan().StartsWith(bom)?bom:Array.Empty<byte>();
        text=encoding.GetString(bytes,preamble.Length,bytes.Length-preamble.Length);
        return new RouteDocument { Path=path, Text=text, original=bytes, encoding=encoding,
            preamble=preamble, crlf=text.Contains("\r\n") && !text.Replace("\r\n", "").Contains('\n') };
    }
    internal void Save(string text)
    {
        if(crlf)text=text.Replace("\r\n","\n").Replace("\n","\r\n");
        byte[] bytes = preamble.Concat(encoding.GetBytes(text)).ToArray();
        if (!File.ReadAllBytes(Path).AsSpan().SequenceEqual(original))
            throw new IOException("O arquivo foi alterado por outro programa. Suas edições foram mantidas no editor. Resolva a diferença antes de salvar.");
        string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), ".route-editor-"+Guid.NewGuid().ToString("N"));
        try {
            File.WriteAllBytes(temporary,bytes);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary,File.GetUnixFileMode(Path));
            File.Move(temporary,Path,true);
            original=bytes; Text=text;
        } finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
