using System.Threading.Channels;

namespace src.utils;

internal sealed class BufferedFileLog : IDisposable
{
    private readonly Channel<string> lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true
    });
    private readonly Task worker;

    public BufferedFileLog(string path)
    {
        worker = Task.Run(() => WriteLines(path));
    }

    public void Write(string line) => lines.Writer.TryWrite(line);

    public void Dispose()
    {
        lines.Writer.TryComplete();
        worker.GetAwaiter().GetResult();
    }

    private async Task WriteLines(string path)
    {
        StreamWriter? writer = null;
        try
        {
            while (await lines.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (lines.Reader.TryRead(out string? line))
                {
                    try
                    {
                        if (writer == null)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            writer = new StreamWriter(path, append: true, System.Text.Encoding.UTF8);
                        }

                        writer.WriteLine(line);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Close(writer);
                        writer = null;
                    }
                }

                try { writer?.Flush(); }
                catch (IOException) { Close(writer); writer = null; }
            }
        }
        finally
        {
            Close(writer);
        }
    }

    private static void Close(StreamWriter? writer)
    {
        try { writer?.Dispose(); }
        catch (IOException) { }
    }
}
