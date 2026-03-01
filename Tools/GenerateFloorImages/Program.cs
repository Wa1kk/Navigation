using SkiaSharp;
using Svg.Skia;

// ─────────────────────────────────────────────────────────────────────────────
// Генератор предрастеризованных WebP для IndoorNav.
// Запуск: dotnet run (из папки Tools/GenerateFloorImages)
// Читает SVG из  ../../IndoorNav/Resources/Raw/SvgFloors/
// Пишет  WebP в  ../../IndoorNav/Resources/Raw/FloorImages/
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// Генератор предрастеризованных WebP для IndoorNav.
// Запуск: dotnet run --project ... -- <путь_к_IndoorNav>
// Или без аргументов — ищет IndoorNav относительно папки проекта.
// ─────────────────────────────────────────────────────────────────────────────

const int MaxDim     = 3072;
const int WebpQuality = 85;

// Определяем корень IndoorNav
string indoorNavRoot;
if (args.Length > 0)
{
    indoorNavRoot = args[0];
}
else
{
    // Ищем от папки проекта (поднимаемся от BaseDirectory до test\Tools\, затем идём в IndoorNav)
    var dir = AppContext.BaseDirectory;
    // Поднимаемся вверх пока не найдём папку IndoorNav рядом
    while (!string.IsNullOrEmpty(dir))
    {
        var candidate = Path.Combine(dir, "IndoorNav");
        if (Directory.Exists(Path.Combine(candidate, "Resources", "Raw", "SvgFloors")))
        {
            indoorNavRoot = candidate;
            goto found;
        }
        var parent = Path.GetDirectoryName(dir);
        if (parent == dir) break;
        dir = parent!;
    }
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Не удалось найти папку IndoorNav автоматически.");
    Console.WriteLine("Передайте путь явно: dotnet run --project ... -- C:\\path\\to\\IndoorNav");
    Console.ResetColor();
    return 1;
    found:;
}

var svgRoot = Path.Combine(indoorNavRoot, "Resources", "Raw", "SvgFloors");
var outRoot = Path.Combine(indoorNavRoot, "Resources", "Raw", "FloorImages");

Console.WriteLine($"SVG source : {svgRoot}");
Console.WriteLine($"WebP output: {outRoot}");
Console.WriteLine();

if (!Directory.Exists(svgRoot))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Папка не найдена: {svgRoot}");
    Console.ResetColor();
    return 1;
}

Directory.CreateDirectory(outRoot);

var files = Directory.GetFiles(svgRoot, "*.svg", SearchOption.AllDirectories);
if (files.Length == 0)
{
    Console.WriteLine("Нет SVG-файлов.");
    return 0;
}

int ok = 0, skip = 0, fail = 0;

foreach (var svgFile in files.OrderBy(f => f))
{
    // relativePath вида "BuildingA/floor1.svg"
    var rel      = Path.GetRelativePath(svgRoot, svgFile).Replace('\\', '/');
    var cacheKey = rel.Replace('/', '_');   // "BuildingA_floor1.svg"

    var webpOut = Path.Combine(outRoot, cacheKey + ".webp");
    var metaOut = Path.Combine(outRoot, cacheKey + ".webp.meta");

    if (File.Exists(webpOut) && File.Exists(metaOut))
    {
        Console.WriteLine($"  [пропуск]  {rel}  (уже есть)");
        skip++;
        continue;
    }

    Console.Write($"  [обработка] {rel} ...");
    var sw = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        var svg = new SKSvg();
        svg.Load(svgFile);
        if (svg.Picture == null)
            throw new Exception("SKSvg.Picture == null");

        var bounds = svg.Picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new Exception($"Некорректные размеры: {bounds}");

        float ratio = Math.Max(bounds.Width, bounds.Height);
        float s     = ratio <= MaxDim ? 1f : MaxDim / ratio;
        int bw = Math.Max(1, (int)(bounds.Width  * s));
        int bh = Math.Max(1, (int)(bounds.Height * s));

        using var bitmap  = new SKBitmap(bw, bh, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bCanvas = new SKCanvas(bitmap);
        bCanvas.Clear(new SKColor(248, 248, 248));
        float sx = bw / bounds.Width;
        float sy = bh / bounds.Height;
        bCanvas.SetMatrix(SKMatrix.CreateScaleTranslation(
            sx, sy, -bounds.Left * sx, -bounds.Top * sy));
        bCanvas.DrawPicture(svg.Picture);

        using var fs = File.Create(webpOut);
        bitmap.Encode(fs, SKEncodedImageFormat.Webp, WebpQuality);

        var meta = FormattableString.Invariant($"{bounds.Width},{bounds.Height}");
        File.WriteAllText(metaOut, meta);

        sw.Stop();
        var sizeMb = new FileInfo(webpOut).Length / 1024f / 1024f;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($" готово  {sw.Elapsed.TotalSeconds:F1}s  →  {sizeMb:F2} MB");
        Console.ResetColor();
        ok++;
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($" ОШИБКА: {ex.Message}");
        Console.ResetColor();
        // Удаляем неполные файлы
        try { File.Delete(webpOut); } catch { }
        try { File.Delete(metaOut); } catch { }
        fail++;
    }
}

Console.WriteLine();
Console.WriteLine($"Готово: {ok} создано, {skip} пропущено, {fail} ошибок.");
Console.WriteLine($"Папка: {outRoot}");

return fail > 0 ? 2 : 0;
