# IndoorNav — Навигация по зданию

Мобильное и десктопное приложение для навигации внутри зданий. Построено на **.NET 9 MAUI**.

## Поддерживаемые платформы

| Платформа | Статус |
|-----------|--------|
| Windows | ✅ |
| Android | ✅ |
| iOS / macOS | ✅ (требует Mac для сборки) |

---

## Требования

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- MAUI workload: `dotnet workload install maui`
- Для Android: Android SDK (устанавливается вместе с Visual Studio или вручную)
- Для iOS/macOS: Mac с Xcode

---

## Запуск

### Windows
```bash
cd IndoorNav
dotnet run -f net9.0-windows10.0.19041.0
```

### Android (через USB)
```bash
# Сборка
dotnet build IndoorNav/IndoorNav.csproj -f net9.0-android -c Release

# Установка на подключённый телефон
adb install -r "IndoorNav/bin/Release/net9.0-android/com.companyname.indoornav-Signed.apk"
```

> **adb** входит в Android SDK: `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`

---

## Структура проекта

```
IndoorNav/                          # Основной проект MAUI
├── Controls/SvgView.cs             # Кастомный контрол карты (SkiaSharp)
├── Models/                         # NavNode, NavGraph, Building, Floor
├── Services/NavGraphService.cs     # Загрузка/сохранение графа навигации
├── ViewModels/                     # MainViewModel, AdminViewModel
├── Pages/AdminPage.xaml            # Режим редактирования карты
├── MainPage.xaml                   # Главная страница навигации
└── Resources/
    ├── Raw/FloorImages/            # Предгенерированные WebP планы этажей
    └── Raw/navgraph.json           # Граф навигации (узлы и рёбра)

Tools/GenerateFloorImages/          # Утилита генерации WebP из SVG
```

---

## Обновление карт этажей

Планы этажей хранятся в `IndoorNav/Resources/Raw/FloorImages/` в формате WebP (9–52 КБ каждый).

Если SVG-источники изменились — перегенерируй WebP:
```bash
# Сначала собери утилиту
cd Tools/GenerateFloorImages
dotnet build

# Затем запусти (укажи путь к папке IndoorNav)
./bin/Debug/net9.0/GenerateFloorImages.exe "../../IndoorNav"
```

---

## Обновление данных навигации

После редактирования точек на ПК обнови бандл и переустанови на телефон:

```bash
# 1. Скопируй актуальный граф в ресурсы
copy "%LOCALAPPDATA%\User Name\com.companyname.indoornav\Data\navgraph.json" IndoorNav\Resources\Raw\navgraph.json

# 2. Пересобери и установи APK
dotnet build IndoorNav/IndoorNav.csproj -f net9.0-android -c Release
adb install -r "IndoorNav/bin/Release/net9.0-android/com.companyname.indoornav-Signed.apk"
```

---

## Технологии

- [.NET MAUI 9](https://learn.microsoft.com/dotnet/maui/)
- [SkiaSharp](https://github.com/mono/SkiaSharp) — рендеринг карты
- [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) — парсинг SVG
