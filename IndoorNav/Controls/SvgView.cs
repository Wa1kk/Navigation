using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using Svg.Skia;
using System.Collections.Concurrent;
using System.IO;
using IndoorNav.Models;
using Microsoft.Maui.Storage;

namespace IndoorNav.Controls;

/// <summary>
/// SKCanvasView: рендер SVG + оверлей навигационного графа + взаимодействие в режиме администратора.
/// </summary>
public class SvgView : SKCanvasView
{
    // ===== Bindable Properties =====

    public static readonly BindableProperty NodesProperty =
        BindableProperty.Create(nameof(Nodes), typeof(IList<NavNode>), typeof(SvgView),
            null, propertyChanged: (b, old, nw) =>
            {
                var v = (SvgView)b;
                if (old is System.Collections.Specialized.INotifyCollectionChanged oc)
                    oc.CollectionChanged -= v.OnOverlayCollectionChanged;
                if (nw is System.Collections.Specialized.INotifyCollectionChanged nc)
                    nc.CollectionChanged += v.OnOverlayCollectionChanged;
                v.InvalidateSurface();
            });

    public static readonly BindableProperty EdgesProperty =
        BindableProperty.Create(nameof(Edges), typeof(IList<NavEdge>), typeof(SvgView),
            null, propertyChanged: (b, old, nw) =>
            {
                var v = (SvgView)b;
                if (old is System.Collections.Specialized.INotifyCollectionChanged oc)
                    oc.CollectionChanged -= v.OnOverlayCollectionChanged;
                if (nw is System.Collections.Specialized.INotifyCollectionChanged nc)
                    nc.CollectionChanged += v.OnOverlayCollectionChanged;
                v.InvalidateSurface();
            });

    public static readonly BindableProperty RouteNodesProperty =
        BindableProperty.Create(nameof(RouteNodes), typeof(IList<NavNode>), typeof(SvgView),
            null, propertyChanged: (b, old, nw) =>
            {
                var v = (SvgView)b;
                if (old is System.Collections.Specialized.INotifyCollectionChanged oc)
                    oc.CollectionChanged -= v.OnOverlayCollectionChanged;
                if (nw is System.Collections.Specialized.INotifyCollectionChanged nc)
                    nc.CollectionChanged += v.OnOverlayCollectionChanged;
                v.UpdateRouteAnimation();
                v.InvalidateSurface();
            });

