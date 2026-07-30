using System.Collections;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace My_Fancy_Fences;

public static class LocalizationService
{
    private static readonly string LanguageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "My Fancy Fences",
        "language.txt");

    private static readonly DependencyProperty OriginalTextProperty =
        DependencyProperty.RegisterAttached(
            "OriginalText",
            typeof(string),
            typeof(LocalizationService));

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("pl", "Polski"), new("en", "English"), new("de", "Deutsch"),
        new("fr", "Français"), new("es", "Español"), new("it", "Italiano"),
        new("pt", "Português"), new("nl", "Nederlands"), new("cs", "Čeština"),
        new("uk", "Українська"), new("ru", "Русский"), new("zh-CN", "简体中文")
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Translations =
        CreateTranslations();

    public static string CurrentLanguage { get; private set; } = "pl";

    public static void Initialize()
    {
        try
        {
            var saved = File.Exists(LanguageFilePath)
                ? File.ReadAllText(LanguageFilePath).Trim()
                : string.Empty;
            if (Languages.Any(language => language.Code == saved))
                CurrentLanguage = saved;
            else
                CurrentLanguage = DetectSystemLanguage();
        }
        catch
        {
            CurrentLanguage = DetectSystemLanguage();
        }
    }

    private static string DetectSystemLanguage()
    {
        var culture = CultureInfo.InstalledUICulture;
        var fullCode = culture.Name;
        var neutralCode = culture.TwoLetterISOLanguageName;

        if (neutralCode.Equals("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";

        var supported = Languages.FirstOrDefault(language =>
            language.Code.Equals(fullCode, StringComparison.OrdinalIgnoreCase) ||
            language.Code.Equals(neutralCode, StringComparison.OrdinalIgnoreCase));
        return supported?.Code ?? "en";
    }

    public static void SetLanguage(string code)
    {
        if (!Languages.Any(language => language.Code == code))
            return;

        CurrentLanguage = code;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LanguageFilePath)!);
            File.WriteAllText(LanguageFilePath, code);
        }
        catch
        {
            // A read-only profile should not prevent changing the current UI.
        }

        foreach (Window window in Application.Current.Windows)
            Apply(window);
    }

    public static string T(string source)
    {
        if (CurrentLanguage == "pl")
            return source;

        return Translations.TryGetValue(CurrentLanguage, out var language) &&
               language.TryGetValue(source, out var translated)
            ? translated
            : source;
    }

    public static void Apply(Window window)
    {
        var visited = new HashSet<DependencyObject>();
        ApplyElement(window, visited);
    }

    private static void ApplyElement(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element))
            return;

        switch (element)
        {
            case Window window:
                window.Title = TranslateStored(window, window.Title);
                break;
            case TextBlock textBlock:
                if (textBlock.Inlines.Count > 1)
                {
                    foreach (var run in textBlock.Inlines.OfType<Run>())
                        run.Text = TranslateStored(run, run.Text);
                }
                else if (!BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
                {
                    textBlock.Text = TranslateStored(textBlock, textBlock.Text);
                }
                break;
            case HeaderedContentControl headered when headered.Header is string header:
                headered.Header = TranslateStored(headered, header);
                break;
            case ContentControl contentControl
                when contentControl.Content is string content &&
                     !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty):
                contentControl.Content = TranslateStored(contentControl, content);
                break;
        }

        if (element is FrameworkElement frameworkElement && frameworkElement.ToolTip is string tooltip)
            frameworkElement.ToolTip = T(tooltip);

        foreach (var child in GetLogicalChildren(element))
            ApplyElement(child, visited);
    }

    private static string TranslateStored(DependencyObject element, string current)
    {
        if (string.IsNullOrWhiteSpace(current))
            return current;

        var original = (string?)element.GetValue(OriginalTextProperty);
        if (string.IsNullOrEmpty(original))
        {
            original = current;
            element.SetValue(OriginalTextProperty, original);
        }

        return T(original);
    }

    private static IEnumerable<DependencyObject> GetLogicalChildren(DependencyObject parent)
    {
        IEnumerable children;
        try
        {
            children = LogicalTreeHelper.GetChildren(parent);
        }
        catch
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child is DependencyObject dependencyObject)
                yield return dependencyObject;
        }
    }

    private static Dictionary<string, Dictionary<string, string>> CreateTranslations()
    {
        var result = Languages
            .Where(language => language.Code != "pl")
            .ToDictionary(language => language.Code, _ => new Dictionary<string, string>());

        void Add(string pl, string en, string de, string fr, string es, string it,
            string pt, string nl, string cs, string uk, string ru, string zh)
        {
            result["en"][pl] = en; result["de"][pl] = de; result["fr"][pl] = fr;
            result["es"][pl] = es; result["it"][pl] = it; result["pt"][pl] = pt;
            result["nl"][pl] = nl; result["cs"][pl] = cs; result["uk"][pl] = uk;
            result["ru"][pl] = ru; result["zh-CN"][pl] = zh;
        }

        Add("Ustawienia", "Settings", "Einstellungen", "Paramètres", "Ajustes", "Impostazioni", "Configurações", "Instellingen", "Nastavení", "Налаштування", "Настройки", "设置");
        Add("Ustawienia panelu", "Panel settings", "Panel-Einstellungen", "Paramètres du panneau", "Ajustes del panel", "Impostazioni pannello", "Configurações do painel", "Paneelinstellingen", "Nastavení panelu", "Налаштування панелі", "Настройки панели", "面板设置");
        Add("Ustawienia kreatora", "Creator settings", "Ersteller-Einstellungen", "Paramètres du créateur", "Ajustes del creador", "Impostazioni creazione", "Configurações do criador", "Makerinstellingen", "Nastavení tvůrce", "Налаштування конструктора", "Настройки конструктора", "创建器设置");
        Add("Ogólne", "General", "Allgemein", "Général", "General", "Generali", "Geral", "Algemeen", "Obecné", "Загальні", "Общие", "常规");
        Add("Panele", "Panels", "Panels", "Panneaux", "Paneles", "Pannelli", "Painéis", "Panelen", "Panely", "Панелі", "Панели", "面板");
        Add("Import / eksport", "Import / export", "Import / Export", "Import / export", "Importar / exportar", "Importa / esporta", "Importar / exportar", "Import / export", "Import / export", "Імпорт / експорт", "Импорт / экспорт", "导入 / 导出");
        Add("Aktualizacja", "Update", "Update", "Mise à jour", "Actualización", "Aggiornamento", "Atualização", "Update", "Aktualizace", "Оновлення", "Обновление", "更新");
        Add("Aktualizuj", "Update", "Aktualisieren", "Mettre à jour", "Actualizar", "Aggiorna", "Atualizar", "Bijwerken", "Aktualizovat", "Оновити", "Обновить", "更新");
        Add("Automatyczna aktualizacja", "Automatic update", "Automatisches Update", "Mise à jour automatique", "Actualización automática", "Aggiornamento automatico", "Atualização automática", "Automatische update", "Automatická aktualizace", "Автоматичне оновлення", "Автоматическое обновление", "自动更新");
        Add("Nowa wersja jest gotowa", "A new version is ready", "Eine neue Version ist bereit", "Une nouvelle version est prête", "Hay una nueva versión lista", "Una nuova versione è pronta", "Uma nova versão está pronta", "Een nieuwe versie staat klaar", "Nová verze je připravena", "Нова версія готова", "Новая версия готова", "新版本已准备就绪");
        Add("Program może pobrać i zainstalować najnowszą wersję automatycznie.\n\nCzy chcesz rozpocząć aktualizację?", "The app can download and install the latest version automatically.\n\nDo you want to start the update?", "Die App kann die neueste Version automatisch herunterladen und installieren.\n\nMöchtest du das Update starten?", "L’application peut télécharger et installer automatiquement la dernière version.\n\nVoulez-vous lancer la mise à jour ?", "La aplicación puede descargar e instalar automáticamente la última versión.\n\n¿Quieres iniciar la actualización?", "L’app può scaricare e installare automaticamente la versione più recente.\n\nVuoi avviare l’aggiornamento?", "O aplicativo pode baixar e instalar automaticamente a versão mais recente.\n\nDeseja iniciar a atualização?", "De app kan de nieuwste versie automatisch downloaden en installeren.\n\nWil je de update starten?", "Aplikace může automaticky stáhnout a nainstalovat nejnovější verzi.\n\nChcete spustit aktualizaci?", "Програма може автоматично завантажити й установити останню версію.\n\nПочати оновлення?", "Приложение может автоматически скачать и установить последнюю версию.\n\nНачать обновление?", "应用可以自动下载并安装最新版本。\n\n是否开始更新？");
        Add("Nie teraz", "Not now", "Nicht jetzt", "Pas maintenant", "Ahora no", "Non ora", "Agora não", "Niet nu", "Teď ne", "Не зараз", "Не сейчас", "暂不");
        Add("Czy zainstalować aktualizację automatycznie?", "Install the update automatically?", "Update automatisch installieren?", "Installer la mise à jour automatiquement ?", "¿Instalar la actualización automáticamente?", "Installare automaticamente l’aggiornamento?", "Instalar a atualização automaticamente?", "De update automatisch installeren?", "Nainstalovat aktualizaci automaticky?", "Встановити оновлення автоматично?", "Установить обновление автоматически?", "是否自动安装更新？");
        Add("Tak", "Yes", "Ja", "Oui", "Sí", "Sì", "Sim", "Ja", "Ano", "Так", "Да", "是");
        Add("Nie", "No", "Nein", "Non", "No", "No", "Não", "Nee", "Ne", "Ні", "Нет", "否");
        Add("Sprawdź ponownie", "Check again", "Erneut prüfen", "Vérifier à nouveau", "Comprobar de nuevo", "Controlla di nuovo", "Verificar novamente", "Opnieuw controleren", "Zkontrolovat znovu", "Перевірити знову", "Проверить снова", "重新检查");
        Add("Sprawdzanie aktualizacji…", "Checking for updates…", "Suche nach Updates…", "Recherche de mises à jour…", "Buscando actualizaciones…", "Controllo aggiornamenti…", "Verificando atualizações…", "Controleren op updates…", "Kontrola aktualizací…", "Перевірка оновлень…", "Проверка обновлений…", "正在检查更新…");
        Add("sprawdzanie…", "checking…", "wird geprüft…", "vérification…", "comprobando…", "controllo…", "verificando…", "controleren…", "kontrola…", "перевірка…", "проверка…", "检查中…");
        Add("Nie udało się połączyć z GitHubem", "Could not connect to GitHub", "Verbindung zu GitHub fehlgeschlagen", "Connexion à GitHub impossible", "No se pudo conectar con GitHub", "Impossibile connettersi a GitHub", "Não foi possível conectar ao GitHub", "Kan geen verbinding maken met GitHub", "Nelze se připojit ke GitHubu", "Не вдалося підключитися до GitHub", "Не удалось подключиться к GitHub", "无法连接到 GitHub");
        Add("Dostępna jest nowa wersja", "A new version is available", "Eine neue Version ist verfügbar", "Une nouvelle version est disponible", "Hay una nueva versión disponible", "È disponibile una nuova versione", "Uma nova versão está disponível", "Er is een nieuwe versie beschikbaar", "Je dostupná nová verze", "Доступна нова версія", "Доступна новая версия", "有新版本可用");
        Add("Masz najnowszą wersję", "You have the latest version", "Du hast die neueste Version", "Vous avez la dernière version", "Tienes la última versión", "Hai la versione più recente", "Você tem a versão mais recente", "Je hebt de nieuwste versie", "Máte nejnovější verzi", "У вас остання версія", "У вас последняя версия", "您使用的是最新版本");
        Add("Panel główny", "Main panel", "Hauptpanel", "Panneau principal", "Panel principal", "Pannello principale", "Painel principal", "Hoofdpaneel", "Hlavní panel", "Головна панель", "Главная панель", "主面板");
        Add("Widoczny", "Visible", "Sichtbar", "Visible", "Visible", "Visibile", "Visível", "Zichtbaar", "Viditelný", "Видимий", "Видимый", "可见");
        Add("Ukryty", "Hidden", "Ausgeblendet", "Masqué", "Oculto", "Nascosto", "Oculto", "Verborgen", "Skrytý", "Прихований", "Скрытый", "隐藏");
        Add("ikony", "icons", "Symbole", "icônes", "iconos", "icone", "ícones", "pictogrammen", "ikony", "значки", "значки", "图标");
        Add("Język", "Language", "Sprache", "Langue", "Idioma", "Lingua", "Idioma", "Taal", "Jazyk", "Мова", "Язык", "语言");
        Add("Język interfejsu", "Interface language", "Sprache der Oberfläche", "Langue de l’interface", "Idioma de la interfaz", "Lingua dell’interfaccia", "Idioma da interface", "Interfacetaal", "Jazyk rozhraní", "Мова інтерфейсу", "Язык интерфейса", "界面语言");
        Add("Podstawowe narzędzia aplikacji", "Basic application tools", "Grundlegende App-Werkzeuge", "Outils de base de l’application", "Herramientas básicas de la aplicación", "Strumenti di base dell’app", "Ferramentas básicas do aplicativo", "Basisfuncties van de app", "Základní nástroje aplikace", "Основні інструменти програми", "Основные инструменты приложения", "基本应用工具");
        Add("Odśwież ikony", "Refresh icons", "Symbole aktualisieren", "Actualiser les icônes", "Actualizar iconos", "Aggiorna icone", "Atualizar ícones", "Pictogrammen vernieuwen", "Obnovit ikony", "Оновити значки", "Обновить значки", "刷新图标");
        Add("Ponownie pobiera ikony skrótów we wszystkich panelach.", "Reloads shortcut icons in all panels.", "Lädt Verknüpfungssymbole in allen Panels neu.", "Recharge les icônes des raccourcis dans tous les panneaux.", "Vuelve a cargar los iconos de accesos directos en todos los paneles.", "Ricarica le icone dei collegamenti in tutti i pannelli.", "Recarrega os ícones de atalho em todos os painéis.", "Laadt snelkoppelingspictogrammen in alle panelen opnieuw.", "Znovu načte ikony zástupců ve všech panelech.", "Повторно завантажує значки ярликів у всіх панелях.", "Повторно загружает значки ярлыков во всех панелях.", "重新加载所有面板中的快捷方式图标。");
        Add("Uruchamianie elementów", "Opening items", "Elemente öffnen", "Ouverture des éléments", "Abrir elementos", "Apertura elementi", "Abrir itens", "Items openen", "Spouštění položek", "Відкриття елементів", "Открытие элементов", "打开项目");
        Add("Uruchamiaj dwuklikiem", "Open with double-click", "Mit Doppelklick öffnen", "Ouvrir par double-clic", "Abrir con doble clic", "Apri con doppio clic", "Abrir com duplo clique", "Openen met dubbelklik", "Otevírat dvojklikem", "Відкривати подвійним клацанням", "Открывать двойным щелчком", "双击打开");
        Add("Wszystkie panele utworzone w aplikacji", "All panels created in the application", "Alle in der App erstellten Panels", "Tous les panneaux créés dans l’application", "Todos los paneles creados en la aplicación", "Tutti i pannelli creati nell’app", "Todos os painéis criados no aplicativo", "Alle panelen die in de app zijn gemaakt", "Všechny panely vytvořené v aplikaci", "Усі панелі, створені в програмі", "Все панели, созданные в приложении", "应用中创建的所有面板");
        Add("Zamknij", "Close", "Schließen", "Fermer", "Cerrar", "Chiudi", "Fechar", "Sluiten", "Zavřít", "Закрити", "Закрыть", "关闭");
        Add("Anuluj", "Cancel", "Abbrechen", "Annuler", "Cancelar", "Annulla", "Cancelar", "Annuleren", "Zrušit", "Скасувати", "Отмена", "取消");
        Add("Zapisz", "Save", "Speichern", "Enregistrer", "Guardar", "Salva", "Salvar", "Opslaan", "Uložit", "Зберегти", "Сохранить", "保存");
        Add("Wybierz", "Choose", "Auswählen", "Choisir", "Elegir", "Scegli", "Escolher", "Kiezen", "Vybrat", "Вибрати", "Выбрать", "选择");
        Add("Tytuł", "Title", "Titel", "Titre", "Título", "Titolo", "Título", "Titel", "Název", "Назва", "Название", "标题");
        Add("Ikonka", "Icon", "Symbol", "Icône", "Icono", "Icona", "Ícone", "Pictogram", "Ikona", "Значок", "Значок", "图标");
        Add("Tło", "Background", "Hintergrund", "Arrière-plan", "Fondo", "Sfondo", "Fundo", "Achtergrond", "Pozadí", "Тло", "Фон", "背景");
        Add("Tło panelu", "Panel background", "Panel-Hintergrund", "Arrière-plan du panneau", "Fondo del panel", "Sfondo pannello", "Fundo do painel", "Paneelachtergrond", "Pozadí panelu", "Тло панелі", "Фон панели", "面板背景");
        Add("Kolor panelu", "Panel color", "Panel-Farbe", "Couleur du panneau", "Color del panel", "Colore pannello", "Cor do painel", "Paneelkleur", "Barva panelu", "Колір панелі", "Цвет панели", "面板颜色");
        Add("Nagłówek", "Header", "Kopfzeile", "En-tête", "Encabezado", "Intestazione", "Cabeçalho", "Koptekst", "Záhlaví", "Заголовок", "Заголовок", "标题栏");
        Add("Ikony", "Icons", "Symbole", "Icônes", "Iconos", "Icone", "Ícones", "Pictogrammen", "Ikony", "Значки", "Значки", "图标");
        Add("Ukryj nagłówek", "Hide header", "Kopfzeile ausblenden", "Masquer l’en-tête", "Ocultar encabezado", "Nascondi intestazione", "Ocultar cabeçalho", "Koptekst verbergen", "Skrýt záhlaví", "Приховати заголовок", "Скрыть заголовок", "隐藏标题栏");
        Add("Czcionka", "Font", "Schriftart", "Police", "Fuente", "Carattere", "Fonte", "Lettertype", "Písmo", "Шрифт", "Шрифт", "字体");
        Add("Kolor tekstu", "Text color", "Textfarbe", "Couleur du texte", "Color del texto", "Colore testo", "Cor do texto", "Tekstkleur", "Barva textu", "Колір тексту", "Цвет текста", "文字颜色");
        Add("Pogrubienie", "Bold", "Fett", "Gras", "Negrita", "Grassetto", "Negrito", "Vet", "Tučné", "Жирний", "Жирный", "粗体");
        Add("Odstęp między literami", "Letter spacing", "Zeichenabstand", "Espacement des lettres", "Espaciado entre letras", "Spaziatura lettere", "Espaçamento entre letras", "Letterafstand", "Rozestup písmen", "Інтервал між літерами", "Интервал между буквами", "字间距");
        Add("Wielkość ikon", "Icon size", "Symbolgröße", "Taille des icônes", "Tamaño de iconos", "Dimensione icone", "Tamanho dos ícones", "Pictogramgrootte", "Velikost ikon", "Розмір значків", "Размер значков", "图标大小");
        Add("Szerokość", "Width", "Breite", "Largeur", "Anchura", "Larghezza", "Largura", "Breedte", "Šířka", "Ширина", "Ширина", "宽度");
        Add("Wysokość", "Height", "Höhe", "Hauteur", "Altura", "Altezza", "Altura", "Hoogte", "Výška", "Висота", "Высота", "高度");
        Add("Folder z elementami", "Items folder", "Ordner mit Elementen", "Dossier des éléments", "Carpeta de elementos", "Cartella elementi", "Pasta de itens", "Itemmap", "Složka položek", "Папка елементів", "Папка элементов", "项目文件夹");
        Add("Ukryj foldery", "Hide folders", "Ordner ausblenden", "Masquer les dossiers", "Ocultar carpetas", "Nascondi cartelle", "Ocultar pastas", "Mappen verbergen", "Skrýt složky", "Приховати папки", "Скрыть папки", "隐藏文件夹");
        Add("Zaokrąglenie", "Corner radius", "Eckenradius", "Arrondi", "Radio de esquina", "Raggio angoli", "Raio dos cantos", "Hoekradius", "Poloměr rohů", "Заокруглення", "Скругление", "圆角");
        Add("Grubość obramowania", "Border width", "Rahmenbreite", "Épaisseur de bordure", "Grosor del borde", "Spessore bordo", "Espessura da borda", "Randdikte", "Tloušťka okraje", "Товщина рамки", "Толщина рамки", "边框宽度");
        Add("Kolor obramowania", "Border color", "Rahmenfarbe", "Couleur de bordure", "Color del borde", "Colore bordo", "Cor da borda", "Randkleur", "Barva okraje", "Колір рамки", "Цвет рамки", "边框颜色");
        Add("Przywróć domyślne tło", "Reset background", "Hintergrund zurücksetzen", "Réinitialiser l’arrière-plan", "Restablecer fondo", "Ripristina sfondo", "Redefinir fundo", "Achtergrond herstellen", "Obnovit pozadí", "Відновити тло", "Сбросить фон", "重置背景");
        Add("Przywróć domyślny nagłówek", "Reset header", "Kopfzeile zurücksetzen", "Réinitialiser l’en-tête", "Restablecer encabezado", "Ripristina intestazione", "Redefinir cabeçalho", "Koptekst herstellen", "Obnovit záhlaví", "Відновити заголовок", "Сбросить заголовок", "重置标题栏");
        Add("Przywróć domyślne ikony", "Reset icons", "Symbole zurücksetzen", "Réinitialiser les icônes", "Restablecer iconos", "Ripristina icone", "Redefinir ícones", "Pictogrammen herstellen", "Obnovit ikony", "Відновити значки", "Сбросить значки", "重置图标");
        Add("Usuń panel", "Delete panel", "Panel löschen", "Supprimer le panneau", "Eliminar panel", "Elimina pannello", "Excluir painel", "Paneel verwijderen", "Odstranit panel", "Видалити панель", "Удалить панель", "删除面板");
        Add("Usuń ikonę", "Delete icon", "Symbol löschen", "Supprimer l’icône", "Eliminar icono", "Elimina icona", "Excluir ícone", "Pictogram verwijderen", "Odstranit ikonu", "Видалити значок", "Удалить значок", "删除图标");
        Add("Edytuj", "Edit", "Bearbeiten", "Modifier", "Editar", "Modifica", "Editar", "Bewerken", "Upravit", "Редагувати", "Изменить", "编辑");
        Add("Kreator", "Creator", "Ersteller", "Créateur", "Creador", "Creazione", "Criador", "Maker", "Tvůrce", "Конструктор", "Конструктор", "创建器");
        Add("Pokaż panel kreatora", "Show creator panel", "Ersteller-Panel anzeigen", "Afficher le panneau créateur", "Mostrar panel creador", "Mostra pannello creazione", "Mostrar painel criador", "Makerpaneel tonen", "Zobrazit panel tvůrce", "Показати панель конструктора", "Показать панель конструктора", "显示创建器面板");
        Add("Uruchamiaj przy starcie systemu", "Start with Windows", "Mit Windows starten", "Démarrer avec Windows", "Iniciar con Windows", "Avvia con Windows", "Iniciar com o Windows", "Starten met Windows", "Spouštět se systémem", "Запускати з Windows", "Запускать с Windows", "随 Windows 启动");
        Add("Nowy panel", "New panel", "Neues Panel", "Nouveau panneau", "Nuevo panel", "Nuovo pannello", "Novo painel", "Nieuw paneel", "Nový panel", "Нова панель", "Новая панель", "新建面板");
        Add("Dodaj nowy panel", "Add new panel", "Neues Panel hinzufügen", "Ajouter un panneau", "Añadir panel", "Aggiungi pannello", "Adicionar painel", "Nieuw paneel toevoegen", "Přidat nový panel", "Додати нову панель", "Добавить новую панель", "添加新面板");
        Add("Tapeta", "Wallpaper", "Hintergrundbild", "Fond d’écran", "Fondo de pantalla", "Sfondo", "Papel de parede", "Achtergrond", "Tapeta", "Шпалери", "Обои", "壁纸");
        Add("Tapety", "Wallpapers", "Hintergrundbilder", "Fonds d’écran", "Fondos de pantalla", "Sfondi", "Papéis de parede", "Achtergronden", "Tapety", "Шпалери", "Обои", "壁纸");
        Add("Szczegóły tapety", "Wallpaper details", "Details zum Hintergrundbild", "Détails du fond d’écran", "Detalles del fondo", "Dettagli sfondo", "Detalhes do papel de parede", "Achtergronddetails", "Podrobnosti tapety", "Деталі шпалер", "Сведения об обоях", "壁纸详情");
        Add("Tagi", "Tags", "Tags", "Tags", "Etiquetas", "Tag", "Tags", "Tags", "Štítky", "Теги", "Теги", "标签");
        Add("Ładowanie", "Loading", "Laden", "Chargement", "Cargando", "Caricamento", "Carregando", "Laden", "Načítání", "Завантаження", "Загрузка", "加载中");
        Add("Minimalizuj", "Minimize", "Minimieren", "Réduire", "Minimizar", "Riduci", "Minimizar", "Minimaliseren", "Minimalizovat", "Згорнути", "Свернуть", "最小化");
        Add("Pełny ekran", "Full screen", "Vollbild", "Plein écran", "Pantalla completa", "Schermo intero", "Tela cheia", "Volledig scherm", "Celá obrazovka", "Повний екран", "Полный экран", "全屏");
        Add("Kategorie", "Categories", "Kategorien", "Catégories", "Categorías", "Categorie", "Categorias", "Categorieën", "Kategorie", "Категорії", "Категории", "分类");
        Add("Czystość", "Purity", "Reinheit", "Pureté", "Pureza", "Purezza", "Pureza", "Zuiverheid", "Čistota", "Чистота", "Чистота", "纯净度");
        Add("Wybierz kolor", "Choose color", "Farbe wählen", "Choisir la couleur", "Elegir color", "Scegli colore", "Escolher cor", "Kleur kiezen", "Vybrat barvu", "Вибрати колір", "Выбрать цвет", "选择颜色");
        Add("Potwierdzenie", "Confirmation", "Bestätigung", "Confirmation", "Confirmación", "Conferma", "Confirmação", "Bevestiging", "Potvrzení", "Підтвердження", "Подтверждение", "确认");
        Add("Wygląd", "Appearance", "Aussehen", "Apparence", "Apariencia", "Aspetto", "Aparência", "Uiterlijk", "Vzhled", "Вигляд", "Внешний вид", "外观");
        Add("Pobierz", "Download", "Herunterladen", "Télécharger", "Descargar", "Scarica", "Baixar", "Downloaden", "Stáhnout", "Завантажити", "Скачать", "下载");
        Add("Ustaw jako tapeta", "Set as wallpaper", "Als Hintergrund festlegen", "Définir comme fond d’écran", "Establecer como fondo", "Imposta come sfondo", "Definir como papel de parede", "Instellen als achtergrond", "Nastavit jako tapetu", "Установити як шпалери", "Установить как обои", "设为壁纸");
        Add("Spróbuj ponownie", "Try again", "Erneut versuchen", "Réessayer", "Intentar de nuevo", "Riprova", "Tentar novamente", "Opnieuw proberen", "Zkusit znovu", "Спробувати знову", "Повторить", "重试");
        return result;
    }
}

public sealed record LanguageOption(string Code, string Name);
