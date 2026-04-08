using System.Drawing;

namespace Clicky.App.Models;

public sealed record CapturedScreen(byte[] ImageBytes, string Label, Rectangle Bounds);

