using System.Threading.Channels;

namespace src.utils;

internal sealed class BufferedFileLog : IDisposable
{
    private readonly Channel<string> lines = Channel.CreateBounded<string>(new BoundedChannelOptions(16384)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true
    });
    private readonly Task worker;
    private long dropped;

    public BufferedFileLog(string path)
    {
        worker = Task.Run(() => WriteLines(path));
    }

    public void Write(string line)
    {
        if (!lines.Writer.TryWrite(line))
            Interlocked.Increment(ref dropped);
    }

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

                try
                {
                    if (writer != null)
                    {
                        long skipped = Interlocked.Exchange(ref dropped, 0);
                        if (skipped > 0) writer.WriteLine($"[jRandomSkills] Log queue full, skipped {skipped} records.");
                        writer.Flush();
                    }
                }
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
