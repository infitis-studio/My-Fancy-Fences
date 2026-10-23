using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace My_Fancy_Fences;

/// <summary>
/// Icons for items of the native tray menu. Put in the menu's icon column they let the item text line up
/// with all the other items, instead of being pushed right by a symbol inside the text.
/// </summary>
internal static class MenuIcon
{
    private const uint MiimBitmap = 0x00000080;

    /// <summary>
    /// Renders <paramref name="glyph"/> (Segoe UI Symbol) into a 16 px bitmap and attaches it to the item.
    /// The bitmap is not owned by the menu: free it with <see cref="Delete"/> after the menu is destroyed.
    /// </summary>
    public static IntPtr Attach(IntPtr menu, uint commandId, string glyph)
    {
        var size = (int)Math.Round(16 * MonitorLayoutService.SystemScale);
        var bitmap = CreateGlyphBitmap(glyph, size);
        if (bitmap == IntPtr.Zero)
            return IntPtr.Zero;

        var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = MiimBitmap, BitmapItem = bitmap };
        if (SetMenuItemInfo(menu, commandId, false, ref info))
            return bitmap;

        DeleteObject(bitmap);
        return IntPtr.Zero;
    }

    public static void Delete(IntPtr bitmap)
    {
        if (bitmap != IntPtr.Zero)
            DeleteObject(bitmap);
    }

    private static IntPtr CreateGlyphBitmap(string glyph, int size)
    {
        // The glyph is rendered by WPF and copied into a top-down 32-bit bitmap (premultiplied alpha).
        var text = new FormattedText(
            glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Symbol"),
            size * 0.82,
            new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)),
            1.0);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var pixels = new byte[size * size * 4];
        target.CopyPixels(pixels, size * 4, 0);

        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = size,
            Height = -size,
            Planes = 1,
            BitCount = 32
        };
        var handle = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (handle == IntPtr.Zero || bits == IntPtr.Zero)
            return IntPtr.Zero;

        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint Size;
        public uint Mask;
        public uint Type;
        public uint State;
        public uint Id;
        public IntPtr SubMenu;
        public IntPtr BitmapChecked;
        public IntPtr BitmapUnchecked;
        public UIntPtr ItemData;
        public IntPtr TypeData;
        public uint Cch;
        public IntPtr BitmapItem;
    }

    [DllImport("user32.dll", EntryPoint = "SetMenuItemInfoW")]
    private static extern bool SetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MenuItemInfo info);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
