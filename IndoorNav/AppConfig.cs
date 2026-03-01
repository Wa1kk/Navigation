namespace IndoorNav;

/// <summary>
/// Настройки синхронизации графа между устройствами через GitHub Gist.
///
/// КАК НАСТРОИТЬ:
/// 1. Создайте GitHub Gist на https://gist.github.com
///    - Имя файла: navgraph.json
///    - Содержимое: скопируйте из %LocalAppData%\User Name\com.companyname.indoornav\Data\navgraph.json
/// 2. После создания Gist скопируйте ID из URL:
///    https://gist.github.com/USERNAME/XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX  ← это GistId
/// 3. Создайте Personal Access Token на https://github.com/settings/tokens
///    - Нужно только право: gist
///    - Вставьте токен в GithubToken ниже
/// 4. В GistRawUrl вставьте ссылку на RAW файл:
///    https://gist.githubusercontent.com/USERNAME/GIST_ID/raw/navgraph.json
/// </summary>
public static class AppConfig
{
    // ──────────────────────────────────────────────────────────
    // URL для загрузки графа при запуске приложения (все устройства)
    // Пример: "https://gist.githubusercontent.com/username/abc123/raw/navgraph.json"
    public const string GistRawUrl = "";

    // ──────────────────────────────────────────────────────────
    // Только для публикации с ПК (заполнять не обязательно на телефоне)
    public const string GistId      = "";
    public const string GithubToken = "";

    // ──────────────────────────────────────────────────────────
    public static bool CanFetch   => !string.IsNullOrWhiteSpace(GistRawUrl);
    public static bool CanPublish => !string.IsNullOrWhiteSpace(GistId) &&
                                     !string.IsNullOrWhiteSpace(GithubToken);
}
