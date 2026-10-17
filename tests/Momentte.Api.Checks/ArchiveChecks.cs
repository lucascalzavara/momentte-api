using System.IO.Compression;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Domain.Events;
using Momentte.Api.Infrastructure.Implementations;
using Momentte.Api.Infrastructure.Interfaces;

internal static class ArchiveChecks
{
    public static async Task Run()
    {
        var clock = new Clock();
        var tickets = new EventArchiveTickets(clock);
        var owner = Guid.NewGuid(); var eventId = Guid.NewGuid();
        var token = tickets.Issue(owner, eventId);
        Reject(() => tickets.Consume(Guid.NewGuid(), token));
        Reject(() => tickets.Consume(eventId, token[..^1] + (token[^1] == 'A' ? 'B' : 'A')));
        Require(tickets.Consume(eventId, token) == owner, "Ticket vinculado ao evento e ao organizador");
        Reject(() => tickets.Consume(eventId, token));
        var expired = tickets.Issue(owner, eventId);
        clock.Now += TimeSpan.FromMinutes(2);
        Reject(() => tickets.Consume(eventId, expired));
        var replaced = tickets.Issue(owner, eventId);
        var current = tickets.Issue(owner, eventId);
        Reject(() => tickets.Consume(eventId, replaced));
        Require(tickets.Consume(eventId, current) == owner, "Novo ticket substitui o anterior; expiração e uso único verificados");

        var store = new MemoryMedia();
        var images = Enumerable.Range(0, 205).Select(index => new MediaRecord {
            Id = Guid.NewGuid(), EventId = eventId, RelativePath = index.ToString(),
            ContentType = new[] { "image/jpeg", "image/png", "image/webp" }[index % 3],
            Status = "approved"
        }).ToList();
        foreach (var image in images) store.Files[image.RelativePath!] = [1, 3, 5, 7];
        images.Add(new() { Id = Guid.NewGuid(), RelativePath = "rejected", ContentType = "image/png", Status = "rejected" });
        images.Add(new() { Id = Guid.NewGuid(), RelativePath = "pending", ContentType = "image/png", Status = "pending" });
        images.Add(new() { Id = Guid.NewGuid(), Text = "Somente texto", Status = "approved" });
        await using var bytes = new MemoryStream();
        await using var output = new AsyncOnlyOutput(bytes);
        await EventImageArchive.Write(output, images, store, CancellationToken.None);
        bytes.Position = 0;
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read, leaveOpen: true);
        Require(zip.Entries.Count == 205, "ZIP inclui mais de 200 imagens e exclui pendentes, rejeitados e textos");
        foreach (var entry in zip.Entries)
        {
            Require(new[] { ".jpg", ".png", ".webp" }.Contains(Path.GetExtension(entry.Name)), "Extensão da imagem no ZIP", quiet: true);
            await using var source = entry.Open(); using var copy = new MemoryStream();
            await source.CopyToAsync(copy);
            Require(copy.ToArray().SequenceEqual(new byte[] { 1, 3, 5, 7 }), "Conteúdo original preservado", quiet: true);
        }
        Console.WriteLine("OK: ZIP válido em saída não pesquisável, somente escrita assíncrona, bytes originais e extensões corretas.");
    }
    private static void Require(bool condition, string message, bool quiet = false) { if (!condition) throw new Exception(message); if (!quiet) Console.WriteLine("OK: " + message); }
    private static void Reject(Action action) { try { action(); } catch (BillingRuleException error) when (error.Status == 401) { return; } throw new Exception("Ticket inválido aceito."); }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class MemoryMedia : IMediaStore
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public Stream Read(string path) => new MemoryStream(Files[path]);
        public Task Delete(string path) => throw new NotSupportedException();
        public Task<(string Path, long Bytes, string ContentType)> Put(Stream input, Guid id, long maximumBytes, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class AsyncOnlyOutput(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new InvalidOperationException("Flush síncrono proibido.");
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Escrita síncrona proibida.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Escrita síncrona proibida.");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.WriteAsync(buffer, offset, count, ct);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => inner.WriteAsync(buffer, ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
