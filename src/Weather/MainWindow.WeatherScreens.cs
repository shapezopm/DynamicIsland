using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

// The add screen (search and popular places), the preview of a chosen place, the select mode and the delete bubble.
// The preview is the weather card itself, filled with the candidate's report, with Отмена / Добавить under it.
public partial class MainWindow
{
    static readonly string[] PopularPlaces =
    [
        "Москва", "Санкт-Петербург", "Ташкент", "Киев", "Казань", "Екатеринбург", "Новосибирск", "Нижний Новгород", "Минск", "Алматы",
        "Пермь", "Таллин", "Ереван", "Ашхабад", "Душанбе", "Воронеж", "Волгоград", "Тбилиси", "Астана", "Ростов-на-Дону",
        "Баку", "Омск", "Бишкек", "Рига", "Вильнюс", "Красноярск", "Челябинск", "Уфа", "Самара", "Кишинёв",
    ];

    readonly CancellationTokenSource _weatherPreload = new();
    bool _savedWeatherRefreshing;
    DateTime _savedWeatherRetryAt;
    StackPanel? _addResults;
    WrapPanel? _addPopular;
    ScrollViewer? _addScroll;
    GradientStop? _addEdgeTop, _addEdgeBottom;
    TextBlock? _addCaption;
    CancellationTokenSource? _addLookup;
    TextBlock? _addHint, _addStatus;
    
    int _previewVersion;

    Grid? _addPage, _previewPage;
    TextBox? _addSearch;
    readonly System.Windows.Threading.DispatcherTimer _addSearchDelay = new() { Interval = TimeSpan.FromMilliseconds(320) };
    Border? _deleteBubble;
    TextBlock? _deleteLabel;
    static readonly SolidColorBrush DeleteRed = new(Color.FromRgb(0xFF, 0x69, 0x61));
    WeatherPlace? _previewPlace;
    bool _addOpen, _selectMode;
    readonly HashSet<string> _selectedIds = [];

    // Built once: the add page, the preview buttons, the select button and the delete bubble of the manage screen.
    void BuildWeatherScreens()
    {
        if (_addPage is not null) return;
        BuildAddPage();
        BuildPreviewPage();
        BuildManageExtras();
    }

