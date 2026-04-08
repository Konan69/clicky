using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Clicky.App.Models;

namespace Clicky.App.Services.ScreenCapture;

public sealed class WindowsScreenCaptureService
{
    public Task<IReadOnlyList<CapturedScreen>> CaptureAllDisplaysAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CapturedScreen>>(() =>
        {
            var capturedScreens = new List<CapturedScreen>();
            var orderedScreens = Screen.AllScreens
                .OrderBy(screen => screen.Bounds.Left)
                .ThenBy(screen => screen.Bounds.Top)
                .ToArray();

            for (var index = 0; index < orderedScreens.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var screen = orderedScreens[index];
                using var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
                using var graphics = Graphics.FromImage(bitmap);

                graphics.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);

                using var memoryStream = new MemoryStream();
                bitmap.Save(memoryStream, ImageFormat.Png);

                var screenLabel = $"Screen {index + 1} ({screen.Bounds.Width}x{screen.Bounds.Height})";
                capturedScreens.Add(new CapturedScreen(memoryStream.ToArray(), screenLabel, screen.Bounds));
            }

            return capturedScreens;
        }, cancellationToken);
    }
}