    /// <summary>
    /// Indices in <see cref="RouteNodes"/> where the path must be broken (MoveTo instead of LineTo)
    /// because the route crosses another floor between those two on-floor nodes.
    /// </summary>
    public static readonly BindableProperty RouteNodeBreaksProperty =
        BindableProperty.Create(nameof(RouteNodeBreaks), typeof(IEnumerable<int>), typeof(SvgView),
            null, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    public IEnumerable<int>? RouteNodeBreaks
    {
        get => (IEnumerable<int>?)GetValue(RouteNodeBreaksProperty);
        set => SetValue(RouteNodeBreaksProperty, value);
    }

    public static readonly BindableProperty SelectedNodeProperty =
        BindableProperty.Create(nameof(SelectedNode), typeof(NavNode), typeof(SvgView),
            null, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    /// <summary>Набор узлов, выделенных в режиме мультивыбора (рисуется бирюзовое кольцо).</summary>
    public static readonly BindableProperty MultiSelectedNodesProperty =
        BindableProperty.Create(nameof(MultiSelectedNodes), typeof(IEnumerable<NavNode>), typeof(SvgView),
            null, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    public IEnumerable<NavNode>? MultiSelectedNodes
    {
        get => (IEnumerable<NavNode>?)GetValue(MultiSelectedNodesProperty);
        set => SetValue(MultiSelectedNodesProperty, value);
    }

    /// <summary>Id узла, чьи рёбра нужно подсветить в режиме администратора.</summary>
    public static readonly BindableProperty HighlightedNodeIdProperty =
        BindableProperty.Create(nameof(HighlightedNodeId), typeof(string), typeof(SvgView),
            null, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    public static readonly BindableProperty IsAdminModeProperty =
        BindableProperty.Create(nameof(IsAdminMode), typeof(bool), typeof(SvgView), false);

    public static readonly BindableProperty ShowGraphProperty =
        BindableProperty.Create(nameof(ShowGraph), typeof(bool), typeof(SvgView),
            true, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    /// <summary>Когда true — узлы огнетушителей видны на карте (только в режиме ЧС).</summary>
    public static readonly BindableProperty ShowFireExtinguishersProperty =
        BindableProperty.Create(nameof(ShowFireExtinguishers), typeof(bool), typeof(SvgView),
            false, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    /// <summary>ID узлов-QR-якорей, которые должны быть видимы пользователю (активная стартовая точка из QR). Остальные QR-якори скрыты.</summary>
    public static readonly BindableProperty QrAnchorNodeIdsProperty =
        BindableProperty.Create(nameof(QrAnchorNodeIds), typeof(IEnumerable<string>), typeof(SvgView),
            null, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    public IEnumerable<string>? QrAnchorNodeIds
    {
        get => (IEnumerable<string>?)GetValue(QrAnchorNodeIdsProperty);
        set => SetValue(QrAnchorNodeIdsProperty, value);
    }

    public bool ShowGraph
    {
        get => (bool)GetValue(ShowGraphProperty);
        set => SetValue(ShowGraphProperty, value);
    }

    public bool ShowFireExtinguishers
    {
        get => (bool)GetValue(ShowFireExtinguishersProperty);
        set => SetValue(ShowFireExtinguishersProperty, value);
    }

    public bool IsDragMode
    {
        get => (bool)GetValue(IsDragModeProperty);
        set => SetValue(IsDragModeProperty, value);
    }

    public static readonly BindableProperty CurrentFloorNumberProperty =
        BindableProperty.Create(nameof(CurrentFloorNumber), typeof(int), typeof(SvgView), 0,
            propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());
    public int CurrentFloorNumber
    {
        get => (int)GetValue(CurrentFloorNumberProperty);
        set => SetValue(CurrentFloorNumberProperty, value);
    }

    public static readonly BindableProperty HighlightBoundaryNodeProperty =
        BindableProperty.Create(nameof(HighlightBoundaryNode), typeof(NavNode), typeof(SvgView), null,
            propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());
    public NavNode? HighlightBoundaryNode
    {
        get => (NavNode?)GetValue(HighlightBoundaryNodeProperty);
        set => SetValue(HighlightBoundaryNodeProperty, value);
    }

    public static readonly BindableProperty HighlightStartNodeProperty =
        BindableProperty.Create(nameof(HighlightStartNode), typeof(NavNode), typeof(SvgView), null,
            propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());
    public NavNode? HighlightStartNode
    {
        get => (NavNode?)GetValue(HighlightStartNodeProperty);
        set => SetValue(HighlightStartNodeProperty, value);
    }

    public static readonly BindableProperty BoundaryPreviewProperty =
        BindableProperty.Create(nameof(BoundaryPreview), typeof(IList<SKPoint>), typeof(SvgView), null,
            propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());
    public IList<SKPoint>? BoundaryPreview
    {
        get => (IList<SKPoint>?)GetValue(BoundaryPreviewProperty);
        set => SetValue(BoundaryPreviewProperty, value);
    }

    /// <summary>IDs of nodes the user has marked as impassable (ЧС blocked-route feature).</summary>
    public static readonly BindableProperty BlockedNodeIdsProperty =
        BindableProperty.Create(nameof(BlockedNodeIds), typeof(IEnumerable<string>), typeof(SvgView),
            null, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    public IEnumerable<string>? BlockedNodeIds
    {
        get => (IEnumerable<string>?)GetValue(BlockedNodeIdsProperty);
        set => SetValue(BlockedNodeIdsProperty, value);
    }

    /// <summary>When true, intermediate waypoint nodes on the route are rendered so the user can tap them (blocking mode).</summary>
    public static readonly BindableProperty ShowRouteWaypointsProperty =
        BindableProperty.Create(nameof(ShowRouteWaypoints), typeof(bool), typeof(SvgView),
            false, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    public bool ShowRouteWaypoints
    {
        get => (bool)GetValue(ShowRouteWaypointsProperty);
        set => SetValue(ShowRouteWaypointsProperty, value);
    }

    /// <summary>Когда true — зажатие на узле позволяет его перетащить (режим редактирования положения).</summary>
    public static readonly BindableProperty IsDragModeProperty =
        BindableProperty.Create(nameof(IsDragMode), typeof(bool), typeof(SvgView), false);

    public IList<NavNode>? Nodes
    {
        get => (IList<NavNode>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }
    public IList<NavEdge>? Edges
    {
        get => (IList<NavEdge>?)GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }
    public IList<NavNode>? RouteNodes
    {
        get => (IList<NavNode>?)GetValue(RouteNodesProperty);
        set => SetValue(RouteNodesProperty, value);
    }
    public NavNode? SelectedNode
    {
        get => (NavNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }
    public string? HighlightedNodeId
    {
        get => (string?)GetValue(HighlightedNodeIdProperty);
        set => SetValue(HighlightedNodeIdProperty, value);
    }
    public bool IsAdminMode
    {
        get => (bool)GetValue(IsAdminModeProperty);
        set => SetValue(IsAdminModeProperty, value);
    }

    /// <summary>
    /// Zoom to a node (center it on the canvas and set zoom).
    /// </summary>
    public void ZoomToNode(NavNode node, float zoomFactor = 2.5f)
    {
        if (node == null || _canvasW <= 0 || _canvasH <= 0) return;

        SKPoint canvasCenter = new(_canvasW / 2f, _canvasH / 2f);
        SKPoint nodePos = new(node.X, node.Y);

        float currentScale = MatrixScale();
        float targetScale = currentScale * zoomFactor;

        if (targetScale > MaxZoom) targetScale = MaxZoom;
        if (targetScale < MinZoom) targetScale = MinZoom;

        float zoomCoeff = targetScale / currentScale;

        _matrix = SKMatrix.CreateScaleTranslation(
            targetScale, targetScale,
            canvasCenter.X - nodePos.X * targetScale,
            canvasCenter.Y - nodePos.Y * targetScale
        );

        InvalidateSurface();
    }

    /// <summary>
    /// Apply a small rotation around `pivot` (clamped by `MaxRotationDeg`).
    /// </summary>
    private void ApplyRotation(float deltaDeg, SKPoint pivot)
    {
        if (MathF.Abs(deltaDeg) < 0.05f) return;

        float clamped = deltaDeg;
        if (_totalRotationDeg + deltaDeg >  MaxRotationDeg) clamped =  MaxRotationDeg - _totalRotationDeg;
        if (_totalRotationDeg + deltaDeg < -MaxRotationDeg) clamped = -MaxRotationDeg - _totalRotationDeg;
        if (MathF.Abs(clamped) < 0.01f) return;

        _totalRotationDeg += clamped;
        _matrix = _matrix.PostConcat(SKMatrix.CreateRotationDegrees(clamped, pivot.X, pivot.Y));
        ClampPan();
        _panDirty = true;
    }

    /// <summary>
    /// Не даёт карте полностью уйти за пределы экрана при панировании/зуме/вращении.
    /// Гарантирует, что не менее <c>PanMargin</c> пикселей контента всегда остаётся видимым.
    /// </summary>
    private void ClampPan()
    {
        if (_canvasW <= 0 || _canvasH <= 0) return;

        SKRect content;
        if (_svgBounds.Width > 0)
            content = _svgBounds;
        else if (_picture != null)
            content = _picture.CullRect;
        else
            return;

        // Вычисляем экранный ограничивающий прямоугольник карты (MapRect учитывает вращение)
        var r = _matrix.MapRect(content);

        // Минимальный «выступ» карты за края экрана (пиксели)
        const float margin = 80f;

        float dx = 0f, dy = 0f;

        if (r.Right  < margin)             dx =  margin - r.Right;
        else if (r.Left   > _canvasW - margin) dx = (_canvasW - margin) - r.Left;

        if (r.Bottom < margin)             dy =  margin - r.Bottom;
        else if (r.Top    > _canvasH - margin) dy = (_canvasH - margin) - r.Top;

        if (MathF.Abs(dx) > 0.5f || MathF.Abs(dy) > 0.5f)
            _matrix = _matrix.PostConcat(SKMatrix.CreateTranslation(dx, dy));
    }

    public static readonly BindableProperty SelectedBoundaryVertexIndexProperty =
        BindableProperty.Create(nameof(SelectedBoundaryVertexIndex), typeof(int), typeof(SvgView),
            -1, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    public int SelectedBoundaryVertexIndex
    {
        get => (int)GetValue(SelectedBoundaryVertexIndexProperty);
        set => SetValue(SelectedBoundaryVertexIndexProperty, value);
    }

    /// <summary>Индекс выбранного полигона границы в режиме редактирования (-1 = нет).</summary>
    public static readonly BindableProperty SelectedBoundaryPolygonIndexProperty =
        BindableProperty.Create(nameof(SelectedBoundaryPolygonIndex), typeof(int), typeof(SvgView),
            -1, propertyChanged: (b, _, _) => ((SvgView)b).InvalidateSurface());

    public int SelectedBoundaryPolygonIndex
    {
        get => (int)GetValue(SelectedBoundaryPolygonIndexProperty);
        set => SetValue(SelectedBoundaryPolygonIndexProperty, value);
    }

    /// <summary>Нажатие на пустое место холста. Аргумент — координаты в пространстве SVG.</summary>
    public event EventHandler<SKPoint>? CanvasTapped;

    /// <summary>Нажатие на существующий узел.</summary>
    public event EventHandler<NavNode>? NodeTapped;

    /// <summary>Перемещение узла. Аргументы: узел + новые SVG-координаты.</summary>
    public event EventHandler<(NavNode node, SKPoint svgPos)>? NodeMoved;

    /// <summary>Перемещение вершины границы. Аргументы: индекс полигона, индекс вершины, новая SVG-позиция.</summary>
    public event EventHandler<(int polyIdx, int vtxIdx, SKPoint svgPos)>? BoundaryVertexMoved;

    /// <summary>Тап по вершине границы. Аргументы: индекс полигона, индекс вершины.</summary>
    public event EventHandler<(int polyIdx, int vtxIdx)>? BoundaryVertexTapped;

    // ===== Internal state =====

    private static readonly ConcurrentDictionary<int, SKSvg> _svgCache = new();

    /// <summary>
    /// Кеш растровых иконок. null-значение означает «загрузка завершилась ошибкой».
    /// Ключ отсутствует — иконка ещё не загружалась или загружается прямо сейчас.
    /// </summary>
    private readonly ConcurrentDictionary<string, SKBitmap?> _iconBitmapCache = new();

    /// <summary>Пути иконок, загрузка которых уже запущена (чтобы не стартовать дважды).</summary>
    private readonly ConcurrentDictionary<string, bool> _iconLoading = new();

    /// <summary>
    /// Возвращает кешированную иконку или null. Если иконка ещё не загружена — запускает
    /// асинхронную загрузку и перерисовывает холст по её завершении.
    /// Поддерживает:
    ///   — абсолютный путь ФС (FilePicker на десктопе)
    ///   — относительный бандл-путь (например «Icons/free-icon-stair-7170845.png»)
    /// </summary>
    private SKBitmap? GetNodeIconBitmap(string path)
    {
        if (_iconBitmapCache.TryGetValue(path, out var cached))
            return cached;

        // Запускаем загрузку ровно один раз
        if (_iconLoading.TryAdd(path, true))
            _ = LoadIconAsync(path);

        return null;
    }

    private async Task LoadIconAsync(string path)
    {
        SKBitmap? bmp = null;
        try
        {
            if (System.IO.Path.IsPathRooted(path))
            {
                if (System.IO.File.Exists(path))
                {
                    // Абсолютный путь с диска (FilePicker)
                    var bytes = await Task.Run(() => System.IO.File.ReadAllBytes(path)).ConfigureAwait(false);
                    bmp = SKBitmap.Decode(bytes);
                }
                else
                {
                    // Файл не найден по абсолютному пути (другой ПК) —
                    // пробуем загрузить из бандла по имени файла
                    var bundlePath = "Icons/" + System.IO.Path.GetFileName(path);
                    using var s  = await FileSystem.OpenAppPackageFileAsync(bundlePath).ConfigureAwait(false);
                    using var ms = new System.IO.MemoryStream();
                    await s.CopyToAsync(ms).ConfigureAwait(false);
                    bmp = SKBitmap.Decode(ms.ToArray());
                }
            }
            else
            {
                // Бандл-ресурс (Resources/Raw/...)
                using var s  = await FileSystem.OpenAppPackageFileAsync(path).ConfigureAwait(false);
                using var ms = new System.IO.MemoryStream();
                await s.CopyToAsync(ms).ConfigureAwait(false);
                bmp = SKBitmap.Decode(ms.ToArray());
            }
        }
        catch { /* оставляем null */ }

        // Записываем в кеш и перерисовываем (null тоже пишем, чтобы не повторять загрузку)
        _iconBitmapCache[path] = bmp;
        _iconLoading.TryRemove(path, out _);
        MainThread.BeginInvokeOnMainThread(InvalidateSurface);
    }

    /// <summary>Инвалидировать кеш иконок (например при удалении иконки у узла или выборе нового файла).</summary>
    public void ClearIconCache()
    {
        foreach (var bmp in _iconBitmapCache.Values)
            bmp?.Dispose();
        _iconBitmapCache.Clear();
        _iconLoading.Clear();
    }

    public static readonly BindableProperty SvgContentProperty =
        BindableProperty.Create(nameof(SvgContent), typeof(string), typeof(SvgView),
            null, propertyChanged: OnSvgContentChanged);

    public string? SvgContent
    {
        get => (string?)GetValue(SvgContentProperty);
        set => SetValue(SvgContentProperty, value);
    }

    /// <summary>Путь к SVG-файлу относительно SvgFloors/ (например BuildingA/floor1.svg).
    /// SvgView сам загружает файл, растеризует и кеширует WebP на диск.</summary>
    public static readonly BindableProperty FloorSvgPathProperty =
        BindableProperty.Create(nameof(FloorSvgPath), typeof(string), typeof(SvgView),
            null, propertyChanged: (b, _, n) => ((SvgView)b).StartFloorLoad(n as string));

    public string? FloorSvgPath
    {
        get => (string?)GetValue(FloorSvgPathProperty);
        set => SetValue(FloorSvgPathProperty, value);
    }

    private SKPicture? _picture;
    private SKBitmap?  _bitmap;     // растеризованный план (быстрый рендер)
    private SKRect     _svgBounds;  // оригинальные размеры SVG-пространства
    private float      _canvasW, _canvasH;  // размер поверхности рисования (пиксели)
    private bool       _floorLoading;
    private int        _loadVersion; // для отмены устаревших загрузок
    private SKMatrix   _matrix = SKMatrix.Identity;

    // ===== Zoom limits =====
    // ↓↓↓  НАСТРОЙКА ЗУМА  ↓↓↓
    /// <summary>Минимальный масштаб (насколько можно отдалить карту). Например: 0.1 = можно уменьшить в 10 раз от исходного FitMatrix.</summary>
    private const float MinZoom = 0.13f;   // ← МИНИМАЛЬНЫЙ МАСШТАБ
    /// <summary>Максимальный масштаб (насколько можно приблизить карту). Например: 15 = можно увеличить в 15 раз.</summary>
    private const float MaxZoom = 1.0f;  // ← МАКСИМАЛЬНЫЙ МАСШТАБ
    // ↑↑↑  НАСТРОЙКА ЗУМА  ↑↑↑

    // Admin drag state
    private NavNode? _draggingNode;
    private int      _draggingBoundaryPolyIdx   = -1;
    private int      _draggingBoundaryVertexIdx = -1;
    private bool     _didDrag;

    // Rotation state
    private float _totalRotationDeg = 0f;          // накопленный угол
    private const float MaxRotationDeg = 25f;      // максимальный угол вращения
    private float _lastTouchAngleRad = 0f;         // угол между пальцами на предыдущем шаге
    private bool  _touchAngleInitialized = false;  // первый шаг двухпальцевого жеста

    // Отложенный зум, применяемый после завершения загрузки этажа
    private Action? _pendingZoom;

    // ===== Route animation =====
    private float            _dashPhase = 0f;
    private float            _pulsePhase = 0f;
    private DateTime         _lastTick;
    private bool             _routeAnimActive;

    // Настройка анимации маршрута
    private const float DashAnimSpeed  = 30f;   // SVG-ед/с — скорость движения штрихов
    private const float PulseAnimSpeed = 1.2f;  // циклов сияния в секунду

    // ===== Master render timer (single timer at display refresh rate) =====
    private IDispatcherTimer? _masterTimer;
    private bool              _panDirty;
    private bool              _isPanning;

    // ===== Constructor =====

    public SvgView()
    {
        EnableTouchEvents = true;
        Touch += OnTouch;
    }

    private void StartRouteAnimation()
    {
        _routeAnimActive = true;
        _lastTick = DateTime.UtcNow;
        StartMasterTimer();
    }

    private void StopRouteAnimation()
    {
        _routeAnimActive = false;
        _dashPhase  = 0f;
        _pulsePhase = 0f;
        StopMasterTimerIfIdle();
    }

    // ===== Pan render timer (VSync-locked via display refresh rate) =====

    private void StartPanRender()
    {
        _isPanning = true;
        StartMasterTimer();
    }

    private void StopPanRender()
    {
        _isPanning = false;
        _panDirty = false;
        StopMasterTimerIfIdle();
    }

    // ===== Master timer =====

    private void StartMasterTimer()
    {
        if (_masterTimer != null) return;
        double hz = DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
        if (hz <= 0) hz = 60;
        _masterTimer = Dispatcher.CreateTimer();
        _masterTimer.Interval = TimeSpan.FromSeconds(1.0 / hz);
        _masterTimer.Tick += OnMasterTick;
        _masterTimer.Start();
    }

    private void StopMasterTimerIfIdle()
    {
        if (_routeAnimActive || _isPanning) return;
        _masterTimer?.Stop();
        _masterTimer = null;
    }

    private void OnMasterTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        float dt = (float)(now - _lastTick).TotalSeconds;
        _lastTick = now;

        bool needRedraw = false;

        // Анимация маршрута
        if (_routeAnimActive)
        {
            float sc = MatrixScale();
            if (sc < 0.001f) sc = 1f;
            _dashPhase  -= DashAnimSpeed / sc * dt;
            _pulsePhase += PulseAnimSpeed * dt;
            needRedraw = true;
        }

        // Панирование
        if (_panDirty)
        {
            _panDirty  = false;
            needRedraw = true;
        }

        if (needRedraw) InvalidateSurface();
    }

    private void OnOverlayCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateRouteAnimation();
            InvalidateSurface();
        });
    }

    private void UpdateRouteAnimation()
    {
        var route = RouteNodes;
        if (route != null && route.Count > 0) StartRouteAnimation();
        else StopRouteAnimation();
    }

    // ===== Preload (legacy no-op) =====
    public static Task PreloadAsync(string content) => Task.CompletedTask;

    // ===== FloorSvgPath loading pipeline =====

    private void StartFloorLoad(string? relativePath)
    {
        _bitmap = null;
        _picture = null;
        _matrix = SKMatrix.Identity;
        _totalRotationDeg = 0f;
        _floorLoading = !string.IsNullOrWhiteSpace(relativePath);
        InvalidateSurface();
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            int ver = System.Threading.Interlocked.Increment(ref _loadVersion);
            _ = LoadFloorInternalAsync(relativePath, ver);
        }
    }

    private async Task LoadFloorInternalAsync(string relativePath, int version)
    {
        try
        {
            // Ключ кеша по пути (BuildingA/floor1.svg → BuildingA_floor1.svg)
            var cacheKey  = relativePath.Replace('/', '_').Replace('\\', '_');
            var cachePath = Path.Combine(FileSystem.CacheDirectory, cacheKey + ".webp");
            var metaPath  = cachePath + ".meta";

            // ─── Путь 0: предгенерированный WebP прямо в бандле приложения (мгновенно) ───
            var bundledWebp = $"FloorImages/{cacheKey}.webp";
            var bundledMeta = $"FloorImages/{cacheKey}.webp.meta";
            try
            {
                using var metaStream = await FileSystem.OpenAppPackageFileAsync(bundledMeta).ConfigureAwait(false);
                using var metaReader = new StreamReader(metaStream);
                var meta  = await metaReader.ReadToEndAsync().ConfigureAwait(false);
                var parts = meta.Split(',');
                if (parts.Length >= 2
                    && float.TryParse(parts[0], System.Globalization.NumberStyles.Any,
                                      System.Globalization.CultureInfo.InvariantCulture, out float sw)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Any,
                                      System.Globalization.CultureInfo.InvariantCulture, out float sh))
                {
                    using var imgStream = await FileSystem.OpenAppPackageFileAsync(bundledWebp).ConfigureAwait(false);
                    using var imgMs = new MemoryStream();
                    await imgStream.CopyToAsync(imgMs).ConfigureAwait(false);
                    var decoded = SKBitmap.Decode(imgMs.ToArray());
                    if (decoded != null && _loadVersion == version)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            if (_loadVersion != version) { decoded.Dispose(); return; }
                            _bitmap       = decoded;
                            _svgBounds    = new SKRect(0, 0, sw, sh);
                            _floorLoading = false;
                            ApplyPendingZoomOrFit();
                        });
                        return;
                    }
                }
            }
            catch { /* бандловый WebP не найден — идём дальше */ }

            // ─── Путь 1: WebP кеш устройства ───
            if (File.Exists(cachePath) && File.Exists(metaPath))
            {
                var meta  = await File.ReadAllTextAsync(metaPath).ConfigureAwait(false);
                var parts = meta.Split(',');
                if (parts.Length >= 2
                    && float.TryParse(parts[0], System.Globalization.NumberStyles.Any,
                                      System.Globalization.CultureInfo.InvariantCulture, out float sw)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Any,
                                      System.Globalization.CultureInfo.InvariantCulture, out float sh))
                {
                    var bytes   = await File.ReadAllBytesAsync(cachePath).ConfigureAwait(false);
                    var decoded = SKBitmap.Decode(bytes);
                    if (decoded != null && _loadVersion == version)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            if (_loadVersion != version) { decoded.Dispose(); return; }
                            _bitmap       = decoded;
                            _svgBounds    = new SKRect(0, 0, sw, sh);
                            _floorLoading = false;
                            ApplyPendingZoomOrFit();
                        });
                        return;
                    }
                }
            }

            // ─── Путь 2: читаем SVG, растеризуем, сохраняем WebP ───
            var packagePath = $"SvgFloors/{relativePath}";
            byte[] svgBytes;
            using (var pkgStream = await FileSystem.OpenAppPackageFileAsync(packagePath).ConfigureAwait(false))
            using (var ms = new MemoryStream())
            {
                await pkgStream.CopyToAsync(ms).ConfigureAwait(false);
                svgBytes = ms.ToArray();
            }

            if (_loadVersion != version) return;

            // Парсим SVG и растеризуем на пуле потоков
            SKBitmap? bitmap = null;
            SKRect    bounds = default;
            await Task.Run(() =>
            {
                var svg = new SKSvg();
                using var svgStream = new MemoryStream(svgBytes);
                svg.Load(svgStream);
                if (svg.Picture == null) return;

                bounds = svg.Picture.CullRect;

                // Масштабируем до максимум 2048px по большей стороне
                float ratio = Math.Max(bounds.Width, bounds.Height);
                float s     = ratio <= 2048f ? 1f : 2048f / ratio;
                int bw = Math.Max(1, (int)(bounds.Width  * s));
                int bh = Math.Max(1, (int)(bounds.Height * s));

                bitmap = new SKBitmap(bw, bh, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var bCanvas = new SKCanvas(bitmap);
                bCanvas.Clear(new SKColor(248, 248, 248));
                float sx = bw / bounds.Width;
                float sy = bh / bounds.Height;
                bCanvas.SetMatrix(SKMatrix.CreateScaleTranslation(
                    sx, sy, -bounds.Left * sx, -bounds.Top * sy));
                bCanvas.DrawPicture(svg.Picture);
            }).ConfigureAwait(false);

            if (bitmap == null || _loadVersion != version) { bitmap?.Dispose(); return; }

            // Сохраняем WebP в кеш
            try
            {
                using var fs = File.Create(cachePath);
                bitmap.Encode(fs, SKEncodedImageFormat.Webp, 85);
                var meta = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1}", bounds.Width, bounds.Height);
                await File.WriteAllTextAsync(metaPath, meta).ConfigureAwait(false);
            }
            catch { /* кеш необязателен */ }

            if (_loadVersion != version) { bitmap.Dispose(); return; }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_loadVersion != version) { bitmap.Dispose(); return; }
                _bitmap       = bitmap;
                _svgBounds    = bounds;
                _floorLoading = false;
                ApplyPendingZoomOrFit();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FloorSvgPath load error: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_loadVersion != version) return;
                _floorLoading = false;
                InvalidateSurface();
            });
        }
    }

    // ===== Mouse wheel (Windows) =====

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if WINDOWS
        if (Handler?.PlatformView is SkiaSharp.Views.Windows.SKXamlCanvas nativeCanvas)
        {
            nativeCanvas.PointerWheelChanged -= OnPointerWheelChanged;
            nativeCanvas.PointerWheelChanged += OnPointerWheelChanged;
            nativeCanvas.PointerMoved -= OnPointerMovedWindows;
            nativeCanvas.PointerMoved += OnPointerMovedWindows;
            // Подавляем контекстное меню ПКМ, чтобы оно не мешало вращению
            nativeCanvas.RightTapped -= OnRightTappedWindows;
            nativeCanvas.RightTapped += OnRightTappedWindows;
        }