    void BuildAddPage()
    {
        _addPage = new Grid { Width = 522, Height = 452, Visibility = Visibility.Collapsed, Background = Brushes.Transparent };
        _addSearch = new TextBox
        {
            Style = (Style)FindResource("IslandTextBox"), FontSize = WeatherBodySize, Height = 44, MaxLength = 100,
            Padding = new Thickness(14, 0, 40, 0), VerticalContentAlignment = VerticalAlignment.Center,
        };
        _addSearch.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideAddPage();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _addSearchDelay.Stop();
                string query = _addSearch.Text.Trim();
                if (query.Length > 0) Safely(() => PreviewFirstMatchAsync(query));
            }
        };
        _addSearch.TextChanged += (_, _) =>
        {
            _addSearchDelay.Stop();
            _addLookup?.Cancel();
            if (_addSearch.Text.Trim().Length < 2)
            {
                _addResults?.Children.Clear();
                if (_addResults is not null) _addResults.Visibility = Visibility.Collapsed;
                if (_addPopular is not null) _addPopular.Visibility = Visibility.Visible;
                if (_addCaption is not null) _addCaption.Text = "Популярные места";
                if (_addStatus is not null) _addStatus.Text = "";
                return;
            }
            _addSearchDelay.Start();
        };
        _addSearchDelay.Tick += (_, _) =>
        {
            _addSearchDelay.Stop();
            if (_addOpen && _addSearch is not null) Safely(() => PreviewFirstMatchAsync(_addSearch.Text.Trim()));
        };
        System.Windows.Automation.AutomationProperties.SetName(_addSearch, "Добавить город");
        var searchFrame = new Grid { Height = 44, Margin = new Thickness(78, 16, 22, 0), VerticalAlignment = VerticalAlignment.Top };
        searchFrame.Children.Add(_addSearch);
        var searchIcon = WeatherIcon(Glyph.Search, 14);
        searchIcon.HorizontalAlignment = HorizontalAlignment.Right;
        searchIcon.Margin = new Thickness(0, 0, 14, 0);
        searchFrame.Children.Add(searchIcon);
        _addHint = WeatherText("Введите название города", WeatherBodySize, DimBrush);
        _addHint.Margin = new Thickness(32, 0, 40, 0);
        _addHint.VerticalAlignment = VerticalAlignment.Center;
        WireWeatherSearchHint(_addSearch, _addHint);
        searchFrame.Children.Add(_addHint);
        _ = new WeatherSearchCursor(_addSearch, _addHint, searchFrame);
        _addPage.Children.Add(searchFrame);
        _addStatus = WeatherText("", WeatherBodySize, DimBrush);
        _addStatus.Margin = new Thickness(22, 68, 22, 0);
        _addStatus.VerticalAlignment = VerticalAlignment.Top;
        _addPage.Children.Add(_addStatus);
        var close = new WeatherActionBorder
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Cursor = Cursors.Hand,
            Margin = new Thickness(22, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xE3, 0xF0, 0xFF)), Child = WeatherIcon(Glyph.Cross, 12),
        };
        WireWeatherAction(close, "Закрыть добавление города", HideAddPage);
        Claim(close);
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            HideAddPage();
        };
        _addPage.Children.Add(close);
        var caption = _addCaption = WeatherHeading("Популярные места");
        caption.Margin = new Thickness(22, 92, 0, 0);
        caption.VerticalAlignment = VerticalAlignment.Top;
        _addPage.Children.Add(caption);
        var pills = _addPopular = new WrapPanel { Width = 456, Margin = new Thickness(0, 0, 12, 0) };
        foreach (string name in PopularPlaces)
        {
            string city = name;
            var pill = new WeatherActionBorder
            {
                Width = 144, Height = 40, Margin = new Thickness(4), CornerRadius = new CornerRadius(20), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0x26, 0xE3, 0xF0, 0xFF)),
                Child = WeatherText(name, 14, Brushes.White, TextAlignment.Center, FontWeights.SemiBold),
            };
            WireWeatherAction(pill, "Предпросмотр " + city, () => Safely(() => PreviewFirstMatchAsync(city, true)));
            Claim(pill);
            pill.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Safely(() => PreviewFirstMatchAsync(city, true));
            };
            pills.Children.Add(pill);
        }
        _addResults = new StackPanel { Visibility = Visibility.Collapsed };
        var choices = new Grid();
        choices.Children.Add(pills);
        choices.Children.Add(_addResults);
        var scroll = _addScroll = new ScrollViewer
        {
            Margin = new Thickness(18, 124, 18, 18), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = choices,
        };
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mask.GradientStops.Add(_addEdgeTop = new GradientStop(Colors.Black, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.07));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.93));
        mask.GradientStops.Add(_addEdgeBottom = new GradientStop(Colors.Black, 1));
        scroll.OpacityMask = mask;
        ApplyIslandScrollBar(scroll);
        EnableWeatherSmoothScroll(scroll);
        _addPage.Children.Add(scroll);
        WeatherView.Children.Add(_addPage);
    }

    void ApplyAddEdges(ScrollViewer viewer)
    {
        if (viewer.ViewportHeight <= 0) return;
        double scrolled = viewer.VerticalOffset;
        double overflow = viewer.ScrollableHeight;
        if (_addEdgeTop is not null) _addEdgeTop.Color = EdgeFade(scrolled);
        if (_addEdgeBottom is not null) _addEdgeBottom.Color = EdgeFade(overflow - scrolled);
        double top = Math.Clamp(scrolled / LookEdgeZone, 0, 1);
        double bottom = Math.Clamp((overflow - scrolled) / LookEdgeZone, 0, 1);
        var rows = _addResults is { Visibility: Visibility.Visible } ? _addResults.Children : _addPopular?.Children;
        if (rows is null) return;
        foreach (Border row in rows.OfType<Border>())
        {
            if (row.ActualHeight <= 0) continue;
            double middle = row.TranslatePoint(new Point(0, row.ActualHeight / 2), viewer).Y;
            double fromTop = Math.Clamp(middle / LookEdgeZone, 0, 1);
            double fromBottom = Math.Clamp((viewer.ViewportHeight - middle) / LookEdgeZone, 0, 1);
            double shrink = Math.Max(top * Smooth(1 - fromTop), bottom * Smooth(1 - fromBottom));
            if (row.RenderTransform is not ScaleTransform size)
            {
                row.RenderTransform = size = new ScaleTransform();
                row.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            size.ScaleX = size.ScaleY = 1;
        }
    }

    // The island's own thin scroll bar instead of the default one.
    void ApplyIslandScrollBar(ScrollViewer viewer)
    {
        if (TryFindResource("IslandScrollBar") is Style bar) viewer.Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), bar);
    }

    // Overlay over the weather card: only the two buttons, at the bottom where the footer is hidden during the preview.
    void BuildPreviewPage()
    {
        _previewPage = new Grid { Width = 522, Height = 452, Visibility = Visibility.Collapsed };
        var cancel = PillButton("Отмена", HidePreview);
        cancel.HorizontalAlignment = HorizontalAlignment.Left;
        cancel.VerticalAlignment = VerticalAlignment.Bottom;
        cancel.Margin = new Thickness(22, 0, 0, 14);
        _previewPage.Children.Add(cancel);
        var add = PillButton("Добавить", () =>
        {
            if (_previewPlace is { } place) Safely(() => AddPreviewAsync(place));
        });
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.VerticalAlignment = VerticalAlignment.Bottom;
        add.Margin = new Thickness(0, 0, 22, 14);
        _previewPage.Children.Add(add);
        var main = PillButton("Основным", () =>
        {
            if (_previewPlace is { } place) Safely(() => SetWeatherHomeAsync(place));
        });
        main.HorizontalAlignment = HorizontalAlignment.Center;
        main.VerticalAlignment = VerticalAlignment.Bottom;
        main.Margin = new Thickness(0, 0, 0, 14);
        _previewPage.Children.Add(main);
        WeatherView.Children.Add(_previewPage);
    }

    static Border PillButton(string text, Action action)
    {
        var pill = new WeatherActionBorder
        {
            Height = 40, Padding = new Thickness(22, 0, 22, 0), CornerRadius = new CornerRadius(20), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xE3, 0xF0, 0xFF)),
            Child = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
        };
        WireWeatherAction(pill, text, action);
        Claim(pill);
        pill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return pill;
    }

    // A search always shows explicit choices, including region and country.
    async Task PreviewFirstMatchAsync(string query, bool directPreview = false)
    {
        if (directPreview) _addSearchDelay.Stop();
        int version = ++_previewVersion;
        _addLookup?.Cancel(); _addLookup?.Dispose();
        var lookup = _addLookup = CancellationTokenSource.CreateLinkedTokenSource(_weatherPreload.Token);
        if (_addStatus is not null) _addStatus.Text = "Ищу места…";
        try
        {
            var places = WeatherLocation.Coordinates(query) is { } point ? new[] { point }
                : await WeatherLocation.SearchAsync(query, lookup.Token);
            if (version != _previewVersion || !_addOpen) return;
            if (directPreview && places.Length > 0)
            {
                if (_addStatus is not null) _addStatus.Text = "Загружаю прогноз…";
                await ShowPreviewAsync(places[0], version);
                return;
            }
            ShowAddResults(places);
            if (_addStatus is not null) _addStatus.Text = places.Length == 0 ? "Город не найден. Уточните название." : "Выберите нужное место";
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Log(ex);
            if (version == _previewVersion && _addStatus is not null)
                _addStatus.Text = "Поиск недоступен. Попробуйте ещё раз";
        }
    }

    void ShowAddResults(IEnumerable<WeatherPlace> places)
    {
        if (_addResults is null || _addPopular is null || _addCaption is null) return;
        _addResults.Children.Clear();
        _addPopular.Visibility = Visibility.Collapsed;
        _addResults.Visibility = Visibility.Visible;
        _addCaption.Text = "Результаты поиска";
        _addScroll?.ScrollToTop();
        foreach (var place in places)
            _addResults.Children.Add(WeatherSearchResult(place, () => Safely(async () =>
            {
                int version = ++_previewVersion;
                if (_addStatus is not null) _addStatus.Text = "Загружаю прогноз…";
                try { await ShowPreviewAsync(place, version); }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    App.Log(ex);
                    if (version == _previewVersion && _addStatus is not null)
                        _addStatus.Text = "Прогноз недоступен. Попробуйте ещё раз";
                }
            })));
    }

    async Task ShowPreviewAsync(WeatherPlace place, int version)
    {
        if (_previewPage is null) return;
        // Keep the city picker usable until the complete forecast is available.
        var report = await _weather.FetchPreviewAsync(place, _weatherPreload.Token);
        if (version != _previewVersion || !_addOpen) return;
        if (report is null)
        {
            if (_addStatus is not null) _addStatus.Text = "Прогноз недоступен. Попробуйте ещё раз";
            return;
        }
        _previewPlace = place;
        if (_addPage is not null) _addPage.Visibility = Visibility.Collapsed;
        ShowPreviewState(place, report);
        _previewPage.Visibility = Visibility.Visible;
    }

    async Task WarmSavedWeatherAsync()
    {
        if (!Settings.Weather || _savedWeatherRefreshing || DateTime.UtcNow < _savedWeatherRetryAt) return;
        _savedWeatherRefreshing = true;
        _savedWeatherRetryAt = DateTime.UtcNow.AddMinutes(1);
        try
        {
            Task selected = _weather.RefreshAsync();
            var places = _weatherCities.Concat(new[] { WeatherLocation.Read(Settings.WeatherHome) }.OfType<WeatherPlace>()).ToArray();
            await Task.WhenAll(selected, _weather.PreloadAsync(places, _weatherPreload.Token));
        }
        finally { _savedWeatherRefreshing = false; }
    }

    // The preview shows the same blocks as the weather card of a city, filled with the candidate's report.
    void ShowPreviewState(WeatherPlace place, WeatherService.Report? report)
    {
        WeatherFooter.Visibility = Visibility.Collapsed;
        if (report is null)
        {
            WeatherContent.Visibility = Visibility.Collapsed;
            WeatherNotice.Visibility = Visibility.Visible;
            WeatherNoticeText.Text = "Загружаю погоду…";
            WeatherNoticeAction.Visibility = Visibility.Collapsed;
            return;
        }
        FillWeather(new WeatherCityView(WeatherWebData.Id(place), place.DisplayName, report), report);
        WeatherNotice.Visibility = Visibility.Collapsed;
        WeatherContent.Visibility = Visibility.Visible;
        WeatherPlace.Visibility = Visibility.Visible;
    }

    // Cancel returns to the add screen; the shown city and the footer come back.
    void HidePreview()
    {
        if (_previewPage is null || (_previewPlace is null && _previewPage.Visibility != Visibility.Visible)) return;
        _previewVersion++;
        _previewPlace = null;
        _previewPage.Visibility = Visibility.Collapsed;
        PublishWeather();
        if (_addOpen && _addPage is not null) _addPage.Visibility = Visibility.Visible;
    }

    // "Добавить" in the preview adds the place to the saved cities; the home city does not change.
    async Task AddPreviewAsync(WeatherPlace place)
    {
        HidePreview();
        await AddWeatherCityAsync(place);
    }

    void ShowAddPage()
    {
        if (_addPage is null || _addSearch is null) return;
        _addOpen = true;
        _addPage.Background = Brushes.Transparent;
        WeatherCityPage.Visibility = Visibility.Collapsed;
        if (_addStatus is not null) _addStatus.Text = "";
        if (_addResults is not null) _addResults.Visibility = Visibility.Collapsed;
        if (_addPopular is not null) _addPopular.Visibility = Visibility.Visible;
        if (_addCaption is not null) _addCaption.Text = "Популярные места";
        _addScroll?.ScrollToTop();
        _addPage.Visibility = Visibility.Visible;
        _addSearch.Text = "";
        WeatherContent.Visibility = Visibility.Collapsed; WeatherPlace.Visibility = WeatherFreshness.Visibility = Visibility.Collapsed;
        WeatherFooter.Visibility = Visibility.Collapsed; WeatherNotice.Visibility = Visibility.Collapsed;
        var scale = _addPage.RenderTransform as ScaleTransform ?? new ScaleTransform();
        _addPage.RenderTransform = scale;
        _addPage.RenderTransformOrigin = new Point(0.5, 1);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, Ms(SystemParameters.ClientAreaAnimation ? 220 : 0)) { EasingFunction = WeatherOut, FillBehavior = FillBehavior.HoldEnd });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, Ms(SystemParameters.ClientAreaAnimation ? 220 : 0)) { EasingFunction = WeatherOut, FillBehavior = FillBehavior.HoldEnd });
        _addPage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(SystemParameters.ClientAreaAnimation ? 220 : 0)) { EasingFunction = WeatherEase, FillBehavior = FillBehavior.HoldEnd });
        SetWeatherEditing(true);
        Dispatcher.BeginInvoke(() => Keyboard.Focus(_addSearch), System.Windows.Threading.DispatcherPriority.Input);
    }

    void HideAddPage()
    {
        _addSearchDelay.Stop();
        _previewVersion++;
        _addLookup?.Cancel();
        _addOpen = false;
        HidePreview();
        if (_addPage is null) return;
        var fade = new DoubleAnimation(0, Ms(SystemParameters.ClientAreaAnimation ? 180 : 0)) { EasingFunction = WeatherEase, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) =>
        {
            if (!_addOpen) { _addPage.Visibility = Visibility.Collapsed; PublishWeather(); }
        };
        _addPage.BeginAnimation(OpacityProperty, fade);
        SetWeatherEditing(_weatherWebPane);
    }

    TextBlock? _manageTitle;
    Border? _manageAction;
    static readonly string ManageTitleDefault = "Управление городами";

    void BuildManageExtras()
    {
        _manageTitle = WeatherText(ManageTitleDefault, WeatherTitleSize, Brushes.White, TextAlignment.Center, FontWeights.SemiBold);
        _manageTitle.Margin = new Thickness(46, 0, 46, 0);
        _manageTitle.VerticalAlignment = VerticalAlignment.Center;
        WeatherCityPage.Children.Add(_manageTitle);

        _manageAction = new WeatherActionBorder
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Cursor = Cursors.Hand, Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xE3, 0xF0, 0xFF)), Child = ManagePencil(),
        };
        WireWeatherAction(_manageAction, "Выбрать города", OnManageAction);
        Claim(_manageAction);
        _manageAction.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OnManageAction();
        };
        WeatherCityPage.Children.Add(_manageAction);

        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(TrashIcon());
        var label = _deleteLabel = WeatherText("Удалить", WeatherBodySize, DeleteRed, TextAlignment.Left, FontWeights.SemiBold);
        label.Margin = new Thickness(8, 0, 0, 0);
        content.Children.Add(label);
        _deleteBubble = new WeatherActionBorder
        {
            Height = 40, CornerRadius = new CornerRadius(20), Padding = new Thickness(24, 0, 24, 0), Cursor = Cursors.Hand,
            Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0x69, 0x61)), Child = content,
        };
        WireWeatherAction(_deleteBubble, "Удалить выбранные города", DeleteSelectedCities);
        Claim(_deleteBubble);
        _deleteBubble.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            DeleteSelectedCities();
        };
        WeatherCityPage.Children.Add(_deleteBubble);

        Grid.SetRow(_deleteBubble, 6);
    }

    static Path ManagePencil() => new Path
    {
        Data = Geometry.Parse("M2,12 L3.2,8.6 L9.6,2.2 L11.8,4.4 L5.4,10.8 Z"), Stroke = Brushes.White, StrokeThickness = 1.3,
        StrokeLineJoin = PenLineJoin.Round, Width = 14, Height = 14, Stretch = Stretch.Uniform, IsHitTestVisible = false,
    };

    static Path TrashIcon() => new Path
    {
        Data = Geometry.Parse("M3,4.5 H13 M6.5,4.5 V3 H9.5 V4.5 M4.5,4.5 L5.2,13.5 H10.8 L11.5,4.5"), Stroke = DeleteRed, StrokeThickness = 1.3,
        StrokeLineJoin = PenLineJoin.Round, Width = 15, Height = 15, Stretch = Stretch.Uniform, IsHitTestVisible = false,
    };

    // Pencil enters selection; the check mark finishes selection without deleting.
    void OnManageAction()
    {
        if (!_selectMode)
        {
            _selectMode = true;
            _selectedIds.Clear();
            RefreshManageState();
            RefreshWeatherCities();
            return;
        }
        ExitSelectMode();
    }

    void ToggleCitySelection(string id)
    {
        if (!_selectedIds.Add(id)) _selectedIds.Remove(id);
        RefreshManageState();
        RefreshWeatherCities();
    }

    void ExitSelectMode()
    {
        _selectMode = false;
        _selectedIds.Clear();
        RefreshManageState();
        RefreshWeatherCities();
    }

    void WeatherManageClose_Click(object sender, RoutedEventArgs e)
    {
        if (_selectMode) ExitSelectMode();
        else WeatherBack_Click(sender, e);
    }

    // The single source of the manage screen state: title, action button, delete bubble and the automatic button.
    void RefreshManageState()
    {
        if (_manageTitle is null || _manageAction is null || _deleteBubble is null) return;
        int count = _selectedIds.Count;
        System.Windows.Automation.AutomationProperties.SetName(_manageAction, _selectMode ? "Готово" : "Выбрать города");
        _manageTitle.Text = _selectMode ? $"Выбрано: {count}" : ManageTitleDefault;
        _manageAction.Child = _selectMode ? WeatherIcon(Glyph.Check, 15, Brushes.White) : ManagePencil();
        _manageAction.Background = CardTile;
        if (_deleteLabel is not null) _deleteLabel.Text = $"Удалить ({count})";
        _deleteBubble.Visibility = _selectMode && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WeatherAutomaticButton.Visibility = _selectMode ? Visibility.Collapsed : Visibility.Visible;
    }

    // Removes only the saved cities; the current place is never in the selection. Stays in the select mode.
    void DeleteSelectedCities()
    {
        if (_selectedIds.Count == 0) return;
        _weatherCities.RemoveAll(city => _selectedIds.Contains(WeatherWebData.Id(city)));
        _selectedIds.Clear();
        SaveWeatherCities();
        RefreshManageState();
        PublishWeather();
        EnsureWeatherPlace();
    }
}
