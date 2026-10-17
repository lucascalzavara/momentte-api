using System.IO.Compression;
using Momentte.Api.Domain.Events;
using Momentte.Api.Infrastructure.Interfaces;

namespace Momentte.Api.Infrastructure.Implementations;

public static class EventImageArchive
{
    public static async Task Write(Stream destination, IEnumerable<MediaRecord> images, IMediaStore store, CancellationToken ct)
    {
        using var sink = new AsyncZipSink(destination);
        await using (var archive = await ZipArchive.CreateAsync(sink, ZipArchiveMode.Create, leaveOpen: true,
            entryNameEncoding: null, cancellationToken: ct))
        {
            foreach (var image in images)
            {
                ct.ThrowIfCancellationRequested();
                if (!image.HasImage || image.Status != "approved") continue;
                var extension = image.ContentType switch
                {
                    "image/jpeg" => ".jpg", "image/png" => ".png", "image/webp" => ".webp",
                    _ => throw new InvalidDataException("Formato de imagem não suportado no acervo.")
                };
                // JPEG/PNG/WebP are already compressed; avoid spending CPU recompressing them.
                var entry = archive.CreateEntry(image.Id.ToString("N") + extension, CompressionLevel.NoCompression);
                await using var source = store.Read(image.RelativePath!);
                await using var output = await entry.OpenAsync(ct);
                await source.CopyToAsync(output, ct);
            }
        }
        await sink.FlushAsync(ct);
    }

    // ZipArchive can write entry descriptors synchronously even with OpenAsync.
    // Buffer those small metadata writes; image bodies and HTTP output remain asynchronous.
    private sealed class AsyncZipSink(Stream destination) : Stream
    {
        private readonly MemoryStream metadata = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => metadata.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => metadata.Write(buffer);
        public override void Flush() { }
        private async Task FlushMetadata(CancellationToken ct)
        {
            if (metadata.Length == 0) return;
            await destination.WriteAsync(metadata.GetBuffer().AsMemory(0, checked((int)metadata.Length)), ct);
            metadata.SetLength(0); metadata.Position = 0;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { await FlushMetadata(ct); await destination.WriteAsync(buffer, ct); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override async Task FlushAsync(CancellationToken ct) { await FlushMetadata(ct); await destination.FlushAsync(ct); }
        protected override void Dispose(bool disposing) { if (disposing) metadata.Dispose(); base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
