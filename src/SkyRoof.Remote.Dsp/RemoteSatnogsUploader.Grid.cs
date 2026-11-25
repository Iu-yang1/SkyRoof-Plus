namespace SkyRoof.Remote.Dsp;

internal sealed partial class RemoteSatnogsUploader
{
    internal static bool TryGrid(string grid, out double lat, out double lon)
    {
        lat = -90; lon = -180;
        if (grid.Length is not (4 or 6 or 8)) return false;
        if (!Letter(grid[0], 'R') || !Letter(grid[1], 'R') || !Digit(grid[2]) || !Digit(grid[3])) return false;
        lon += 20 * (grid[0] - 'A') + 2 * (grid[2] - '0');
        lat += 10 * (grid[1] - 'A') + grid[3] - '0';
        double dx = 2, dy = 1;
        if (grid.Length >= 6)
        {
            if (!Letter(grid[4], 'X') || !Letter(grid[5], 'X')) return false;
            dx /= 24; dy /= 24;
            lon += dx * (grid[4] - 'A'); lat += dy * (grid[5] - 'A');
        }
        if (grid.Length == 8)
        {
            if (!Digit(grid[6]) || !Digit(grid[7])) return false;
            dx /= 10; dy /= 10;
            lon += dx * (grid[6] - '0'); lat += dy * (grid[7] - '0');
        }
        lon += dx / 2; lat += dy / 2;
        return true;
    }

    private static bool Digit(char value) => value is >= '0' and <= '9';
    private static bool Letter(char value, char maximum) => value >= 'A' && value <= maximum;
}