#endif
    }

#if WINDOWS
    private void OnPointerWheelChanged(object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point  = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
        int delta  = point.Properties.MouseWheelDelta;
        var pos    = point.Position;
        float factor = delta > 0 ? 1.1f : (1f / 1.1f);
        var pivot  = new SKPoint((float)pos.X, (float)pos.Y);
        ApplyZoom(factor, pivot);
        e.Handled = true;
    }

    private void OnRightTappedWindows(object sender,
        Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        => e.Handled = true;   // подавляем контекстное меню

    // Двухкнопочное вращение на Windows
    // Состояние кнопок читается прямо из Properties каждого PointerMoved,
    // поэтому PointerPressed/Released не нужны.
    private float _winLastRotX = -1f;   // -1 = режим вращения ещё не начался

    private void OnPointerMovedWindows(object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var uiElem = sender as Microsoft.UI.Xaml.UIElement;
        var props  = e.GetCurrentPoint(uiElem).Properties;
        bool bothDown = props.IsLeftButtonPressed && props.IsRightButtonPressed;

        if (!bothDown)
        {
            _winLastRotX = -1f;   // сброс при отпускании любой кнопки
            return;
        }

        float x = (float)e.GetCurrentPoint(uiElem).Position.X;

        if (_winLastRotX < 0f)
        {
            // Первый кадр с двумя кнопками — запоминаем позицию, не крутим
            _winLastRotX = x;
            e.Handled = true;
            return;
        }

        float dx = x - _winLastRotX;
        _winLastRotX = x;
        if (MathF.Abs(dx) < 0.5f) return;

        // 0.3 градуса на пиксель горизонтального смещения
        float deltaDeg = dx * 0.3f;
        var canvasCenter = new SKPoint(_canvasW / 2f, _canvasH / 2f);
        ApplyRotation(deltaDeg, canvasCenter);
        e.Handled = true;
    }
#endif

    // ===== SVG loading =====

    private static void OnSvgContentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SvgView view) view.ReloadSvg(newValue as string);
    }

    private void ReloadSvg(string? content)
    {
        _picture = null;
        _matrix  = SKMatrix.Identity;

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                int hash = content.GetHashCode();
                if (!_svgCache.TryGetValue(hash, out var svg))
                {
                    svg = new SKSvg();
                    using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                    svg.Load(ms);
                    _svgCache[hash] = svg;
                }
                _picture = svg.Picture;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SVG parse error: {ex.Message}");
                _picture = null;
            }
        }
        InvalidateSurface();
    }

    // ===== Painting =====

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        var info   = e.Info;

        _canvasW = info.Width;
        _canvasH = info.Height;

        canvas.Clear(new SKColor(248, 248, 248));

        // ─── Bitmap-путь (быстро: кешированный WebP) ───
        if (_bitmap != null && _svgBounds.Width > 0)
        {
            if (_matrix.IsIdentity)
                _matrix = FitMatrix(_svgBounds, info.Width, info.Height);
            canvas.SetMatrix(_matrix);
            canvas.DrawBitmap(_bitmap, _svgBounds);
            if (ShowGraph) { DrawGraph(canvas); DrawRoute(canvas); }
            return;
        }

        // ─── SVG-путь (legacy SvgContent байндинг) ───
        if (_picture != null)
        {
            var bounds = _picture.CullRect;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                if (_matrix.IsIdentity)
                    _matrix = FitMatrix(bounds, info.Width, info.Height);
            }
        }

        canvas.SetMatrix(_matrix);
        if (_picture != null)
            canvas.DrawPicture(_picture);
        if (ShowGraph) { DrawGraph(canvas); DrawRoute(canvas); }

        // ─── Индикатор загрузки (если FloorSvgPath задан но ещё грузится) ───
        if (_floorLoading)
        {
            canvas.SetMatrix(SKMatrix.Identity);
            using var bgP = new SKPaint { Color = new SKColor(255, 255, 255, 200) };
            var bgR = new SKRect(info.Width / 2f - 130, info.Height / 2f - 28,
                                 info.Width / 2f + 130, info.Height / 2f + 28);
            canvas.DrawRoundRect(bgR, 12, 12, bgP);
            using var txtP = new SKPaint { Color = new SKColor(50, 50, 50), IsAntialias = true };
            using var font  = new SKFont(SKTypeface.Default, 14f);
            canvas.DrawText("Загрузка плана этажа...",
                info.Width / 2f, info.Height / 2f + 5f, SKTextAlign.Center, font, txtP);
        }
    }

    /// <summary>Масштаб матрицы (среднее X/Y). Используется для компенсации зума при рисовании оверлея.</summary>
    private float MatrixScale()
    {
        float sx = MathF.Sqrt(_matrix.ScaleX * _matrix.ScaleX + _matrix.SkewY  * _matrix.SkewY);
        float sy = MathF.Sqrt(_matrix.SkewX  * _matrix.SkewX  + _matrix.ScaleY * _matrix.ScaleY);
        return (sx + sy) / 2f;
    }

    private void DrawGraph(SKCanvas canvas)
    {
        var edges = Edges;
        var nodes = Nodes;
        if (nodes == null) return;

        // В пользовательском режиме при наличии маршрута: скрываем кружки и рёбра, но оставляем подписи
        bool hideCirclesForRoute = !IsAdminMode && RouteNodes != null && RouteNodes.Count > 0;

        // В пользовательском режиме скрываем служебные waypoint-узлы коридоров,
        // а также узлы огнетушителей (если не активирован режим ЧС)
        var qrVisible = QrAnchorNodeIds != null ? new HashSet<string>(QrAnchorNodeIds) : null;
        var visibleNodes = IsAdminMode
            ? nodes.ToList()
            : nodes.Where(n => !n.IsWaypoint
                && (!n.IsFireExtinguisher || ShowFireExtinguishers)
                && (!n.IsEvacuationExit  || ShowFireExtinguishers)
                && (!n.IsQrAnchor        || (qrVisible?.Contains(n.Id) ?? false))).ToList();

        float sc = MatrixScale();
        if (sc < 0.001f) sc = 1f;

        // ── Границы аудиторий ──────────────────────────────────────────────────────────
        // Пользовательский режим: подсветить границу узла-назначения (только если он на текущем этаже)
        int curFloor = CurrentFloorNumber;
        var hlNode = HighlightBoundaryNode;
        if (!IsAdminMode && hlNode?.Boundaries != null && hlNode.FloorNumber == curFloor)
        {
            foreach (var poly in hlNode.Boundaries)
                if (poly.Count >= 3)
                    DrawBoundaryHighlight(canvas, poly, new SKColor(37, 99, 235), sc);
        }

        // Подсветить границу узла-отправления (зелёный, только если он на текущем этаже)
        var hlStartNode = HighlightStartNode;
        if (!IsAdminMode && hlStartNode?.Boundaries != null
            && hlStartNode != hlNode && hlStartNode.FloorNumber == curFloor)
        {
            foreach (var poly in hlStartNode.Boundaries)
                if (poly.Count >= 3)
                    DrawBoundaryHighlight(canvas, poly, new SKColor(34, 197, 94), sc);
        }

        foreach (var node in visibleNodes)
        {
            if (node.Boundaries == null || node.Boundaries.Count == 0) continue;

            bool isSelected = node == SelectedNode;
            int selPolyIdx = SelectedBoundaryPolygonIndex;
            int selVtx     = SelectedBoundaryVertexIndex;

            for (int pi = 0; pi < node.Boundaries.Count; pi++)
            {
                var poly = node.Boundaries[pi];
                if (poly.Count < 3) continue;

                using var boundaryPath = new SKPath();
                boundaryPath.MoveTo(poly[0][0], poly[0][1]);
                for (int bi = 1; bi < poly.Count; bi++)
                    boundaryPath.LineTo(poly[bi][0], poly[bi][1]);
                boundaryPath.Close();

                if (IsAdminMode)
                {
                    bool isSelPoly = isSelected && pi == selPolyIdx;
                    // Заливка
                    byte fillA = isSelPoly ? (byte)55 : isSelected ? (byte)35 : (byte)25;
                    var fillColor = isSelPoly
                        ? new SKColor(255, 152, 0, fillA)
                        : isSelected
                            ? new SKColor(255, 152, 0, fillA)
                            : new SKColor(37, 99, 235, fillA);
                    using var fillP = new SKPaint { Color = fillColor, IsAntialias = true };
                    canvas.DrawPath(boundaryPath, fillP);
                    // Обводка
                    float strokeW = (isSelPoly ? 2f : 1.5f) / sc;
                    var strokeColor = isSelPoly
                        ? new SKColor(255, 152, 0, 220)
                        : isSelected
                            ? new SKColor(255, 152, 0, 140)
                            : new SKColor(37, 99, 235, 160);
                    using var strokeP = new SKPaint
                    {
                        Color = strokeColor, StrokeWidth = strokeW,
                        IsStroke = true, IsAntialias = true,
                        PathEffect = SKPathEffect.CreateDash(new[] { 6f / sc, 3f / sc }, 0f)
                    };
                    canvas.DrawPath(boundaryPath, strokeP);

                    // Вершины границы (редактируемые ручки) — для всех полигонов выбранного узла
                    if (isSelected)
                    {
                        float vtxR = 6f / sc;
                        bool isThisSelPoly = pi == selPolyIdx;
                        using var vtxFill    = new SKPaint { Color = new SKColor(255, 152, 0, 200), IsAntialias = true };
                        using var vtxSelFill = new SKPaint { Color = new SKColor(255, 80,  0, 255), IsAntialias = true };
                        using var vtxStroke  = new SKPaint { Color = SKColors.White, StrokeWidth = 1.5f / sc, IsStroke = true, IsAntialias = true };
                        for (int vi = 0; vi < poly.Count; vi++)
                        {
                            var vp = new SKPoint(poly[vi][0], poly[vi][1]);
                            bool isSelVtx = isThisSelPoly && vi == selVtx;
                            canvas.DrawCircle(vp, vtxR, isSelVtx ? vtxSelFill : vtxFill);
                            canvas.DrawCircle(vp, vtxR, vtxStroke);
                        }
                    }
                }
            }
        }

        // ── Превью рисуемой границы ───────────────────────────────────
        var preview = BoundaryPreview;
        if (IsAdminMode && preview != null && preview.Count > 0)
        {
            using var previewPath = new SKPath();
            previewPath.MoveTo(preview[0]);
            for (int bi = 1; bi < preview.Count; bi++)
                previewPath.LineTo(preview[bi]);

            // Прерывистая линия
            using var previewP = new SKPaint
            {
                Color = new SKColor(255, 152, 0, 200), StrokeWidth = 2f / sc,
                IsStroke = true, IsAntialias = true,
                StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
                PathEffect = SKPathEffect.CreateDash(new[] { 8f / sc, 4f / sc }, 0f)
            };
            canvas.DrawPath(previewPath, previewP);

            // Точки вершин
            float dotR = 5f / sc;
            using var dotP = new SKPaint { Color = new SKColor(255, 152, 0, 230), IsAntialias = true };
            using var dotStroke = new SKPaint { Color = SKColors.White, StrokeWidth = 1.5f / sc, IsStroke = true, IsAntialias = true };
            foreach (var pt in preview)
            {
                canvas.DrawCircle(pt, dotR, dotP);
                canvas.DrawCircle(pt, dotR, dotStroke);
            }
        }

        // O(1) lookup: словарь строится один раз на кадр (вместо O(E×N) FirstOrDefault)
        var nodeById = new Dictionary<string, NavNode>(visibleNodes.Count);
        foreach (var n in visibleNodes) nodeById[n.Id] = n;

        using var edgePaint = new SKPaint
        {
            Color = new SKColor(150, 150, 150, 180),
            StrokeWidth = 2f / sc,
            IsAntialias = true, IsStroke = true
        };

        if (edges != null && !hideCirclesForRoute)
        {
            foreach (var edge in edges)
            {
                if (!nodeById.TryGetValue(edge.FromId, out var from)) continue;
                if (!nodeById.TryGetValue(edge.ToId,   out var to))   continue;
                // координаты — напрямую в SVG-пространстве, холст трансформирует сам
                canvas.DrawLine(from.X, from.Y, to.X, to.Y, edgePaint);
            }
        }

        // Подсветка рёбер выбранного узла (режим администратора)
        var hlId = HighlightedNodeId;
        if (IsAdminMode && !string.IsNullOrEmpty(hlId) && edges != null && !hideCirclesForRoute)
        {
            // Ищем также кросс-этажные рёбра — используем все узлы графа (не только visibleNodes)
            Dictionary<string, NavNode> allNodeById;
            if (nodes != null && nodes.Count != visibleNodes.Count)
            {
                allNodeById = new Dictionary<string, NavNode>(nodes.Count);
                foreach (var n in nodes) allNodeById[n.Id] = n;
            }
            else allNodeById = nodeById;

            using var hlPaint = new SKPaint
            {
                Color       = new SKColor(255, 152, 0, 230),
                StrokeWidth = 4f / sc,
                IsAntialias = true,
                IsStroke    = true,
                StrokeCap   = SKStrokeCap.Round
            };
            foreach (var edge in edges)
            {
                if (edge.FromId != hlId && edge.ToId != hlId) continue;
                if (!allNodeById.TryGetValue(edge.FromId, out var from)) continue;
                if (!allNodeById.TryGetValue(edge.ToId,   out var to))   continue;
                canvas.DrawLine(from.X, from.Y, to.X, to.Y, hlPaint);
            }
        }

        float r        = 15f / sc;   // ← РАДИУС ТОЧЕК В ЭКРАННЫХ ПИКСЕЛЯХ (меняйте здесь)
        float fontSize = 11f / sc;

        using var fillNorm  = new SKPaint { Color = new SKColor(33, 150, 243),  IsAntialias = true };
        using var fillSel   = new SKPaint { Color = new SKColor(255, 152, 0),   IsAntialias = true };
        using var fillTrans = new SKPaint { Color = new SKColor(156, 39, 176),  IsAntialias = true };
        using var fillExit     = new SKPaint { Color = new SKColor(76, 175, 80),   IsAntialias = true };
        using var fillFireExt  = new SKPaint { Color = new SKColor(76, 175, 80),   IsAntialias = true };
        using var fillEvacExit = new SKPaint { Color = new SKColor(220, 38, 38),   IsAntialias = true };
        using var fillQr       = new SKPaint { Color = new SKColor(0, 188, 212),   IsAntialias = true };  // cyan/teal for QR anchors
        using var fillWaypoint = new SKPaint { Color = new SKColor(210, 105, 30),  IsAntialias = true };  // brown for corridor waypoints
        using var stroke    = new SKPaint { Color = SKColors.White, StrokeWidth = 2.5f / sc, IsStroke = true, IsAntialias = true };
        using var shadowP   = new SKPaint { Color = new SKColor(0, 0, 0, 60), IsAntialias = true,
                                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f / sc) };
        using var font      = new SKFont(SKTypeface.Default, fontSize);

        // Набор узлов мультивыбора (для быстрого O(1) поиска в цикле ниже)
        var multiSelectSet = MultiSelectedNodes != null
            ? new HashSet<NavNode>(MultiSelectedNodes)
            : null;

        // При наличии маршрута — строим набор id узлов маршрута, чтобы не рисовать их метки дважды
        // (DrawRoute уже рисует метки для узлов маршрута)
        var routeNodeIds = hideCirclesForRoute && RouteNodes != null
            ? new HashSet<string>(RouteNodes.Select(n => n.Id))
            : null;

        // Занятые области меток — для отбраковки перекрывающихся подписей (только польз. режим)
        var occupiedLabelRects = new List<SKRect>();

        foreach (var node in visibleNodes)
        {
            // В польз. режиме: если скрыта и точка, и метка — пропускаем полностью
            if (!IsAdminMode && node.IsHidden && node.IsLabelHidden) continue;

            bool drawCircle = (IsAdminMode || !node.IsHidden) && !hideCirclesForRoute;
            bool isInMultiSelect = multiSelectSet?.Contains(node) == true;

            var s    = new SKPoint(node.X, node.Y);
            float nodeR;
            if (node.IsFireExtinguisher)
                // По умолчанию меньший радиус для огнетушителей (если не задан индивидуальный)
                nodeR = r * (node.NodeRadiusScale is > 0.01f and < 0.99f ? node.NodeRadiusScale : 0.6f);
            else
                nodeR = r * (node.NodeRadiusScale > 0.01f ? node.NodeRadiusScale : 1f);

            // Увеличить узлы начала/конца маршрута
            if (!IsAdminMode && (node == hlNode || node == hlStartNode))
                nodeR *= 1.4f;

            if (drawCircle)
            {
                // ── Иконка пользователя вместо кружка ──────────────────────
                if (!string.IsNullOrEmpty(node.IconPath))
                {
                    var bmp = GetNodeIconBitmap(node.IconPath);
                    if (bmp != null)
                    {
                        float iconAlpha = (IsAdminMode && node.IsHidden) ? 0.35f : 1f;
                        // Кольцо выделения (одиночный выбор)
                        if (node == SelectedNode)
                        {
                            using var selRing = new SKPaint
                            {
                                Color       = new SKColor(255, 152, 0, 220),
                                StrokeWidth = 3f / sc,
                                IsStroke    = true,
                                IsAntialias = true,
                            };
                            canvas.DrawCircle(s, nodeR + 4f / sc, selRing);
                        }
                        // Кольцо мультивыбора (бирюзовое)
                        if (isInMultiSelect)
                        {
                            using var multiRing = new SKPaint
                            {
                                Color       = new SKColor(0, 210, 190, 230),
                                StrokeWidth = 3f / sc,
                                IsStroke    = true,
                                IsAntialias = true,
                            };
                            canvas.DrawCircle(s, nodeR + (node == SelectedNode ? 9f : 4f) / sc, multiRing);
                        }
                        var dest = new SKRect(s.X - nodeR, s.Y - nodeR, s.X + nodeR, s.Y + nodeR);
                        using var iconPaint = new SKPaint { IsAntialias = true };
                        if (iconAlpha < 1f)
                            iconPaint.ColorFilter = SKColorFilter.CreateBlendMode(
                                SKColors.White.WithAlpha((byte)(255 * iconAlpha)), SKBlendMode.DstIn);
                        canvas.DrawBitmap(bmp, dest, iconPaint);
                        goto skipCircle;
                    }
                }

                // Цвет: персональный → выбранный → типовой
                SKPaint fill;
                if (node == SelectedNode)
                {
                    fill = fillSel;
                }
                else if (!string.IsNullOrEmpty(node.NodeColorHex))
                {
                    if (SKColor.TryParse("#" + node.NodeColorHex, out var custom))
                        fill = new SKPaint { Color = custom, IsAntialias = true };
                    else
                        fill = fillNorm;
                }
                else if (IsAdminMode && node.IsWaypoint) fill = fillWaypoint;
                else if (node.IsFireExtinguisher) fill = fillFireExt;
                else if (node.IsEvacuationExit)   fill = fillEvacExit;
                else if (node.IsExit)              fill = fillExit;
                else if (node.IsQrAnchor)          fill = fillQr;
                else if (node.IsTransition) fill = fillTrans;
                else                        fill = fillNorm;

                // В админ-режиме скрытые узлы рисуем полупрозрачными
                float alpha = (IsAdminMode && node.IsHidden) ? 0.35f : 1f;
                if (alpha < 1f)
                {
                    var c = fill.Color;
                    fill = new SKPaint { Color = c.WithAlpha((byte)(c.Alpha * alpha)), IsAntialias = true };
                }

                // Тень пропускаем при активном пане — экономим дорогой GPU blur
                if (!_isPanning)
                    canvas.DrawCircle(s.X + 2f / sc, s.Y + 3f / sc, nodeR, shadowP);
                canvas.DrawCircle(s, nodeR, fill);
                canvas.DrawCircle(s, nodeR, stroke);
                // Кольцо мультивыбора (бирюзовое)
                if (isInMultiSelect)
                {
                    using var multiRing = new SKPaint
                    {
                        Color       = new SKColor(0, 210, 190, 230),
                        StrokeWidth = 3f / sc,
                        IsStroke    = true,
                        IsAntialias = true,
                    };
                    canvas.DrawCircle(s, nodeR + (node == SelectedNode ? 8f : 4f) / sc, multiRing);
                }

                // Внутренняя метка (InnerLabel, если задана)
                if (!string.IsNullOrEmpty(node.InnerLabel))
                {
                    using var textP = new SKPaint { Color = SKColors.White, IsAntialias = true };
                    canvas.DrawText(node.InnerLabel, s.X, s.Y + fontSize * 0.38f, SKTextAlign.Center, font, textP);
                }

                skipCircle:;
            }

            // Подпись под точкой
            // Если узел в маршруте — его метку уже нарисует DrawRoute, пропускаем
            if (routeNodeIds != null && routeNodeIds.Contains(node.Id)) continue;
            // Подпись waypoint-точек коридора не отображается в обычном режиме (в админ режиме показываем)
            if (node.IsWaypoint && !IsAdminMode) continue;
            if (!(!IsAdminMode && (node.IsLabelHidden || node.IsFireExtinguisher)))
            {
                float lblOpacity = (IsAdminMode && node.IsLabelHidden) ? 0.35f : 1f;
                float lblSize = (IsAdminMode ? 12f : 11f) / sc * (node.LabelScale > 0.01f ? node.LabelScale : 1f);
                using var namePaint = new SKPaint { Color = new SKColor(20, 20, 20, (byte)(255 * lblOpacity)), IsAntialias = true };
                using var nameFont  = new SKFont(SKTypeface.Default, lblSize);
                var nameStr = node.Name;
                var nameW   = nameFont.MeasureText(nameStr, namePaint);
                // Если кружок скрыт из-за маршрута — подпись располагаем прямо у координаты узла
                float labelYOffset = hideCirclesForRoute ? -lblSize / 2f : nodeR + 2f / sc;
                var pillRect = new SKRect(s.X - nameW / 2f - 4f / sc, s.Y + labelYOffset,
                                          s.X + nameW / 2f + 4f / sc, s.Y + labelYOffset + lblSize + 4f / sc);
                using var pillPaint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)(210 * lblOpacity)), IsAntialias = true };

                // Анти-наложение: в пользовательском режиме пропускаем метку, если она перекрывает уже нарисованную.
                // Приоритетные узлы (выбранный, пункт А/Б маршрута) всегда отображаются.
                bool isPriorityLabel = node == SelectedNode || node == hlNode || node == hlStartNode
                    || !string.IsNullOrEmpty(node.IconPath);
                bool labelCulled = false;
                if (!IsAdminMode && !isPriorityLabel)
                {
                    foreach (var occ in occupiedLabelRects)
                    {
                        if (pillRect.Left < occ.Right  && pillRect.Right  > occ.Left &&
                            pillRect.Top  < occ.Bottom && pillRect.Bottom > occ.Top)
                        { labelCulled = true; break; }
                    }
                }
                if (!labelCulled)
                {
                    occupiedLabelRects.Add(pillRect);
                    canvas.DrawRoundRect(pillRect, 4f / sc, 4f / sc, pillPaint);
                    canvas.DrawText(nameStr, s.X, s.Y + labelYOffset + lblSize, SKTextAlign.Center, nameFont, namePaint);
                }
            }
        }

        // --- Blocked-node overlays: red \u2717 + semi-transparent red circle ---
        var blockedSet = BlockedNodeIds != null ? new HashSet<string>(BlockedNodeIds) : null;
        if (blockedSet != null && blockedSet.Count > 0)
        {
            using var blockedFill   = new SKPaint { Color = new SKColor(220, 38, 38, 180), IsAntialias = true };
            using var blockedStroke = new SKPaint { Color = new SKColor(220, 38, 38), StrokeWidth = 3f / sc, IsStroke = true, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            foreach (var node in visibleNodes)
            {
                if (!blockedSet.Contains(node.Id)) continue;
                var s = new SKPoint(node.X, node.Y);
                float nodeR = r * (node.NodeRadiusScale > 0.01f ? node.NodeRadiusScale : 1f);
                // Red semi-transparent overlay circle
                canvas.DrawCircle(s, nodeR, blockedFill);
                // White border so it stands out
                canvas.DrawCircle(s, nodeR, new SKPaint { Color = SKColors.White, StrokeWidth = 2f / sc, IsStroke = true, IsAntialias = true });
                // Red X
                float arm = nodeR * 0.55f;
                canvas.DrawLine(s.X - arm, s.Y - arm, s.X + arm, s.Y + arm, blockedStroke);
                canvas.DrawLine(s.X + arm, s.Y - arm, s.X - arm, s.Y + arm, blockedStroke);
            }
        }
    }

    private static void DrawBoundaryHighlight(SKCanvas canvas, List<float[]> boundary, SKColor color, float sc)
    {
        using var hlPath = new SKPath();
        hlPath.MoveTo(boundary[0][0], boundary[0][1]);
        for (int bi = 1; bi < boundary.Count; bi++)
            hlPath.LineTo(boundary[bi][0], boundary[bi][1]);
        hlPath.Close();
        using var hlFill = new SKPaint { Color = color.WithAlpha(50), IsAntialias = true };
        canvas.DrawPath(hlPath, hlFill);
        using var hlStroke = new SKPaint
        {
            Color = color.WithAlpha(230), StrokeWidth = 2.5f / sc,
            IsStroke = true, IsAntialias = true, StrokeCap = SKStrokeCap.Round,
        };
        canvas.DrawPath(hlPath, hlStroke);
    }

    private void DrawRoute(SKCanvas canvas)
    {
        var route = RouteNodes;
        if (route == null || route.Count < 2) return;

        float sc = MatrixScale();
        if (sc < 0.001f) sc = 1f;

        // Строим путь с учётом разрывов: в точках смены этажа делаем MoveTo вместо LineTo
        var breaks = RouteNodeBreaks != null ? new HashSet<int>(RouteNodeBreaks) : null;
        using var routePath = new SKPath();
        routePath.MoveTo(route[0].X, route[0].Y);
        for (int i = 1; i < route.Count; i++)
        {
            if (breaks != null && breaks.Contains(i))
                routePath.MoveTo(route[i].X, route[i].Y); // разрыв — маршрут уходил на другой этаж
            else
                routePath.LineTo(route[i].X, route[i].Y);
        }

        // ── 1. Пульсирующая подсветка (широкий blur) ─────────────────────
        float pulse  = (MathF.Sin(_pulsePhase) + 1f) / 2f;       // 0…1
        float glowW  = (18f + 8f * pulse) / sc;
        byte  glowA  = (byte)(45 + 25 * pulse);
        using var glowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (7f + 3f * pulse) / sc);
        using var glowPaint = new SKPaint
        {
            Color       = new SKColor(37, 99, 235, glowA),
            StrokeWidth = glowW,
            IsStroke    = true, IsAntialias = true,
            StrokeCap   = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            MaskFilter  = glowBlur,
        };
        canvas.DrawPath(routePath, glowPaint);

        // ── 2. Полупрозрачный трек (фон под штрихами) ────────────────────
        using var trackPaint = new SKPaint
        {
            Color       = new SKColor(186, 230, 253, 100),
            StrokeWidth = 7f / sc,
            IsStroke    = true, IsAntialias = true,
            StrokeCap   = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(routePath, trackPaint);

        // ── 3. Основные анимированные штрихи ─────────────────────────────
        float dashLen  = 20f / sc;
        float gapLen   = 12f / sc;
        float phase    = _dashPhase % (dashLen + gapLen);
        using var dashEffect = SKPathEffect.CreateDash(new[] { dashLen, gapLen }, phase);
        using var routePaint = new SKPaint
        {
            Color       = new SKColor(37, 99, 235, 230),
            StrokeWidth = 5f / sc,
            IsStroke    = true, IsAntialias = true,
            StrokeCap   = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            PathEffect  = dashEffect,
        };
        canvas.DrawPath(routePath, routePaint);

        // ── 4. Быстрые тонкие блики (вторая фаза) ────────────────────────
        float phase2 = (_dashPhase * 1.7f) % (dashLen + gapLen);
        using var dashEffect2 = SKPathEffect.CreateDash(new[] { dashLen * 0.35f, gapLen * 2.5f }, phase2);
        using var sparkPaint  = new SKPaint
        {
            Color       = new SKColor(147, 197, 253, 150),
            StrokeWidth = 2.5f / sc,
            IsStroke    = true, IsAntialias = true,
            StrokeCap   = SKStrokeCap.Round,
            PathEffect  = dashEffect2,
        };
        canvas.DrawPath(routePath, sparkPaint);

        // ── Waypoint circles ──────────────────────────────────────────────
        using var rfill   = new SKPaint { Color = new SKColor(255, 152,  0),   IsAntialias = true }; // промежуточные аудитории — оранжевый
        using var tfill   = new SKPaint { Color = new SKColor(156, 39,  176),   IsAntialias = true }; // переходы — фиолетовый
        using var sfill   = new SKPaint { Color = new SKColor(33,  150, 243),   IsAntialias = true }; // старт — синий
        using var efill   = new SKPaint { Color = new SKColor(244,  67,  54),   IsAntialias = true }; // финиш — красный
        using var rstroke = new SKPaint { Color = SKColors.White, StrokeWidth = 2.5f / sc, IsStroke = true, IsAntialias = true };
        using var stepFont = new SKFont(SKTypeface.Default, 11f / sc);
        using var stepTxt  = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var lblFont  = new SKFont(SKTypeface.Default, 11f / sc);
        using var lblPaint = new SKPaint { Color = new SKColor(20, 20, 20), IsAntialias = true };

        for (int i = 0; i < route.Count; i++)
        {
            var node = route[i];
            var pt   = new SKPoint(node.X, node.Y);
            bool isStart = i == 0, isEnd = i == route.Count - 1;
            bool isIntermediate = !isStart && !isEnd;

            // Промежуточные waypoint-точки коридора не показываем (кроме режима блокировки)
            if (!IsAdminMode && isIntermediate && node.IsWaypoint && !ShowRouteWaypoints) continue;

            // В режиме блокировки промежуточные waypoint-точки рисуем в их собственном стиле (без маршрутных цветов и без подписи)
            if (isIntermediate && node.IsWaypoint)
            {
                float wr = 15f / sc * (node.NodeRadiusScale > 0.01f ? node.NodeRadiusScale : 1f);
                SKPaint wfill;
                if (!string.IsNullOrEmpty(node.NodeColorHex) && SKColor.TryParse("#" + node.NodeColorHex, out var wc))
                    wfill = new SKPaint { Color = wc, IsAntialias = true };
                else
                    wfill = new SKPaint { Color = new SKColor(33, 150, 243), IsAntialias = true };
                canvas.DrawCircle(pt, wr, wfill);
                canvas.DrawCircle(pt, wr, rstroke);
                wfill.Dispose();
                continue;
            }

            float cr = 14f / sc;   // единый размер для всех видимых узлов
            if (isStart || isEnd) cr *= 1.4f;  // увеличить узлы начала/конца маршрута

            // ── Иконка пользователя ──────────────────────────────────────
            if (!string.IsNullOrEmpty(node.IconPath))
            {
                var bmp = GetNodeIconBitmap(node.IconPath);
                if (bmp != null)
                {
                    float iconR = cr * (node.NodeRadiusScale > 0.01f ? node.NodeRadiusScale : 1f);
                    var dest = new SKRect(pt.X - iconR, pt.Y - iconR, pt.X + iconR, pt.Y + iconR);
                    using var iconPaint = new SKPaint { IsAntialias = true };
                    canvas.DrawBitmap(bmp, dest, iconPaint);

                    // Буква A/B у старта и финиша поверх иконки
                    string? letterI = isStart ? "A" : isEnd ? "B" : null;
                    if (letterI != null)
                        canvas.DrawText(letterI, pt.X, pt.Y + 4f / sc, SKTextAlign.Center, stepFont, stepTxt);

                    // Подпись с именем узла
                    if (!IsAdminMode && !node.IsWaypoint && !string.IsNullOrWhiteSpace(node.Name) && !node.IsFireExtinguisher)
                    {
                        var lbl  = node.Name;
                        var lblW = lblFont.MeasureText(lbl, lblPaint);
                        float pillX = pt.X - lblW / 2f - 5f / sc;
                        float pillY = pt.Y + iconR + 3f / sc;
                        var pill = new SKRect(pillX, pillY, pillX + lblW + 10f / sc, pillY + 15f / sc);
                        using var pillBg = new SKPaint { Color = new SKColor(255, 255, 255, 230), IsAntialias = true };
                        canvas.DrawRoundRect(pill, 4f / sc, 4f / sc, pillBg);
                        canvas.DrawText(lbl, pt.X, pillY + 12f / sc, SKTextAlign.Center, lblFont, lblPaint);
                    }
                    continue;
                }
            }

            var fill = isStart ? sfill
                     : isEnd   ? efill
                     : node.IsTransition ? tfill
                     : rfill;

            canvas.DrawCircle(pt, cr, fill);
            canvas.DrawCircle(pt, cr, rstroke);

            // Буква A/B у старта и финиша
            string? letter = isStart ? "A" : isEnd ? "B" : null;
            if (letter != null)
                canvas.DrawText(letter, pt.X, pt.Y + 4f / sc, SKTextAlign.Center, stepFont, stepTxt);

            // Подпись с именем узла (waypoints — без подписи всегда)
            if (!IsAdminMode && !node.IsWaypoint && !string.IsNullOrWhiteSpace(node.Name) && !node.IsFireExtinguisher)
            {
                var lbl  = node.Name;
                var lblW = lblFont.MeasureText(lbl, lblPaint);
                float pillX = pt.X - lblW / 2f - 5f / sc;
                float pillY = pt.Y + cr + 3f / sc;
                var pill = new SKRect(pillX, pillY, pillX + lblW + 10f / sc, pillY + 15f / sc);
                using var pillBg = new SKPaint { Color = new SKColor(255, 255, 255, 230), IsAntialias = true };
                canvas.DrawRoundRect(pill, 4f / sc, 4f / sc, pillBg);
                canvas.DrawText(lbl, pt.X, pillY + 12f / sc, SKTextAlign.Center, lblFont, lblPaint);
            }
        }
    }

    /// <summary>Рисует стрелку-шеврон в середине сегмента from→to (координаты в SVG-пространстве).</summary>
    private static void DrawArrow(SKCanvas canvas, SKPoint from, SKPoint to, SKPaint paint, float sc)
    {
        var dx  = to.X - from.X;
        var dy  = to.Y - from.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        // Минимальная длина сегмента в SVG-единицах (при sc≈1 примерно 30px)
        if (len < 30f / sc) return;

        var mx = (from.X + to.X) / 2f;
        var my = (from.Y + to.Y) / 2f;
        var ux = dx / len;
        var uy = dy / len;

        float hs = 7f / sc;
        var tip  = new SKPoint(mx + ux * hs, my + uy * hs);
        var left = new SKPoint(mx - ux * hs - uy * hs, my - uy * hs + ux * hs);
        var right= new SKPoint(mx - ux * hs + uy * hs, my - uy * hs - ux * hs);

        using var path = new SKPath();
        path.MoveTo(left); path.LineTo(tip); path.LineTo(right);

        using var arrowP = paint.Clone();
        arrowP.IsStroke    = true;
        arrowP.StrokeWidth = 2.5f / sc;
        arrowP.StrokeCap   = SKStrokeCap.Round;
        arrowP.StrokeJoin  = SKStrokeJoin.Round;
        canvas.DrawPath(path, arrowP);
    }

    private static SKMatrix FitMatrix(SKRect src, float dstW, float dstH)
    {
        float scale = Math.Min(dstW / src.Width, dstH / src.Height) * 0.95f;
        float tx = (dstW - src.Width  * scale) / 2f - src.Left * scale;
        float ty = (dstH - src.Height * scale) / 2f - src.Top  * scale;
        return SKMatrix.CreateScaleTranslation(scale, scale, tx, ty);
    }

    /// <summary>Cбрасывает зум на исходный (FitMatrix).</summary>
    public void ResetZoom()
    {
        _matrix = SKMatrix.Identity;
        _totalRotationDeg = 0f;
        InvalidateSurface();
    }

    /// <summary>
    /// Применяет действие зума немедленно если этаж уже загружен, иначе откладывает до завершения загрузки.
    /// Передайте null для сброса зума.
    /// </summary>
    public void ApplyOrQueueZoom(Action? zoomAction)
    {
        if (!_floorLoading && (_bitmap != null || _picture != null))
        {
            _pendingZoom = null;
            if (zoomAction != null)
                zoomAction();
            else
            {
                _matrix = SKMatrix.Identity;
                _totalRotationDeg = 0f;
                InvalidateSurface();
            }
        }
        else
        {
            // Этаж ещё загружается — применим зум когда он прогрузится
            _pendingZoom = zoomAction;
        }
    }

    /// <summary>После загрузки этажа: применяет отложенный зум или сбрасывает матрицу для FitMatrix.</summary>
    private void ApplyPendingZoomOrFit()
    {
        if (_pendingZoom != null)
        {
            var zoom = _pendingZoom;
            _pendingZoom = null;
            zoom();
        }
        else
        {
            _matrix = SKMatrix.Identity;
            _totalRotationDeg = 0f;
            InvalidateSurface();
        }
    }

    /// <summary>Программно центрирует и приближает указанную SVG-точку (без ограничения MaxZoom).</summary>
    public void ZoomToSvgPoint(float svgX, float svgY)
    {
        if (_canvasW <= 0 || _canvasH <= 0) return;

        float svgW = _svgBounds.Width  > 0 ? _svgBounds.Width  : 800f;
        float svgH = _svgBounds.Height > 0 ? _svgBounds.Height : 600f;

        // Показываем окно ~38% от меньшей из сторон SVG вокруг узла
        float window = Math.Min(svgW, svgH) * 0.38f;
        float scale  = Math.Min(_canvasW, _canvasH) / window;

        float tx = _canvasW / 2f - svgX * scale;
        float ty = _canvasH / 2f - svgY * scale;
        _matrix = SKMatrix.CreateScaleTranslation(scale, scale, tx, ty);
        InvalidateSurface();
    }

    /// <summary>Зумирует карту на указанную SVG-область с равномерным отступом (20%).</summary>
    public void ZoomToFitRect(float minX, float minY, float maxX, float maxY)
    {
        if (_canvasW <= 0 || _canvasH <= 0) return;

        float w = maxX - minX;
        float h = maxY - minY;
        if (w < 1f && h < 1f) return;

        // Отступ 20% от большей из сторон, минимум 40 единиц SVG
        float padding = Math.Max(w, h) * 0.20f + 40f;
        float pMinX = minX - padding;
        float pMinY = minY - padding;
        float pMaxX = maxX + padding;
        float pMaxY = maxY + padding;
        float rectW = pMaxX - pMinX;
        float rectH = pMaxY - pMinY;

        float scale = Math.Min(_canvasW / rectW, _canvasH / rectH);
        float cx = (pMinX + pMaxX) / 2f;
        float cy = (pMinY + pMaxY) / 2f;
        float tx = _canvasW / 2f - cx * scale;
        float ty = _canvasH / 2f - cy * scale;
        _matrix = SKMatrix.CreateScaleTranslation(scale, scale, tx, ty);
        InvalidateSurface();
    }

    // ===== Touch handling =====

    private const float NodeHitRadius = 24f;
    private const float BoundaryVertexHitRadius = 20f;
    private readonly Dictionary<long, SKPoint> _activePointers = new();

    /// <summary>Переводит экранный тап (e.Location) в SVG-координаты.</summary>
    private SKPoint ToSvg(SKPoint screen)
    {
        _matrix.TryInvert(out var inv);
        return inv.MapPoint(screen);
    }

    /// <summary>Ищет узел, ближайший к точке тапа (в экранных координатах).</summary>
    private NavNode? HitNode(SKPoint screen)
    {
        var nodes = Nodes;
        if (nodes == null) return null;

        var svgPt = ToSvg(screen);

        // 1) Проверка границ (в польз. режиме)
        if (!IsAdminMode)
        {
            foreach (var n in nodes)
            {
                if (n.IsWaypoint) continue;
                if (n.Boundaries != null)
                    foreach (var poly in n.Boundaries)
                        if (poly.Count >= 3 && PointInPolygon(svgPt, poly))
                            return n;
            }
        }

        // 2) Проверка кружками (NodeHitRadius в экранных пикселях)
        NavNode? best = null;
        float bestDist = NodeHitRadius;
        foreach (var n in nodes)
        {
            var screenPt = _matrix.MapPoint(new SKPoint(n.X, n.Y));
            float d = Distance(screen, screenPt);
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }

    /// <summary>Ищет вершину границы выбранного узла, ближайшую к экранной точке. Возвращает (polyIdx, vtxIdx) или (-1,-1).</summary>
    private (int polyIdx, int vtxIdx) HitBoundaryVertex(SKPoint screen)
    {
        var selNode = SelectedNode;
        if (!IsAdminMode || selNode?.Boundaries == null || selNode.Boundaries.Count == 0)
            return (-1, -1);
        int bestPoly = -1, bestVtx = -1;
        float bestDist = BoundaryVertexHitRadius;
        for (int pi = 0; pi < selNode.Boundaries.Count; pi++)
        {
            var poly = selNode.Boundaries[pi];
            for (int vi = 0; vi < poly.Count; vi++)
            {
                var pt = _matrix.MapPoint(new SKPoint(poly[vi][0], poly[vi][1]));
                float d = Distance(screen, pt);
                if (d < bestDist) { bestDist = d; bestPoly = pi; bestVtx = vi; }
            }
        }
        return (bestPoly, bestVtx);
    }

    /// <summary>Ray-casting point-in-polygon. Координаты SVG-пространства.</summary>
    private static bool PointInPolygon(SKPoint pt, List<float[]> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = poly[i][0], yi = poly[i][1];
            float xj = poly[j][0], yj = poly[j][1];
            if ((yi > pt.Y) != (yj > pt.Y) &&
                pt.X < (xj - xi) * (pt.Y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _activePointers[e.Id] = e.Location;
                _didDrag = false;
                _draggingBoundaryPolyIdx   = -1;
                _draggingBoundaryVertexIdx = -1;
                _touchAngleInitialized     = false;   // сброс угла вращения при новом касании
                StartPanRender();
                if (IsAdminMode && _activePointers.Count == 1)
                {
                    // Сначала проверяем вершины границы (приоритет над узлом)
                    var (hitPoly, hitVtx) = HitBoundaryVertex(e.Location);
                    if (hitVtx >= 0)
                    {
                        _draggingBoundaryPolyIdx   = hitPoly;
                        _draggingBoundaryVertexIdx = hitVtx;
                        _draggingNode = null;
                    }
                    else if (IsDragMode)
                    {
                        _draggingNode = HitNode(e.Location);
                    }
                }
                break;

            case SKTouchAction.Moved:
                if (!_activePointers.ContainsKey(e.Id)) break;
                var prev = _activePointers[e.Id];
                _activePointers[e.Id] = e.Location;

                if (_activePointers.Count == 1)
                {
                    if (Distance(e.Location, prev) > 3f) _didDrag = true;

                    if (IsAdminMode && _draggingBoundaryVertexIdx >= 0)
                    {
                        // Перемещение вершины границы
                        BoundaryVertexMoved?.Invoke(this, (_draggingBoundaryPolyIdx, _draggingBoundaryVertexIdx, ToSvg(e.Location)));
                        InvalidateSurface();
                    }
                    else if (IsAdminMode && IsDragMode && _draggingNode != null)
                    {
                        // Режим перемещения узла: двигаем только узел, карта стоит на месте
                        NodeMoved?.Invoke(this, (_draggingNode, ToSvg(e.Location)));
                        InvalidateSurface();
                    }
                    else if (!IsDragMode || (_draggingNode == null && _draggingBoundaryVertexIdx < 0))
                    {
                        // Обычный режим или в режиме перемещения нажали на пустое место — двигаем карту
                        // В IsDragMode без захваченного узла тоже разрешаем панорамирование
                        _matrix = _matrix.PostConcat(
                            SKMatrix.CreateTranslation(e.Location.X - prev.X, e.Location.Y - prev.Y));
                        ClampPan();
                        // Не вызываем InvalidateSurface() напрямую — таймер отрисует в наступающем 16мс тике
                        _panDirty = true;
                    }
                }
                else if (_activePointers.Count == 2)
                {
                    _draggingNode = null;
                    var pts      = _activePointers.Values.ToArray();
                    var curDist  = Distance(pts[0], pts[1]);
                    var otherKey = _activePointers.Keys.First(k => k != e.Id);
                    var prevDist = Distance(prev, _activePointers[otherKey]);

                    // Pinch-zoom
                    if (prevDist > 0)
                    {
                        float s = curDist / prevDist;
                        var pivot = MidPoint(pts[0], pts[1]);
                        ApplyZoom(s, pivot);
                    }

                    // Two-finger rotation
                    var other = _activePointers[otherKey];
                    float curAngle = MathF.Atan2(other.Y - e.Location.Y, other.X - e.Location.X);
                    if (_touchAngleInitialized)
                    {
                        float deltaRad = curAngle - _lastTouchAngleRad;
                        // Нормализация дельты в диапазон [-π .. +π]
                        while (deltaRad >  MathF.PI) deltaRad -= 2f * MathF.PI;
                        while (deltaRad < -MathF.PI) deltaRad += 2f * MathF.PI;
                        float deltaDeg = deltaRad * (180f / MathF.PI);
                        var pivot = MidPoint(pts[0], pts[1]);
                        ApplyRotation(deltaDeg, pivot);
                    }
                    _lastTouchAngleRad      = curAngle;
                    _touchAngleInitialized  = true;
                }
                break;

            case SKTouchAction.Released:
                if (_activePointers.ContainsKey(e.Id) && !_didDrag)
                {
                    if (IsAdminMode && _draggingBoundaryVertexIdx >= 0)
                    {
                        // Тап по вершине границы (без перемещения)
                        BoundaryVertexTapped?.Invoke(this, (_draggingBoundaryPolyIdx, _draggingBoundaryVertexIdx));
                    }
                    else
                    {
                        var hit = HitNode(e.Location);
                        // Запрещаем обычным пользователям нажимать на QR-точки
                        if (hit != null && (!hit.IsQrAnchor || IsAdminMode))
                            NodeTapped?.Invoke(this, hit);
                        else if (hit == null && IsAdminMode) CanvasTapped?.Invoke(this, ToSvg(e.Location));
                    }
                }
                _activePointers.Remove(e.Id);
                if (_activePointers.Count == 0)
                {
                    _draggingNode = null;
                    _draggingBoundaryPolyIdx   = -1;
                    _draggingBoundaryVertexIdx = -1;
                    StopPanRender();
                    InvalidateSurface(); // последний кадр по остановке
                }
                break;

            case SKTouchAction.Cancelled:
                _activePointers.Clear();
                _draggingNode = null;
                _draggingBoundaryPolyIdx   = -1;
                _draggingBoundaryVertexIdx = -1;
                StopPanRender();
                break;
        }
    }

    /// <summary>
    /// Применяет масштабирование с ограничением зума.
    /// <para>factor — желаемый коэффициент; pivot — точка в экранных координатах.</para>
    /// Если итоговый масштаб выйдет за [<see cref="MinZoom"/>, <see cref="MaxZoom"/>],
    /// factor автоматически обрезается до границы.
    /// </summary>
    private void ApplyZoom(float factor, SKPoint pivot)
    {
        float current = MatrixScale();
        if (current < 0.0001f) return;

        float target = current * factor;

        // Зажимаем до границ
        if (target < MinZoom) factor = MinZoom / current;
        else if (target > MaxZoom) factor = MaxZoom / current;

        // Применяем только если есть реальное изменение
        if (MathF.Abs(factor - 1f) > 0.0001f)
        {
            _matrix = _matrix.PostConcat(SKMatrix.CreateScale(factor, factor, pivot.X, pivot.Y));
            ClampPan();
            InvalidateSurface();
        }
    }
    

    private static float Distance(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static SKPoint MidPoint(SKPoint a, SKPoint b) =>
        new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
}
