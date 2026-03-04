# IndoorNav — Навигация по зданию

Мобильное и десктопное приложение для навигации внутри зданий. Построено на **.NET 9 MAUI**.

## Документация

| Документ | Описание |
|----------|---------|
| [📋 PRD](IndoorNav/PRD.md) | Требования к продукту: роли, функциональные требования, архитектура, user flows |

---

## Поддерживаемые платформы

| Платформа | Статус |
|-----------|--------|
| Windows | ✅ |
| Android | ✅ |
| iOS / macOS | ✅ (требует Mac для сборки) |

---

## Возможности

### Навигация
- Просмотр планов этажей зданий (SVG → WebP, рендер на SkiaSharp)
- Построение маршрутов с поддержкой **многоэтажных переходов**
- Анимированный маршрут с пульсирующей подсветкой
- Пошаговые инструкции (карточки шагов с переключением этажей)
- Поиск аудиторий по названию, псевдониму и ключевым словам
- Плавное панирование карты с частотой обновления монитора (VSync)
- Масштабирование колесом мыши / двумя пальцами

### QR-навигация (Android / iOS)
- Сканирование QR-кодов на стенах для автоматической установки точки «Я здесь»
- Deep link: `indoornav://<nodeId>` — открывает приложение и прыгает к узлу

### Режим ЧС (чрезвычайная ситуация)
- Администратор активирует ЧС по каждому зданию или сразу по всем
- Автоматическое построение маршрута к ближайшему эвакуационному выходу
- Во время ЧС обычная навигация заблокирована
- Узлы-выходы видны только в режиме ЧС

### Расписание
- Студент видит своё расписание пар
- Администратор управляет записями: здание, аудитория, группа, преподаватель, время, повтор по дню недели
- Автоматическое определение местоположения студента по текущей паре

### Аутентификация и пользователи
- Роли: **Администратор** и **Студент**
- Смена пароля для обеих ролей
- Привязка студента к группе (факультет → группа)

### Управление структурой (только Администратор)
- Факультеты и группы: создание, переименование, удаление
- Студенты: добавление, редактирование, смена пароля, назначение группы
- Расписание: добавление / редактирование / удаление записей, очистка всех

### Редактор карты (только Администратор)
- Добавление, перемещение, удаление узлов и рёбер
- Типы узлов: обычный, аудитория, огнетушитель, QR-якорь, эвакуационный выход, переход между этажами
- Настройка вида: цвет, размер, скрытие, надпись, категория, ключевые слова поиска
- Рисование полигональных **границ аудитории** для корректного определения местоположения
- Генерация и отображение QR-кода узла прямо в приложении

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
IndoorNav/                              # Основной проект MAUI
├── Controls/
│   └── SvgView.cs                     # Контрол карты (SkiaSharp, VSync-рендер)
├── Models/
│   ├── NavNode.cs                     # Узел графа навигации
│   ├── NavEdge.cs                     # Ребро графа
│   ├── Building.cs / Floor.cs         # Здание и этаж
│   ├── RouteStep.cs                   # Шаг пошагового маршрута
│   ├── Department.cs                  # Факультет, группа
│   ├── AuthUser.cs                    # Пользователь (admin / student)
│   └── ScheduleEntry.cs              # Запись расписания
├── Services/
│   ├── NavGraphService.cs             # Загрузка/сохранение графа
│   ├── AuthService.cs                 # Аутентификация, смена пароля
│   ├── EmergencyService.cs            # Режим ЧС
│   ├── ScheduleService.cs             # Расписание пар
│   ├── DepartmentService.cs           # Факультеты и группы
│   ├── QrService.cs                   # QR-коды
│   └── DeepLinkService.cs             # Разбор deep link URI
├── ViewModels/
│   ├── MainViewModel.cs               # Навигация, маршруты, ЧС
│   ├── AdminViewModel.cs              # Редактор карты, пользователи, расписание
│   └── LoginViewModel.cs              # Вход в систему
├── Pages/
│   ├── LoginPage.xaml                 # Экран входа
│   ├── AdminPage.xaml                 # Панель администратора
│   └── QrScanPage.xaml                # Камера QR-сканера (Android/iOS)
├── Converters/                        # MAUI value converters
├── MainPage.xaml                      # Главная страница навигации
└── Resources/
    ├── Raw/FloorImages/               # Планы этажей в формате WebP
    └── Raw/navgraph.json              # Граф навигации (узлы, рёбра, здания)

Tools/GenerateFloorImages/             # Утилита: генерация WebP из SVG
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
- [ZXing.Net.Maui](https://github.com/Redth/ZXing.Net.Maui) — QR-сканер на Android/iOS