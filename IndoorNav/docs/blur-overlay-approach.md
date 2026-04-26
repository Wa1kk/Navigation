# Blur Overlay — подход и архитектура

## Суть

Полноэкранный blur-эффект за popup-карточками на iOS реализован через нативный `UIVisualEffectView`, встроенный в MAUI-контрол `BlurOverlay`.

## Компоненты

### 1. `BlurOverlay.cs` (Controls/BlurOverlay.cs)

Кастомный `ContentView`, который при привязке к нативному рендереру:

1. **Очищает `BackgroundColor`** нативного `UIView` в `UIColor.Clear` — иначе MAUI-фон перекрывает blur
2. **Создаёт `UIVisualEffectView`** со стилем `SystemUltraThinMaterialLight` — лёгкий матовый blur
3. **Добавляет tint overlay** — полупрозрачный тёмный слой (`RGBA 0,0,0,0x33`) поверх blur для затемнения
4. Оба subview привязываются через AutoLayout к краям `uiView` (edge-to-edge)

```
UIVisualEffectView (blur)  ← insertSubview(0)
Tint UIView (darkening)    ← insertSubview(1)
```

### 2. XAML — размещение backdrop

Backdrop размещается **на уровне MainGrid** с `Grid.ColumnSpan="2"`, что позволяет ему покрывать весь экран, включая safe area (Dynamic Island, Home Indicator).

```xml
<controls:BlurOverlay x:Name="NodePopupBackdrop"
                      Grid.ColumnSpan="2"
                      IsVisible="{Binding IsNodePopupOpen}">
    <controls:BlurOverlay.GestureRecognizers>
        <TapGestureRecognizer Command="{Binding CloseNodePopupCommand}" />
    </controls:BlurOverlay.GestureRecognizers>
</controls:BlurOverlay>

<!-- Popup card — ВЫШЕ backdrop в Z-порядке -->
<Border x:Name="NodePopupCard" Grid.ColumnSpan="2" ... />
```

**Порядок в XAML = Z-порядок**: элементы ниже в разметке отображаются поверх предыдущих.

### 3. Code-behind — анимации

Popup show/hide управляется через `IsVisible` + анимации масштаба/прозрачности:

```csharp
private async Task ShowNodePopupAsync()
{
    NodePopupBackdrop.IsVisible = true;   // → BlurOverlay создаёт нативный blur
    NodePopupCard.Scale = 0.85;
    NodePopupCard.Opacity = 0;
    NodePopupCard.IsVisible = true;

    await Task.WhenAll(
        NodePopupCard.ScaleTo(1, 250, Easing.SpringOut),
        NodePopupCard.FadeTo(1, 180, Easing.Linear)
    );
}
```

## Где используется

| Popup            | Backdrop name            | Размещение              |
|------------------|--------------------------|-------------------------|
| Node tap         | `NodePopupBackdrop`      | MainGrid, ColumnSpan=2  |
| User menu        | `UserMenuBackdrop`       | MainGrid, ColumnSpan=2  |
| Building picker  | `BuildingPickerBackdrop` | Inner Grid, RowSpan=3   |
| Node picker      | `NodePickerBackdrop`     | Inner Grid, RowSpan=3   |

## Почему не другие подходы

### ❌ UIVisualEffectView в UIWindow
- Blur добавляется поверх **всего** контента, включая popup-карточки
- При вставке ПОД MAUI root view — blur невидим (opaque фон страницы перекрывает)
- Невозможно контролировать Z-порядок между blur и popup

### ❌ Скриншот + CIFilter
- Требует `UIGraphics.BeginImageContextWithOptions` — нет в MAUI без ObjC runtime
- `CIImage.ImageFilterKeyInputImage` не существует в .NET binding
- `NSData.GetUrl()` не существует
- Высокая задержка на скриншот + blur при каждом открытии popup

### ✅ BlurOverlay (текущий подход)
- Blur встроен **внутрь** MAUI view hierarchy — корректный Z-порядок
- `UIVisualEffectView` автоматически размывает контент **за ним** (нижележащие view)
- Popup карточка выше backdrop — не размыта, интерактивна
- Нет проблем с opaque фоном — `BackgroundColor = Clear`
- Работает мгновенно, без скриншотов

## Edge-to-edge (полноэкранный blur)

Blur привязывается к **UIWindow** через AutoLayout constraints, а не к самому `uiView`. Это позволяет blur покрывать весь экран, включая Dynamic Island и Home Indicator.

```csharp
// В BlurOverlay.cs → InstallBlur()
blurView.LeadingAnchor.ConstraintEqualTo(window.LeadingAnchor).Active = true;
blurView.TrailingAnchor.ConstraintEqualTo(window.TrailingAnchor).Active = true;
blurView.TopAnchor.ConstraintEqualTo(window.TopAnchor).Active = true;
blurView.BottomAnchor.ConstraintEqualTo(window.BottomAnchor).Active = true;
```

### Почему привязка к UIWindow, а не к uiView

MAUI ContentPage может ограничивать размер native view safe area insets, даже при `Padding="0"`. Привязка к `window` гарантирует, что blur растягивается до физических краёв экрана.

### Задержка установки

`uiView.Window` может быть `null` при `OnHandlerChanged` — view ещё не добавлен в иерархию. В этом случае используется `DispatcherTimer` (50мс) для повторной проверки:

```csharp
if (window != null)
    InstallBlur(uiView, window);
else
{
    var timer = Dispatcher.CreateTimer();
    timer.Interval = TimeSpan.FromMilliseconds(50);
    timer.Tick += (_, _) => { timer.Stop(); InstallBlur(uiView, uiView.Window!); };
    timer.Start();
}
```

### Дополнительные настройки

- **`Padding="0"`** на ContentPage — убирает MAUI padding
- **`Shell.NavBarIsVisible="False"`** — убирает навигационную панель Shell
- **`uiView.BackgroundColor = UIColor.Clear`** + **`uiView.Opaque = false`** — нативный фон прозрачный, иначе MAUI фон перекрывает blur
- **Фон UIWindow** установлен в `#F1F5F9` через `WindowHandler.Mapper` — совпадает с фоном страницы
