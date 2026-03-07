# IndoorNav — Навигация по зданию

Мобильное и десктопное приложение для навигации внутри зданий университета. Построено на **.NET 9 MAUI** с SkiaSharp-рендерингом планов этажей и редактором навигационного графа.

## Документация

| Документ | Описание |
|----------|---------|
| [📋 PRD v2.0](IndoorNav/PRD.md) | Требования к продукту: роли, функциональные требования, архитектура, user flows |

---

## Поддерживаемые платформы

| Платформа | Статус |
|-----------|--------|
| Windows 10/11 | ✅ |
| Android | ✅ |
| iOS / macOS | ✅ (требует Mac для сборки) |

---

## Возможности

### Навигация
- Просмотр планов этажей зданий (SVG → рендер на SkiaSharp)
- Построение маршрутов с поддержкой **многоэтажных переходов** (алгоритм Дейкстры)
- Анимированный маршрут с пульсирующей подсветкой и бегущей точкой
- Пошаговые инструкции (карточки шагов с переключением этажей)
- Поиск аудиторий по названию, псевдониму и ключевым словам
- **Блокировка узла** на маршруте: пользователь помечает непроходимую точку, маршрут пересчитывается в обход
- Плавное панирование карты (VSync) + зум колесом мыши / двумя пальцами
- Выбор корпуса через пикер прямо на карте; переключение этажа через оверлей-кнопки

### QR-навигация
- **Android/iOS:** `QrScanPage` — кнопки «Сфотографировать» и «Выбрать из галереи»; декодирование ZXing
- **Windows:** выбор PNG/JPG через FilePicker; декодирование ZXing `RGBLuminanceSource` (TRY_HARDER)
- После сканирования — баннер «Вы здесь: [имя]» с кнопкой «Начать отсюда»
- Бирюзовый индикатор «Вы тут» над узлом на карте
- Deep link: `indoornav://node/{guid}`

### Режим ЧС
- Администратор активирует ЧС по каждому зданию или сразу по всем
- Все пользователи немедленно видят баннер с сообщением
- Автоматическое построение маршрута к ближайшему эвакуационному выходу
- Во время ЧС на карте появляются огнетушители и запасные выходы

### Расписание
- Студент видит текущую и следующую пару своей группы
- Одно нажатие — маршрут до аудитории из расписания
- Администратор управляет расписанием: здание, аудитория, группа, преподаватель, время, повтор по дням

### Аутентификация
- Роли: **Администратор**, **Студент**, **Гость**
- Вход без аккаунта (гость) одним кликом
- Пароли хранятся как SHA-256 хэш

### Редактор карты (только Администратор)
- Добавление, перемещение, удаление узлов и рёбер
- Типы узлов: аудитория, коридор, лестница, лифт, выход, запасной выход, огнетушитель, QR-якорь
- **PNG-иконки** вместо кружков для узлов; назначаются через FilePicker
- **Множественный выбор** узлов (Ctrl+клик / rubber-band); групповые операции
- При размещении QR-якоря вблизи ребра — ребро **автоматически разрезается** через новый узел
- Настройка вида: цвет, размер, скрытие, emoji-метка, категория, теги поиска
- Рисование полигональных **границ аудитории**
- Генерация QR-кода узла + копирование URL + скачивание PNG

---

## Требования

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- MAUI workload: `dotnet workload install maui`
- Для Android: Android SDK
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
dotnet build IndoorNav/IndoorNav.csproj -f net9.0-android -c Release
adb install -r "IndoorNav/bin/Release/net9.0-android/com.companyname.indoornav-Signed.apk"
```

> **adb** входит в Android SDK: `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`

---

## Структура проекта

```
IndoorNav/
├── Controls/
│   └── SvgView.cs                     # Контрол карты (SkiaSharp, VSync-рендер, иконки, "Вы тут")
├── Models/
│   ├── NavNode.cs                     # Узел графа (тип, координаты, иконка, границы)
│   ├── NavEdge.cs                     # Ребро графа
│   ├── NavGraph.cs                    # Граф + алгоритм Дейкстры
│   ├── Building.cs / Floor.cs         # Здание и этаж
│   ├── RouteStep.cs                   # Шаг пошагового маршрута
│   ├── Department.cs                  # Кафедра / учебная группа
│   ├── AuthUser.cs                    # Пользователь (admin / student)
│   └── ScheduleEntry.cs               # Запись расписания
├── Services/
│   ├── NavGraphService.cs             # Загрузка/сохранение графа, CRUD узлов/рёбер
│   ├── AuthService.cs                 # Аутентификация, роли, смена пароля
│   ├── EmergencyService.cs            # Режим ЧС
│   ├── ScheduleService.cs             # Расписание пар
│   ├── DepartmentService.cs           # Кафедры и группы
│   ├── QrService.cs                   # Генерация + декодирование QR (ZXing)
│   └── DeepLinkService.cs             # Разбор deep link URI
├── ViewModels/
│   ├── MainViewModel.cs               # Навигация, QR, ЧС, расписание, выбор корпуса/этажа
│   ├── AdminViewModel.cs              # Редактор карты, пользователи, группы, расписание
│   ├── LoginViewModel.cs              # Вход в систему
│   └── FloorSelectorVm.cs             # Оверлей выбора этажа + пикер корпуса
├── Pages/
│   ├── LoginPage.xaml                 # Экран входа
│   ├── AdminPage.xaml                 # Панель администратора
│   └── QrScanPage.xaml                # QR-сканер (камера/галерея, Android/iOS)
├── Converters/                        # MAUI value converters
├── MainPage.xaml                      # Главная страница навигации
└── Resources/
    ├── Raw/SvgFloors/                 # SVG-планы этажей
    ├── Raw/Icons/                     # PNG-иконки узлов
    └── Raw/navgraph.json              # Граф навигации (узлы, рёбра, здания)

Tools/GenerateFloorImages/             # Утилита: генерация WebP из SVG
```

---

## Обновление карт этажей

Планы этажей хранятся как SVG в `IndoorNav/Resources/Raw/SvgFloors/`.

---

## Обновление данных навигации

После редактирования в панели администратора данные сохраняются в `navgraph.json` через UI.  
Для обновления бандла на телефоне:

```bash
# 1. Скопируй актуальный граф в ресурсы
copy "%LOCALAPPDATA%\...\Data\navgraph.json" IndoorNav\Resources\Raw\navgraph.json

# 2. Пересобери и установи APK
dotnet build IndoorNav/IndoorNav.csproj -f net9.0-android -c Release
adb install -r "IndoorNav/bin/Release/net9.0-android/com.companyname.indoornav-Signed.apk"
```

---

## Технологии

| Библиотека | Версия | Назначение |
|-----------|--------|-----------|
| [.NET MAUI 9](https://learn.microsoft.com/dotnet/maui/) | 9.0 | Кроссплатформенный UI |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | 3.119.2 | 2D-рендеринг карты |
| [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) | 3.4.1 | Парсинг и рендер SVG |
| [QRCoder](https://github.com/codebude/QRCoder) | 1.4.3 | Генерация QR PNG |
| [ZXing.Net.Maui](https://github.com/Redth/ZXing.Net.Maui) | 0.4.0 | Декодирование QR (камера + изображение) |
