using XenoAtom.Terminal;
using XenoAtom.Terminal.Graphics;

namespace HelloImage;

public static class Program
{
    public static async Task Main()
    {
        using var _session = Terminal.Open(options: new TerminalOptions
        {
            PreferUtf8Output = true,
            Prefer7BitC1 = true,
        });

        Terminal.WriteMarkupLine("[bold green]HelloImage[/]");
        Terminal.WriteMarkupLine("Displays [cyan]snow_photo.jpg[/] with terminal graphics.");
        Terminal.WriteLine($"Detected graphics protocol: {Terminal.Graphics.Capabilities.PreferredProtocol} ({Terminal.Graphics.Capabilities.SupportState})");
        Terminal.WriteMarkupLine("[dim]Override detection with XENOATOM_TERMINAL_GRAPHICS=kitty|iterm2|sixel.[/]");
        Terminal.WriteLine();

        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "snow_photo.jpg");
        if (!File.Exists(imagePath))
        {
            Terminal.WriteLine($"Sample image not found: {imagePath}");
            return;
        }

        try
        {
            var cellSize = ResolveImageCellSize();
            var pixelSize = new TerminalImageSize(cellSize.Width * 8, cellSize.Height * 16);
            var encoded = await TerminalGraphics.WriteImageAsync(
                TerminalImageSource.FromFile(imagePath),
                new TerminalImageWriteOptions
                {
                    PixelSize = pixelSize,
                    CellSize = cellSize,
                    ReserveCellArea = true,
                    FallbackText = "No terminal graphics protocol detected.\n",
                    SixelOptions = new TerminalSixelEncoderOptions
                    {
                        PaletteMode = TerminalSixelPaletteMode.FixedRgb332,
                    },
                });

            Terminal.WriteLine();
            if (encoded is not null)
            {
                Terminal.WriteLine($"Displayed with {encoded.Protocol}: {encoded.PixelWidth}x{encoded.PixelHeight}px, {encoded.CellSize.Width}x{encoded.CellSize.Height} cells.");
            }
        }
        catch (Exception ex)
        {
            Terminal.WriteMarkupLine("[red]Image rendering failed.[/]");
            Terminal.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        }

        if (Terminal.IsInteractive)
        {
            Terminal.WriteLine();
            Terminal.WriteLine("Press Enter to exit.");
            _ = Console.ReadLine();
        }
    }

    private static TerminalImageSize ResolveImageCellSize()
    {
        var columns = Terminal.Size.Columns > 0 ? Terminal.Size.Columns : 80;
        var width = Math.Min(48, Math.Max(20, columns - 4));
        var height = Math.Min(16, Math.Max(8, width / 3));
        return new TerminalImageSize(width, height);
    }
}
