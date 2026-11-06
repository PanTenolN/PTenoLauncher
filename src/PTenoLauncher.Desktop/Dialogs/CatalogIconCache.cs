using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PTenoLauncher.Core;

namespace PTenoLauncher.Desktop.Dialogs;

internal sealed class CatalogIconCache(Func<CatalogSearchHit, CancellationToken, Task<byte[]?>> load) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, Task<Bitmap?>> icons = new(StringComparer.Ordinal);
    private bool disposed;

    public async Task ShowAsync(Image image)
    {
        image.Source = null;
        if (disposed || image.DataContext is not CatalogSearchHit { IconUrl: { } url } hit) return;
        if (!icons.TryGetValue(url, out var pending))
        {
            if (icons.Count >= 160) return;
            icons.Add(url, pending = LoadAsync(hit, lifetime.Token));
        }
        var bitmap = await pending;
        if (!disposed && Equals(image.DataContext, hit)) image.Source = bitmap;
    }

    private async Task<Bitmap?> LoadAsync(CatalogSearchHit hit, CancellationToken ct)
    {
        try
        {
            var bytes = await load(hit, ct);
            if (disposed || bytes is null) return null;
            using var data = SkiaSharp.SKData.CreateCopy(bytes);
            using var codec = SkiaSharp.SKCodec.Create(data);
            if (codec is null || codec.Info.Width is < 1 or > 512 || codec.Info.Height is < 1 or > 512) return null;
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, 64);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or ArgumentException or InvalidOperationException)
        { return null; }
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        foreach (var icon in icons.Values)
            if (icon.IsCompletedSuccessfully) icon.Result?.Dispose();
        lifetime.Dispose();
    }
}
