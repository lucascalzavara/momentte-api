using Momentte.Api.Domain.Billing;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class LocalMediaStore(IWebHostEnvironment env, IConfiguration config) : IMediaStore
{
    private string Root => Path.GetFullPath(config["Media:RootPath"] ?? Path.Combine(env.ContentRootPath, "App_Data", "media"));
    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(Root, path));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new BillingRuleException(400, "Arquivo inválido.");
        return full;
    }
    public async Task<(string Path, long Bytes, string ContentType)> Put(Stream input, Guid eventId, long maximumBytes, CancellationToken ct)
    {
        var header = new byte[12];
        var read = 0;
        while (read < header.Length) { var count = await input.ReadAsync(header.AsMemory(read), ct); if (count == 0) break; read += count; }
        var mime = read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff ? "image/jpeg" :
            read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ? "image/png" :
            read == 12 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP" ? "image/webp" : null;
        if (mime is null) throw new BillingRuleException(400, "Envie uma imagem JPEG, PNG ou WebP.");
        var relative = Path.Combine(eventId.ToString("N"), Guid.NewGuid().ToString("N"));
        var destination = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        long bytes = read;
        try
        {
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            await output.WriteAsync(header.AsMemory(0, read), ct);
            var buffer = new byte[65536]; int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            {
                bytes += count;
                if (bytes > maximumBytes) throw new BillingRuleException(409, "Limite de armazenamento do evento excedido.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            if (bytes > maximumBytes) throw new BillingRuleException(409, "Limite de armazenamento do evento excedido.");
            return (relative, bytes, mime);
        }
        catch { File.Delete(destination); throw; }
    }
    public Stream Read(string relativePath) => new FileStream(Resolve(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read);
    public Task Delete(string relativePath) { File.Delete(Resolve(relativePath)); return Task.CompletedTask; }
}
