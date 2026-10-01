using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AstroForge.App.Services;
using AstroForge.App.ViewModels;
using AstroForge.Core.Demo;
using AstroForge.Core.Equipment;
using AstroForge.Core.IO;
using AstroForge.Core.Filters;
using AstroForge.Core.Models;
using AstroForge.Core.Releases;
using AstroForge.CrossPlatform.Controls;
using AstroForge.CrossPlatform.ViewModels;
using Avalonia.VisualTree;

namespace AstroForge.CrossPlatform;

public sealed partial class MainWindow : Window
{
    private const string RepositoryUrl = "https://github.com/astropuzzo/astroproject-forge";
    private const string GuideUrl = RepositoryUrl + "/wiki";
    private const string IssueUrl = RepositoryUrl + "/issues/new?template=bug_report.yml";
    public const string SmokeTestArgument = "--smoke-test";
    public const string CaptureArgument = "--capture";
    public const string UpdatedArgument = "--updated";
    public const string ProjectFileExtension = ".astroforge";
    private readonly MainViewModel _viewModel = new();
    private readonly ObservatoryViewModel _observatory;
    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(2800) };
    private readonly UpdateService _updateService = new();
    private ReleaseArtifact? _availableUpdate;
    private bool _availableUpdateSigned;
    private string? _startupProjectPath;
    private bool _updatedOnLaunch;
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private CancellationTokenSource? _qualityCancellation;
    private CancellationTokenSource? _previewCancellation;
    private int _onboardingStep = 1;
    private int _blinkIndex = -1;
    private double _qualityZoom = 1;
    private bool _localizationPending;
    private int _lastScreen;
    private bool _playingNights;
    private bool _exportCelebrated;

    private static readonly FilePickerFileType AstroImages = new("Immagini astronomiche")
    {
        Patterns = ["*.fit", "*.fits", "*.fts", "*.xisf", "*.FIT", "*.FITS", "*.FTS", "*.XISF"]
    };

    public MainWindow()
    {
        Motion.SetReduced(_viewModel.ReducedMotion);
        InitializeComponent();
        DataContext = _viewModel;
        _observatory = new ObservatoryViewModel(_viewModel);
        _shell = new ShellViewModel(_viewModel, _observatory);
        // The steps (header, pages, footer) read the shell; the tools, menu and overlays outside them keep the project model.
        RootLayout.DataContext = _shell;
        StatsNights.DataContext = _observatory;
        PaletteHost.DataContext = _shell;
        // On a narrow window the header gives up the words, not the steps.
        RootLayout.SizeChanged += (_, args) =>
        {
            BrandText.IsVisible = args.NewSize.Width >= 1240;
            ProjectChip.IsVisible = args.NewSize.Width >= 1400;
            // Narrow: Tools and Menu keep their icons and give up their words (their tooltips say what they are).
            ToolsLabel.IsVisible = MenuLabel.IsVisible = args.NewSize.Width >= 1240;
            ArrangeExportColumns(args.NewSize.Width < 1320);
        };
        Stepper.StepInvoked += (_, step) => GoToStep(step);
        TrainView.PartInvoked += TrainView_PartInvoked;
        _shell.StepChanged += Shell_StepChanged;
        NightSkyView.NightInvoked += (_, night) => _shell.NightIndex = night + 1;
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.IsVisible = false; };
        _observatory.Filters.CollectionChanged += (_, _) => ScheduleLocalization();
        _viewModel.UiLanguageChanged += (_, _) => { ScheduleLocalization(); Dispatcher.UIThread.Post(RefreshCalibrationVisuals); ScheduleDarkCoverage(); };
        _blinkTimer.Tick += BlinkTimer_Tick;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.HasSelection)) UpdateInspectorLayout();
            else if (args.PropertyName == nameof(MainViewModel.ReducedMotion)) Motion.SetReduced(_viewModel.ReducedMotion);
            else if (args.PropertyName == nameof(MainViewModel.Analysis)) { RefreshCalibrationVisuals(); PreparePlanPreview(); ScheduleExportVisuals(); ScheduleDarkCoverage(); }
            else if (args.PropertyName == nameof(MainViewModel.Novelty)) ScheduleExportVisuals();
            else if (args.PropertyName == nameof(MainViewModel.HasAnalysis)) AutoReanalyze();
            else if (args.PropertyName is nameof(MainViewModel.ExportProgress) or nameof(MainViewModel.ExportState)) { RefreshExportProgress(); UpdateNextFill(); }
            else if (args.PropertyName == nameof(MainViewModel.Status)) ShowToast(_viewModel.Status);
            else if (args.PropertyName is nameof(MainViewModel.Progress) or nameof(MainViewModel.IsScanning)) UpdateNextFill();
        };
        // The export page shows the real plan as soon as the name and the destination are settled.
        ProjectNameBox.LostFocus += (_, _) => { PreparePlanPreview(); ScheduleExportVisuals(); };
        DestinationBox.LostFocus += (_, _) => { PreparePlanPreview(); ScheduleExportVisuals(); };
        CalibrationMapView.CellActivated += CalibrationMap_CellActivated;
        _viewModel.PlannedTreeRoots.CollectionChanged += (_, _) => ScheduleExportVisuals();
        _viewModel.MasterOrganizerItems.CollectionChanged += (_, _) => ScheduleDarkCoverage();
        KeyDown += Window_KeyDown;
        // Folders and files can be dropped anywhere on the window.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, Window_DragOver, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, Window_DragLeave, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, Window_Drop, handledEventsToo: true);
        // Tunnel so the tour keys win over focus navigation in whatever control has focus.
        AddHandler(KeyDownEvent, Tour_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            SelectDensity();
            ScheduleLocalization();
            PlayFirstLight();
            if (_updatedOnLaunch) ShowUpdateCompletedNotification();
            if (_startupProjectPath is { } projectPath) await OpenStartupProjectAsync(projectPath);
            if (!Environment.GetCommandLineArgs().Contains(SmokeTestArgument) && !Environment.GetCommandLineArgs().Contains(CaptureArgument)) await CheckUpdatesAsync(false);
        };
        Closing += (_, _) =>
        {
            _qualityCancellation?.Cancel();
            _previewCancellation?.Cancel();
            if (AnalysisGrid.ColumnDefinitions[2].ActualWidth >= 280) _viewModel.InspectorPanelWidth = AnalysisGrid.ColumnDefinitions[2].ActualWidth;
            _viewModel.SaveState();
        };
        ApplyCommandLine();
        UpdateInspectorLayout();
    }

    /// <summary>
    /// Opens every step and tool once and lets layout, bindings and localization settle, so CI catches
    /// startup and template crashes that a build alone cannot. Returns the process exit code.
    /// </summary>
    public async Task<int> RunSmokeTestAsync()
    {
        try
        {
            for (var step = 0; step < ShellViewModel.StepCount; step++)
            {
                _shell.CurrentStep = step;
                await Task.Delay(250);
            }
            for (var index = 0; index < SheetTabs.ItemCount; index++)
            {
                OpenSheet(index);
                await Task.Delay(250);
            }
            CloseSheet();
            OpenPalette();
            await Task.Delay(150);
            ClosePalette();
            SettingsPanel.IsVisible = true;
            await Task.Delay(250);
            SettingsPanel.IsVisible = false;
            _shell.CurrentStep = 0;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await RunBehaviourChecksAsync();
            Console.WriteLine($"SMOKE TEST PASSED · {ShellViewModel.StepCount} steps and {SheetTabs.ItemCount} tools opened, gear and flow checked");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SMOKE TEST FAILED · {exception}");
            return 1;
        }
    }

    /// <summary>
    /// What people asked of the steps, checked on the demo project: the analysis carries on to the gear by itself, a filter Forge
    /// already recognised can still be changed, the camera can be said to be another one and Dark and Bias follow it.
    /// </summary>
    private async Task RunBehaviourChecksAsync()
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Settle() { await Task.Delay(300); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); }

        await LoadDemoAsync();
        await Settle();
        Check(_shell.CurrentStep == ShellViewModel.SetupStep, $"After the analysis the project must carry on to the gear, it is on step {_shell.CurrentStep}.");
        Check(_viewModel.Analysis is not null && _viewModel.ReviewQueue.Count == 0, "The demo project must be fully calibrated.");
        Check(_shell.NextKind == NextActionKind.Continue, "On the gear step the main button must be Continue.");

        // The gear step: the unknown filter is identified, and a recognised one can be changed as well.
        Check(_observatory.PendingCount == 1, $"The demo wheel has one filter to identify, found {_observatory.PendingCount}.");
        var unknown = _observatory.Filters.Single(row => row.NeedsConfirmation);
        unknown.Choice = unknown.Choices.Single(choice => choice.Id == DemoDatasetGenerator.CustomFilterCatalogId);
        _observatory.Confirm(unknown);
        await Settle();
        Check(_observatory.PendingCount == 0, "Identifying the filter must clear the pending confirmation.");
        var recognised = _observatory.Filters.First(row => !row.NeedsConfirmation && !row.IsConfirmed);
        var other = recognised.Choices.First(choice => choice.Id != recognised.Filter.Identity.Product?.Id);
        recognised.Choice = other;
        Check(recognised.IsChoiceNew, "A recognised filter must offer its picker.");
        _observatory.Confirm(recognised);
        await Settle();
        var changed = _observatory.Filters.Single(row => row.RawName == recognised.RawName);
        Check(changed.IsConfirmed && changed.Filter.Identity.Product?.Id == other.Id, "A recognised filter must be changeable to another one.");
        _observatory.Forget(changed);
        await Settle();
        Check(!_observatory.Filters.Single(row => row.RawName == recognised.RawName).IsConfirmed, "Forgetting a choice must bring the recognition back.");

        // The camera: say it is another one. Lights, Flats and the Masters of the same camera all read under the new name, so the matches hold; undo restores the headers.
        var detected = _observatory.CameraName;
        _observatory.BeginEdit();
        _observatory.CameraText = "Camera di prova";
        _observatory.ApplyEdit();
        await Settle();
        Check(_observatory.CameraName == "Camera di prova" && _observatory.CameraWasChanged, $"The camera the user named must be shown ({_observatory.CameraName}).");
        Check(_viewModel.ReviewQueue.Count == 0 && _viewModel.Analysis!.Lights.All(item => item.Light.Camera.Value == "Camera di prova" && item.Dark.IsAccepted), "The frames of that camera must read under the new name and stay matched.");
        _observatory.ResetProfile();
        await Settle();
        Check(_observatory.CameraName == detected && !_observatory.CameraWasChanged && _viewModel.ReviewQueue.Count == 0, "Restoring the detected camera must restore the matches.");

        // The menu is a button with a name in the header, and it opens the preferences and the updates.
        Check(SettingsButton.IsVisible && SettingsButton.Bounds.Width > 40 && MenuLabel.IsVisible && ToolsButton.IsVisible, "The Menu button must be in the header, with its name.");
        var menuWasOpen = SettingsPanel.IsVisible;
        SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        Check(SettingsPanel.IsVisible != menuWasOpen, "Selecting Menu must open the preferences.");
        SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Settle();
        Check(SettingsPanel.IsVisible == menuWasOpen, "Selecting Menu again must close it.");

        // The sky behind the field: with no network the frame is still where the data were taken, on an empty sky that says why;
        // with a picture served the real sky is behind it, centred on the target; and it can be turned off from the Menu.
        await _observatory.SkyLoading;
        Check(_observatory.Sky is { Image: null, Panels.Count: 1 } && _observatory.SkyCaption.Contains("20h") && _observatory.SkyCaption.Contains("+31"),
            $"The demo's Lights say where they pointed ({_observatory.SkyCaption}).");
        Check(_observatory.SkyNote.Contains("non raggiungibile") || _observatory.SkyNote.Contains("not reachable"), $"Without the network the sky must say why it is missing ({_observatory.SkyNote}).");
        var skyFolder = Path.Combine(Path.GetTempPath(), "AstroProjectForge-smoke-sky");
        try
        {
            _observatory.UseSkyClient(new AstroForge.Core.Analysis.SkyImageClient(new HttpClient(new SkyStub(SkyPicture())), skyFolder, offline: false));
            await _observatory.SkyLoading;
            Check(_observatory.Sky is { Image: not null } served && served.Panels.Count == 1 && served.FovDeg >= 4.5 * Math.Max(_observatory.PreviewWidth, _observatory.PreviewHeight) && _observatory.SkyNote.Contains("DSS2"),
                $"A served picture must become the sky behind the frame ({_observatory.SkyNote}).");
            Check(SkyView.Bounds.Width > 100 && SkyView.Scene == _observatory.Sky, "The field card must show the scene.");
            _viewModel.ShowRealSky = false;
            await _observatory.SkyLoading;
            Check(_observatory.Sky is { Image: null, Panels.Count: 1 } && (_observatory.SkyNote.Contains("spento") || _observatory.SkyNote.Contains("off")), "Turned off in the Menu, the sky must leave the frame on an empty sky.");
        }
        finally { _viewModel.ShowRealSky = true; try { if (Directory.Exists(skyFolder)) Directory.Delete(skyFolder, true); } catch (IOException) { } }

        // The catalogues: every filter of the catalogue has a colour you can see and bands you can read, and the lists of cameras and optics open whole, search and scroll.
        foreach (var product in FilterCatalog.Default.Filters)
        {
            var glass = SpectrumColors.Glass(product.Bands, product.Kind);
            // a mix of wide bands is a muted tint, but never the near-black (38, 38, 38) that reading a transmission as a wavelength gave
            Check(Math.Max(glass.R, Math.Max(glass.G, glass.B)) >= 110, $"The glass of {product.Name} must not be dark ({glass}).");
            Check(product.Bands.All(band => band.CentreNm > band.FromNm && band.CentreNm < band.ToNm), $"The bands of {product.Name} must be read from their centre.");
        }
        var antiHalo = FilterCatalog.Default.Find("poahpro")!;
        Check(antiHalo.Bands.Count == 2 && SpectrumColors.GlassBrush(antiHalo.Bands, antiHalo.Kind) is Avalonia.Media.LinearGradientBrush { GradientStops.Count: 4 }, "A dual-band filter must show one colour per line.");
        _shell.CurrentStep = ShellViewModel.SetupStep; await Settle();
        _observatory.BeginEdit(); await Settle();
        BrowseTelescopes(TelescopeBox);
        await Settle();
        var optics = LastCatalog!;
        Check(optics.List.ItemCount == _observatory.TelescopeChoices.Count && _observatory.TelescopeChoices.Count > 500, $"The optics browser must list the whole catalogue ({optics.List.ItemCount}).");
        optics.Search.Text = _observatory.TelescopeChoices[0].Name.Split(' ')[0] + " " + _observatory.TelescopeChoices[0].Name.Split(' ').Last();
        await Settle();
        Check(optics.List.ItemCount is > 0 && optics.List.ItemCount < _observatory.TelescopeChoices.Count, "Searching the optics must narrow the list.");
        optics.Search.Text = "";
        await Settle();
        var scroller = optics.List.GetVisualDescendants().OfType<ScrollViewer>().First();
        Check(scroller.Extent.Height > scroller.Viewport.Height * 3, $"The full list must be long enough to scroll ({scroller.Extent.Height} in {scroller.Viewport.Height}).");
        scroller.Offset = new Vector(0, 400);
        await Settle();
        Check(scroller.Offset.Y > 0, "The list of optics must scroll.");
        var pickedScope = _observatory.TelescopeChoices[5];
        optics.List.SelectedItem = pickedScope;
        await Settle();
        Check(_observatory.SelectedTelescope == pickedScope && _observatory.TelescopeText == pickedScope.Name && !optics.Flyout.IsOpen, "Choosing from the browser must set the optics and close it.");
        BrowseCameras(CameraBox);
        await Settle();
        Check(LastCatalog!.List.ItemCount == _observatory.CameraChoices.Count, "The cameras browser must list every camera.");
        LastCatalog.Flyout.Hide();
        _observatory.IsEditing = false; await Settle();

        // The Moon: the full Moon of 28 August 2026 seen from Rome is high around midnight and below the horizon at midday.
        Check(SkyMath.MoonAltitude(new DateTime(2026, 8, 28, 23, 0, 0, DateTimeKind.Utc), 41.9, 12.5) > 15 && SkyMath.MoonAltitude(new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc), 41.9, 12.5) < -10,
            "The Moon must be high at night and low by day at full Moon.");

        // The export page: the keyword table opening in the right column must leave the left one as it was (the stack picture and the nights keep their height).
        _shell.CurrentStep = ShellViewModel.ExportStep; await Settle(); await Settle();
        var stackHeight = StackCard.Bounds.Height;
        Check(stackHeight >= 430 && Math.Abs(NightsCard.Bounds.Height - 210) < 1, $"The nights card keeps its own height ({NightsCard.Bounds.Height}) and the stack card its minimum ({stackHeight}).");
        KeywordsExpander.IsExpanded = true; await Settle(); await Settle();
        Check(Math.Abs(StackCard.Bounds.Height - stackHeight) < 1 && Math.Abs(NightsCard.Bounds.Height - 210) < 1,
            $"Opening the keywords must not stretch the left column ({stackHeight} → {StackCard.Bounds.Height}, nights {NightsCard.Bounds.Height}).");
        KeywordsExpander.IsExpanded = false; await Settle();

        // A project that mixes rigs: each one is a card to pick, with its own camera, optics and filters, and they share the sky.
        await RunSetupScenarioAsync(null);

        // Data added to a project that was exported: read again in place, what came in is marked, the update copies only that.
        await RunUpdateScenarioAsync(null);

        // Folders and files dropped on the window: sorted, linked where they were dropped, and said.
        await RunDropScenarioAsync(null);

        // The tour walks the real controls of every step to its end.
        StartTour(-1);
        for (var index = 0; index < 30 && Tour.IsRunning; index++) { await Settle(); Tour.Next(); }
        Check(!Tour.IsRunning, "The tour must reach its end.");

        _viewModel.NewProject();
        await Settle();
        Check(!_shell.CanOpen(ShellViewModel.CalibrationStep) || _shell.CurrentStep <= ShellViewModel.CalibrationStep, "A new project must not keep later steps open.");
    }

    /// <summary>
    /// Developer tool (<c>--capture folder</c>): loads the demo project and saves a picture of every step, so the interface
    /// can be looked at without clicking through it. Returns the process exit code.
    /// </summary>
    public async Task<int> RunCaptureAsync(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            // Pictures are taken in the language given with --language (default Italian), past the first-run welcome.
            if (_viewModel.ShowOnboarding) _viewModel.CompleteOnboarding();
            var args = Environment.GetCommandLineArgs();
            var language = Array.IndexOf(args, "--language");
            _viewModel.UiLanguage = language >= 0 && language + 1 < args.Length && args[language + 1].StartsWith("en", StringComparison.OrdinalIgnoreCase) ? UiLocalization.English : UiLocalization.Italian;
            // --size 1100x700 shows the narrow layouts instead of the maximised window.
            var size = Array.IndexOf(args, "--size");
            if (size >= 0 && size + 1 < args.Length && args[size + 1].Split('x') is [var width, var height] && int.TryParse(width, out var w) && int.TryParse(height, out var h))
            {
                WindowState = WindowState.Normal;
                Width = w;
                Height = h;
            }
            await Task.Delay(1800);
            await CaptureAsync(Path.Combine(folder, "1-import-empty.png"));
            await LoadDemoAsync();
            // With a Master Library linked, the import page shows its filled state.
            _viewModel.AddMasterLibrary(Path.Combine(AppDataPaths.Combine("Demo"), "Libreria Master"));
            await _viewModel.ScanAsync();
            await Task.Delay(900);
            var names = new[] { "import", "gear", "calibration", "export" };
            for (var step = 0; step < ShellViewModel.StepCount; step++)
            {
                _shell.CurrentStep = step;
                // the real sky of the gear step comes from the network: wait for it, but not for ever
                if (step == ShellViewModel.SetupStep) await Task.WhenAny(_observatory.SkyLoading, Task.Delay(25000));
                await Task.Delay(1600);
                await CaptureAsync(Path.Combine(folder, $"{step + 1}-{names[step]}.png"));
            }
            OpenSheet(MetadataSheet);
            await Task.Delay(900);
            await CaptureAsync(Path.Combine(folder, "tool-metadata.png"));
            OpenSheet(MastersSheet);
            await Task.Delay(900);
            await CaptureAsync(Path.Combine(folder, "tool-masters.png"));
            CloseSheet();
            _shell.CurrentStep = ShellViewModel.SetupStep;
            _observatory.BeginEdit();
            await Task.Delay(1200);
            await CaptureAsync(Path.Combine(folder, "2-gear-editing.png"));
            // The browser of the whole catalogue, with a search inside, drawn on its own (a pop-up is not part of the window).
            BrowseTelescopes(TelescopeBox);
            await Task.Delay(900);
            LastCatalog!.Search.Text = "newton";
            await Task.Delay(500);
            if (LastCatalog.Flyout.Content is Panel catalogue) { catalogue.Background = new SolidColorBrush(Color.Parse("#0B1122")); await CaptureControlAsync(catalogue, Path.Combine(folder, "2-catalog-browser.png")); }
            LastCatalog.Flyout.Hide();
            _observatory.IsEditing = false;
            // A dual-band filter from the catalogue: its colours, and its bands at their real width.
            var wheelRow = _observatory.Filters.First();
            var dualBand = wheelRow.Choices.First(choice => FilterCatalog.Default.Find(choice.Id ?? "")?.Kind == FilterKind.Multiband);
            wheelRow.Choice = dualBand;
            _observatory.Confirm(wheelRow);
            await Task.Delay(600);
            _observatory.SelectedSlot = _observatory.Filters.ToList().FindIndex(row => row.RawName == wheelRow.RawName);
            await Task.Delay(1400);
            await CaptureAsync(Path.Combine(folder, "2-gear-dualband.png"));
            _observatory.Forget(_observatory.Filters.Single(row => row.RawName == wheelRow.RawName));
            await Task.Delay(500);
            // A framing the demo does not have: two panels with the camera turned 30°, to see the footprints sit right on the sky.
            if (_observatory.Sky is { Image: { } skyImage } skyScene)
            {
                SkyView.Scene = new SkyScene(skyImage, skyScene.FovDeg, [new(-1.1, 0.7, 30, "P1"), new(1.1, -0.7, 30, "P2")]);
                await Task.Delay(1200);
                await CaptureAsync(Path.Combine(folder, "2-gear-mosaic.png"));
            }
            // A real export of the demo (into the capture data folder), then the finished page.
            if (_viewModel.Analysis?.Ready == true)
            {
                _shell.CurrentStep = ShellViewModel.ExportStep;
                await _viewModel.ExportAsync();
                await Task.Delay(2200);
                await CaptureAsync(Path.Combine(folder, "4-export-done.png"));
                // The keyword table opened: the right column grows, and the left one has to stay as it was.
                KeywordsExpander.IsExpanded = true;
                await Task.Delay(900);
                ExportScroll.ScrollToHome();
                await Task.Delay(500);
                await CaptureAsync(Path.Combine(folder, "4-export-keywords-top.png"));
                ExportScroll.ScrollToEnd();
                await Task.Delay(500);
                await CaptureAsync(Path.Combine(folder, "4-export-keywords-end.png"));
                KeywordsExpander.IsExpanded = false;
            }
            await RunSetupScenarioAsync(folder);
            await RunUpdateScenarioAsync(folder);
            await RunDropScenarioAsync(folder);
            Console.WriteLine($"CAPTURE DONE · {folder}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CAPTURE FAILED · {exception}");
            return 1;
        }
    }

    /// <summary>
    /// A project that was exported with one night, then gets the second night added: what the steps show at each moment
    /// (the capture saves a picture of each one).
    /// </summary>
    private async Task RunUpdateScenarioAsync(string? folder)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Settle() => await Task.Delay(400);
        async Task Shot(string name) { if (folder is not null) await CaptureAsync(Path.Combine(folder, name)); }
        var root = AppDataPaths.Combine("Demo");
        _viewModel.NewProject();
        _viewModel.ProjectName = "Cygnus Loop (aggiornamento)";
        _viewModel.DestinationPath = Path.Combine(root, "ExportUpdate");
        foreach (var source in new[] { "ASIAIR", "Libreria Master" }) _viewModel.AddSource(Path.Combine(root, source));
        await _viewModel.ScanAsync();
        await Task.Delay(900);
        _shell.CurrentStep = ShellViewModel.ExportStep;
        await Task.Delay(900);
        await _viewModel.ExportAsync();
        await Task.Delay(1500);
        await Shot("u1-exported-one-night.png");
        Check(_viewModel.ExportState == ExportRunState.Completed && _viewModel.Novelty is null, "A project that was just exported has nothing new.");

        _shell.CurrentStep = ShellViewModel.ImportStep;
        await Task.Delay(900);
        HandleDrop([Path.Combine(root, "NINA")], DropTarget.Captures);
        await Task.Delay(150);
        await Shot("u2-new-source-added.png");

        // The window reads the project again by itself; wait for it.
        for (var wait = 0; wait < 100 && !(_viewModel.HasAnalysis && !_viewModel.IsScanning && !_viewModel.NeedsReanalysis); wait++) await Task.Delay(200);
        await Task.Delay(1800);
        await Shot("u3-rescanned.png");
        Check(_shell.Analyzed && _shell.CurrentStep == ShellViewModel.ImportStep, $"Adding data must read the project again where the person is, not carry on (step {_shell.CurrentStep}).");
        Check(_viewModel.Novelty is { WholeNights: 1, NewLights: 8 } && _shell.HasNovelty && _shell.NoveltyText.Length > 0, "The second night must be reported as new.");
        Check(_shell.SkyNights.Count(night => night.IsNew) == 1 && _shell.SkyNights.All(night => night.MoonAltitudes is { Count: 73 }), "The new night must be marked on the timeline, every night with its Moon.");

        _shell.CurrentStep = ShellViewModel.ExportStep;
        await Task.Delay(1800);
        await Shot("u4-export-update.png");
        if (folder is not null)
        {
            // The card is translucent: give it the night's colour for the close-up.
            var background = NightsCard.Background;
            NightsCard.Background = new SolidColorBrush(Color.Parse("#0B1022"));
            await CaptureControlAsync(NightsCard, Path.Combine(folder, "u5-nightsky.png"));
            NightsCard.Background = background;
        }
        Check(_viewModel.HasExportHistory && _shell.NextKind == NextActionKind.Export && (_shell.NextLabel.StartsWith("Update") || _shell.NextLabel.StartsWith("Aggiorna")), $"An update must say it is one ({_shell.NextLabel}).");
        await _viewModel.ExportAsync();
        await Settle();
        Check(_viewModel.Novelty is null && _viewModel.ExportState == ExportRunState.Completed, "Once exported, nothing is new any more.");
    }

    /// <summary>
    /// The demo plus six Lights of a second camera on a short lens, pointed at the same target: the project has two rigs. The gear page offers both, describes the one picked
    /// (camera, optics, wheel) and keeps what the user says about one from leaking into the other; the sky shows both fields.
    /// </summary>
    private async Task RunSetupScenarioAsync(string? folder)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Settle() => await Task.Delay(450);
        var root = AppDataPaths.Combine("Demo");
        var second = AppDataPaths.Combine("SecondRig");
        if (Directory.Exists(second)) Directory.Delete(second, true);
        Directory.CreateDirectory(second);
        for (var index = 0; index < 16; index++) WriteTinyLight(Path.Combine(second, $"light_{index:00}.fits"), index);
        _viewModel.NewProject();
        _viewModel.ProjectName = "Cygnus Loop (due setup)";
        _viewModel.DestinationPath = Path.Combine(root, "ExportTwoRigs");
        foreach (var source in new[] { "ASIAIR", "NINA", "Libreria Master" }) _viewModel.AddSource(Path.Combine(root, source));
        _viewModel.AddSource(second);
        await _viewModel.ScanAsync();
        _shell.CurrentStep = ShellViewModel.SetupStep;
        await Settle();
        await _observatory.SkyLoading;

        var setups = _viewModel.Setups;
        Check(setups.Count == 2 && setups[0].CameraName.Contains("2600") && setups[1].CameraName.Contains("294") && setups[1].FocalMm == 135 && setups[1].Lights == 16,
            $"The project must have two rigs, the demo's first ({string.Join(" | ", setups.Select(item => $"{item.Key}/{item.FocalMm}/{item.Lights}"))}).");
        Check(_observatory.HasSeveralSetups && _observatory.SetupRows.Count == 2 && _observatory.SetupRows[0].IsSelected && !_observatory.SetupRows[1].IsSelected, "Both rigs must be offered, the main one picked.");
        Check(_observatory.CameraName.Contains("2600") && _observatory.Filters.Count > 1, $"The gear page must describe the main rig ({_observatory.CameraName}).");
        Check(_observatory.Sky is { Rigs.Count: 1 } && _observatory.Sky.Rigs[0].WidthDeg > _observatory.PreviewWidth, $"The other rig must be on the sky, with its wider field ({_observatory.Sky?.Rigs.Count} rigs, {_observatory.Sky?.Rigs.FirstOrDefault()?.WidthDeg} against {_observatory.PreviewWidth}).");
        if (folder is not null) { await Task.Delay(1500); await CaptureAsync(Path.Combine(folder, "2-gear-two-setups.png")); }

        // Picking the second rig describes it: its camera, its focal length, its own wheel; the first becomes the dashed one on the sky.
        _observatory.SelectSetup(setups[1].Key);
        await Settle();
        await _observatory.SkyLoading;
        Check(_observatory.SetupRows[1].IsSelected && _observatory.CameraName.Contains("294") && _observatory.FocalText == "135 mm", $"Picking the second rig must describe it ({_observatory.CameraName}, {_observatory.FocalText}).");
        Check(_observatory.Filters.Count == 8 && _observatory.Filters.Any(row => row.RawName == "OIII"), $"The second rig has its own wheel, with its eight filters ({_observatory.Filters.Count}).");
        Check(_observatory.Sky is { Rigs.Count: 1 } && _observatory.Sky.Rigs[0].WidthDeg < _observatory.PreviewWidth, $"Now the first rig is the dashed one, with its narrower field ({_observatory.Sky?.Rigs.Count} rigs, {_observatory.Sky?.Rigs.FirstOrDefault()?.WidthDeg} against {_observatory.PreviewWidth}).");
        if (folder is not null) { await Task.Delay(1500); await CaptureAsync(Path.Combine(folder, "2-gear-second-setup.png")); }

        // What is said about this rig is this rig's: the first keeps what it had.
        var profile = _viewModel.Instrument!;
        _viewModel.SetEquipment(profile, new AstroForge.Core.Equipment.EquipmentOverride { TelescopeName = "Obiettivo 135", FocalMm = 135, ApertureMm = 50, ReducerFactor = 1 });
        Check(_viewModel.Instrument?.Override is { TelescopeName: "Obiettivo 135" } && _viewModel.EquipmentFor(setups[1].Key) is not null && _viewModel.EquipmentFor(setups[0].CameraKey) is null
            && _viewModel.InstrumentFor(setups[0])!.Override is null, "The optics said for the second rig must stay with it.");
        _viewModel.ClearEquipment(_viewModel.Instrument!);
        Check(_viewModel.Instrument?.Override is null && _viewModel.EquipmentFor(setups[1].Key) is null, "Forgetting what was said about the second rig must forget it.");

        _observatory.SelectSetup(setups[0].Key);
        await Settle();
        Check(_observatory.SetupRows[0].IsSelected && _observatory.CameraName.Contains("2600"), "Going back to the first rig must describe it again.");
        try { Directory.Delete(second, true); } catch (IOException) { }
    }

    // A header and one block of nothing: enough for a scan to read the camera, the lens, the filter and where it pointed.
    private static void WriteTinyLight(string path, int index)
    {
        static string Card(string key, object value) =>
            (key.PadRight(8) + "= " + (value is string text ? ("'" + text + "'").PadRight(20) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!.PadLeft(20))).PadRight(80);
        var cards = new List<string>
        {
            Card("SIMPLE", "T"), Card("BITPIX", 16), Card("NAXIS", 2), Card("NAXIS1", 4144), Card("NAXIS2", 2822), Card("BZERO", 32768), Card("BSCALE", 1),
            Card("IMAGETYP", "Light"), Card("INSTRUME", "ZWO ASI294MC Pro"), Card("TELESCOP", "Obiettivo corto"), Card("FOCALLEN", 135.0), Card("XPIXSZ", 4.63), Card("YPIXSZ", 4.63),
            Card("EXPTIME", 120.0), Card("FILTER", new[] { "L", "R", "G", "B", "Ha", "OIII", "SII", "L-eXtreme" }[index % 8]), Card("GAIN", 120.0), Card("OFFSET", 30.0), Card("SET-TEMP", -10.0), Card("CCD-TEMP", -10.0), Card("XBINNING", 1), Card("YBINNING", 1),
            Card("DATE-OBS", $"2026-08-2{1 + index % 2}T22:{10 + index:00}:00"), Card("OBJECT", "Cygnus Loop"), Card("OBJCTRA", "20 56 24"), Card("OBJCTDEC", "+31 43 00"), Card("ROTATOR", 12.0),
            "END".PadRight(80)
        };
        var header = string.Concat(cards).PadRight((string.Concat(cards).Length + 2879) / 2880 * 2880);
        var bytes = System.Text.Encoding.ASCII.GetBytes(header).Concat(new byte[2880]).ToArray();
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Dragging over the window and dropping: the two targets, what each one links, and what is left out.</summary>
    private async Task RunDropScenarioAsync(string? folder)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Settle() => await Task.Delay(450);
        static async Task<bool> Until(Func<bool> condition) { for (var wait = 0; wait < 40 && !condition(); wait++) await Task.Delay(100); return condition(); }
        async Task Shot(string name) { if (folder is not null) await CaptureAsync(Path.Combine(folder, name)); }
        var root = AppDataPaths.Combine("Demo");
        _viewModel.NewProject();
        _shell.CurrentStep = ShellViewModel.ImportStep;
        await Settle();

        // The overlay: right half lights the captures, left half the library, and it goes away by itself.
        ShowDropOverlay(DropTarget.Captures);
        await Settle();
        await Shot("d1-drag-captures.png");
        Check(DropOverlay.IsVisible && DropCapturesZone.Classes.Contains("over") && !DropLibraryZone.Classes.Contains("over"), "Dragging over the right half must light the captures.");
        ShowDropOverlay(DropTarget.MasterLibrary);
        await Settle();
        await Shot("d2-drag-library.png");
        Check(DropLibraryZone.Classes.Contains("over") && !DropCapturesZone.Classes.Contains("over"), "Dragging over the left half must light the library.");
        HideDropOverlay();
        Check(await Until(() => !DropOverlay.IsVisible) && !DropLibraryZone.Classes.Contains("over"), "The overlay must go away when the drag ends.");

        // Captures: a folder is linked whole, a text file is left out and counted.
        var notes = Path.Combine(root, "notes.txt");
        File.WriteAllText(notes, "not an image");
        var asiair = Path.Combine(root, "ASIAIR");
        var plan = HandleDrop([asiair, notes], DropTarget.Captures);
        await Settle();
        Check(plan.Folders.Count == 1 && plan.Ignored.Count == 1 && _viewModel.SourcePaths.Contains(asiair, PathIdentity.Comparer), "The dropped folder must become a source and the text file must be left out.");
        Check(ToastText.Text is { } toast && (toast.Contains("Aggiunto") || toast.Contains("Added")) && toast.Contains("ASIAIR") && (toast.Contains("non FITS") || toast.Contains("not FITS")), $"The drop must say what it did ({ToastText.Text}).");
        await Shot("d3-dropped-captures.png");

        // The same folder again: nothing doubles.
        HandleDrop([asiair], DropTarget.Captures);
        Check(_viewModel.SourcePaths.Count == 1 && ToastText.Text is { } again && (again.Contains("Già") || again.Contains("Already")), "A folder dropped twice must stay one.");

        // A single Master dropped on the library stands for its folder; a single capture on the captures is a source of its own.
        var masters = Path.Combine(root, "Libreria Master");
        var master = Directory.EnumerateFiles(masters, "*.fits", SearchOption.AllDirectories).First();
        HandleDrop([master], DropTarget.MasterLibrary);
        Check(_viewModel.MasterLibraries.Any(item => PathIdentity.Equals(item.Path, Path.GetDirectoryName(master))), "A Master dropped on the library must link the folder it lives in.");
        var light = Directory.EnumerateFiles(Path.Combine(root, "NINA"), "*.fits", SearchOption.AllDirectories).First();
        HandleDrop([light], DropTarget.Captures);
        Check(_viewModel.SourcePaths.Contains(light, PathIdentity.Comparer), "A single capture dropped must become a source.");
        await Settle();
        await Shot("d4-dropped-library.png");

        // The window's own drag events, carrying a real folder: enter lights the overlay, drop links it and puts the overlay away.
        var nina = Path.Combine(root, "NINA");
        var item = await StorageProvider.TryGetFolderFromPathAsync(nina);
        Check(item is not null, "The storage provider must find the folder to drop.");
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(item!));
        var position = new Point(Bounds.Width * 0.75, Bounds.Height / 2);
        RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, data, this, position, KeyModifiers.None));
        await Settle();
        Check(DropOverlay.IsVisible && DropCapturesZone.Classes.Contains("over"), "A drag over the right half of the window must light the captures.");
        RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, this, position, KeyModifiers.None) { RoutedEvent = DragDrop.DropEvent });
        await Settle();
        Check(_viewModel.SourcePaths.Contains(nina, PathIdentity.Comparer), "Dropping a folder on the window must link it.");
        Check(await Until(() => !DropOverlay.IsVisible), "The overlay must go away once the folder is dropped.");
        Check(!_viewModel.ShowOnboarding, "Dropping data on the welcome must close it: the person is ready.");

        // Nothing usable: nothing changes, and it is said.
        var count = _viewModel.SourcePaths.Count;
        HandleDrop([notes], DropTarget.Captures);
        Check(_viewModel.SourcePaths.Count == count && ToastText.Text is { } nothing && (nothing.Contains("Niente") || nothing.Contains("Nothing")), "A drop without images must change nothing.");
        _viewModel.NewProject();
        await Settle();
    }

    // One control at double size, to look at its drawing closely.
    /// <summary>A picture of "sky" for the stub: dots on black, as a PNG, big enough to pass for a real answer.</summary>
    private static byte[] SkyPicture()
    {
        var random = new Random(7);
        var canvas = new Canvas { Width = 256, Height = 256, Background = Brushes.Black };
        for (var index = 0; index < 400; index++)
        {
            var size = 1 + random.NextDouble() * 3;
            var star = new Avalonia.Controls.Shapes.Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromRgb((byte)(120 + random.Next(135)), (byte)(120 + random.Next(135)), (byte)(120 + random.Next(135)))) };
            Canvas.SetLeft(star, random.NextDouble() * 256);
            Canvas.SetTop(star, random.NextDouble() * 256);
            canvas.Children.Add(star);
        }
        canvas.Measure(new Size(256, 256));
        canvas.Arrange(new Rect(0, 0, 256, 256));
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(256, 256));
        bitmap.Render(canvas);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    private sealed class SkyStub(byte[] picture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
    }

    private async Task CaptureControlAsync(Control control, string path)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var size = new PixelSize(Math.Max(1, (int)control.Bounds.Width * 2), Math.Max(1, (int)control.Bounds.Height * 2));
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(192, 192));
        bitmap.Render(control);
        using var file = File.Create(path);
        bitmap.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private async Task CaptureAsync(string path)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var size = new PixelSize(Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height));
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render((Visual)Content!);
        using var file = File.Create(path);
        bitmap.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    // ---- Steps, tools and the command palette ----

    // The tools open as a sheet over the steps: the steps themselves are pages, never hidden in a sheet.
    private static readonly string[] SheetEyebrows =
        ["Strumenti · Frame e metadati", "Strumenti · Statistiche", "Strumenti · Qualità dei frame", "Strumenti · Libreria Master", "Diagnostica"];
    private const int MetadataSheet = 0, StatisticsSheet = 1, QualitySheet = 2, MastersSheet = 3, LogSheet = 4;

    /// <summary>Opens a step: a step can be opened once there is something to show for it.</summary>
    private void GoToStep(int step)
    {
        CloseSheet();
        ClosePalette();
        SettingsPanel.IsVisible = false;
        if (!_shell.CanOpen(step)) return;
        _shell.CurrentStep = step;
    }

    private void Shell_StepChanged(object? sender, int step)
    {
        // Page content is created on first visit, after the last translation pass.
        if (IsLoaded) ScheduleLocalization();
        if (Backdrop is not null && step != _lastScreen) Backdrop.Warp(step > _lastScreen ? 1 : -1);
        _lastScreen = step;
        ScrollPageHome(step);
        if (step == ShellViewModel.ExportStep) { PreparePlanPreview(); ScheduleExportVisuals(); }
        if (step == ShellViewModel.CalibrationStep) RefreshCalibrationVisuals();
    }

    private void Back_Click(object? sender, RoutedEventArgs e) => GoToStep(_shell.CurrentStep - 1);

    private bool _reanalyzing;

    /// <summary>
    /// A source or library was added to (or taken out of) a project that was already read: read it again where the person is,
    /// instead of leaving every step empty and sending them back to the start. What came in is marked afterwards.
    /// </summary>
    private void AutoReanalyze()
    {
        if (_reanalyzing || !_viewModel.NeedsReanalysis || !_viewModel.CanAnalyzeProject) return;
        _reanalyzing = true;
        _shell.StayOnStep();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await RunAsync("AF-SCAN-001", () => _viewModel.ScanAsync()); }
            finally { _reanalyzing = false; }
        }, DispatcherPriority.Background);
    }

    private void NoveltyGoToExport_Click(object? sender, RoutedEventArgs e) => GoToStep(ShellViewModel.ExportStep);
    private void DismissNovelty_Click(object? sender, RoutedEventArgs e) => _viewModel.DismissNovelty();

    // The catalogue lists are long: the box above filters as you type, the button opens the whole list to look around (search inside, scrolls).
    internal CatalogBrowser.Handle? LastCatalog { get; private set; }

    internal void BrowseCameras(Control anchor)
    {
        var english = _viewModel.UiLanguage == UiLocalization.English;
        LastCatalog = CatalogBrowser.Show(anchor, _observatory.CameraChoices, camera => camera.Name,
            camera => camera.Type switch { CameraSensorType.Mono => english ? "mono" : "mono", CameraSensorType.Color => english ? "colour" : "colore", CameraSensorType.Dslr => "DSLR", CameraSensorType.DslrModified => "DSLR mod", _ => "" },
            _observatory.SelectedCamera, camera => _observatory.SelectedCamera = camera,
            english ? "Search the cameras" : "Cerca tra le camere",
            (shown, total) => english ? $"{shown} of {total} cameras" : $"{shown} di {total} camere");
    }

    internal void BrowseTelescopes(Control anchor)
    {
        var english = _viewModel.UiLanguage == UiLocalization.English;
        LastCatalog = CatalogBrowser.Show(anchor, _observatory.TelescopeChoices, scope => scope.Name,
            scope => (scope.ApertureMm, scope.FocalMm) switch
            {
                ({ } aperture, { } focal) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Ø {aperture:0} · {focal:0} mm"),
                (null, { } focal) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{focal:0} mm"),
                _ => ""
            },
            _observatory.SelectedTelescope, scope => _observatory.SelectedTelescope = scope,
            english ? "Search the telescopes" : "Cerca tra le ottiche",
            (shown, total) => english ? $"{shown} of {total} telescopes" : $"{shown} di {total} ottiche");
    }

    private void BrowseCameras_Click(object? sender, RoutedEventArgs e) => BrowseCameras(sender as Control ?? CameraBox);
    private void BrowseTelescopes_Click(object? sender, RoutedEventArgs e) => BrowseTelescopes(sender as Control ?? TelescopeBox);

    // A page opens at its top, whatever the person did on it the last time.
    private void ScrollPageHome(int step)
    {
        switch (step)
        {
            case ShellViewModel.ImportStep: ImportPage.ScrollToHome(); break;
            case ShellViewModel.SetupStep: SetupScroll.ScrollToHome(); break;
            case ShellViewModel.CalibrationStep: CalibrationScroll.ScrollToHome(); break;
            case ShellViewModel.ExportStep: ExportScroll.ScrollToHome(); break;
        }
    }

    private void OpenSheet(int index)
    {
        SettingsPanel.IsVisible = false;
        ClosePalette();
        ToolsButton.Flyout?.Hide();
        SheetTabs.SelectedIndex = index;
        SheetEyebrow.Text = CanvasText.T(SheetEyebrows[index]).ToUpperInvariant();
        if (index == LogSheet) _viewModel.RefreshDiagnostics();
        if (SheetHost.IsVisible) return;
        SheetHost.IsVisible = true;
        var shift = new TranslateTransform(48, 0);
        Drawer.RenderTransform = shift;
        Drawer.Opacity = 0;
        SheetHost.Opacity = 0;
        _ = Motion.Tween(this, TimeSpan.FromMilliseconds(420), Motion.EaseOutExpo, t =>
        {
            SheetHost.Opacity = Math.Min(1, t * 2);
            Drawer.Opacity = t;
            shift.X = 48 * (1 - t);
        });
        ScheduleLocalization();
    }

    private void CloseSheet()
    {
        if (!SheetHost.IsVisible) return;
        SheetHost.IsVisible = false;
        Drawer.RenderTransform = null;
        Drawer.Opacity = 1;
        SheetHost.Opacity = 1;
    }

    private void OpenPalette()
    {
        _shell.Query = "";
        PaletteHost.IsVisible = true;
        Dispatcher.UIThread.Post(() => PaletteQuery.Focus(), DispatcherPriority.Input);
    }

    private void ClosePalette() => PaletteHost.IsVisible = false;

    private void RunCommand(CommandItem? command)
    {
        ClosePalette();
        switch (command?.Id)
        {
            case "next": Next_Click(null, new RoutedEventArgs()); break;
            case "import": GoToStep(ShellViewModel.ImportStep); break;
            case "setup": GoToStep(ShellViewModel.SetupStep); break;
            case "calibration": GoToStep(ShellViewModel.CalibrationStep); break;
            case "export": GoToStep(ShellViewModel.ExportStep); break;
            case "metadata": OpenSheet(MetadataSheet); break;
            case "stats": OpenSheet(StatisticsSheet); break;
            case "quality": OpenSheet(QualitySheet); break;
            case "masters": OpenSheet(MastersSheet); break;
            case "log": OpenSheet(LogSheet); break;
            case "analyze": if (_viewModel.CanAnalyzeProject) Analyze_Click(null, new RoutedEventArgs()); break;
            case "addFolder": AddSources_Click(null, new RoutedEventArgs()); break;
            case "addLibrary": AddLibrary_Click(null, new RoutedEventArgs()); break;
            case "new": NewProject_Click(null, new RoutedEventArgs()); break;
            case "open": OpenProject_Click(null, new RoutedEventArgs()); break;
            case "save": SaveProject_Click(null, new RoutedEventArgs()); break;
            case "saveAs": SaveProjectAs_Click(null, new RoutedEventArgs()); break;
            case "demo": OpenDemo_Click(null, new RoutedEventArgs()); break;
            case "tour": StartTour(-1); break;
            case "menu": CloseSheet(); SettingsPanel.IsVisible = true; break;
            case "updates": CheckUpdates_Click(null, new RoutedEventArgs()); CloseSheet(); SettingsPanel.IsVisible = true; break;
        }
    }

    private void OpenPalette_Click(object? sender, RoutedEventArgs e) => OpenPalette();
    private void PaletteScrim_PointerPressed(object? sender, PointerPressedEventArgs e) => ClosePalette();
    private void PaletteList_Tapped(object? sender, TappedEventArgs e) => RunCommand(PaletteList.SelectedItem as CommandItem);

    private void PaletteQuery_KeyDown(object? sender, KeyEventArgs e)
    {
        var count = _shell.Commands.Count;
        if (e.Key == Key.Down && count > 0) { _shell.CommandIndex = (_shell.CommandIndex + 1) % count; e.Handled = true; }
        else if (e.Key == Key.Up && count > 0) { _shell.CommandIndex = (_shell.CommandIndex + count - 1) % count; e.Handled = true; }
        else if (e.Key == Key.Enter) { RunCommand(_shell.Commands.ElementAtOrDefault(Math.Max(0, _shell.CommandIndex))); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClosePalette(); e.Handled = true; }
    }

    private void CloseSheet_Click(object? sender, RoutedEventArgs e) => CloseSheet();
    private void SheetScrim_PointerPressed(object? sender, PointerPressedEventArgs e) => CloseSheet();
    private void Drawer_PointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
    private void OpenMetadata_Click(object? sender, RoutedEventArgs e) => OpenSheet(MetadataSheet);
    private void OpenStatistics_Click(object? sender, RoutedEventArgs e) => OpenSheet(StatisticsSheet);
    private void OpenQuality_Click(object? sender, RoutedEventArgs e) => OpenSheet(QualitySheet);
    private void OpenMasters_Click(object? sender, RoutedEventArgs e) => OpenSheet(MastersSheet);

    private void SheetTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != SheetTabs) return;
        UpdateInspectorLayout();
        if (IsLoaded) ScheduleLocalization();
    }

    /// <summary>The one button that always does the next thing the project needs, in the same place on every step.</summary>
    private async void Next_Click(object? sender, RoutedEventArgs e)
    {
        switch (_shell.NextKind)
        {
            case NextActionKind.AddSources: AddSources_Click(sender, e); break;
            case NextActionKind.Analyze: Analyze_Click(sender, e); break;
            case NextActionKind.Continue: GoToStep(_shell.CurrentStep + 1); break;
            case NextActionKind.Resolve when _shell.CurrentStep != ShellViewModel.CalibrationStep: GoToStep(ShellViewModel.CalibrationStep); break;
            case NextActionKind.Resolve: CalibrationScroll.ScrollToHome(); PulseCard(ResolveCard); break;
            case NextActionKind.NameProject: ProjectNameBox.Focus(); PulseCard(ExportCard); break;
            case NextActionKind.ChooseDestination: ChooseDestination_Click(sender, e); break;
            case NextActionKind.Export: await RunAsync("AF-EXPORT-001", () => _viewModel.ExportAsync()); break;
            case NextActionKind.OpenPixInsight: OpenWbppInstance_Click(sender, e); break;
        }
    }

    private void PulseCard(Control card)
    {
        card.BringIntoView();
        if (Motion.Reduced) return;
        var scale = new ScaleTransform(1, 1);
        card.RenderTransformOrigin = RelativePoint.Center;
        card.RenderTransform = scale;
        _ = Motion.Tween(this, TimeSpan.FromMilliseconds(700), t => t, t =>
        {
            var bump = Math.Sin(t * Math.PI) * 0.02;
            scale.ScaleX = scale.ScaleY = 1 + bump;
        }).ContinueWith(_ => Dispatcher.UIThread.Post(() => card.RenderTransform = null));
    }

    private void UpdateNextFill()
    {
        var width = NextButton.Bounds.Width + 40;
        NextFill.Width = _viewModel.IsScanning ? width * Math.Clamp(_viewModel.Progress / 100, 0, 1) : 0;
    }

    private void UseCandidate(int index)
    {
        if (_shell.Choice is not { } item || index >= item.Candidates.Count) return;
        item.SelectedCandidate = item.Candidates[index];
        _viewModel.AssignReviewCandidate(item, ReviewAssignmentScope.Light);
    }

    private void UseFirstCandidate_Click(object? sender, RoutedEventArgs e) => UseCandidate(0);
    private void UseSecondCandidate_Click(object? sender, RoutedEventArgs e) => UseCandidate(1);

    private void ResolveImport_Click(object? sender, RoutedEventArgs e)
    {
        switch (_shell.Choice)
        {
            case { CanImportFlat: true }: AddSources_Click(sender, e); break;
            case { CanAddMasterLibrary: true }: AddLibrary_Click(sender, e); break;
            case { CanEditMetadata: true } item: _viewModel.SelectReviewItem(item); OpenSheet(MetadataSheet); break;
            default: ReviewExpander.IsExpanded = true; break;
        }
    }

    /// <summary>Space replays the nights stacking up one by one, as the mockup's play button.</summary>
    private async void PlayNights_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingNights || !_shell.HasNights) return;
        _playingNights = true;
        try
        {
            if (Motion.Reduced) { _shell.NightIndex = _shell.NightCount; return; }
            var delay = Math.Clamp(2400 / _shell.NightCount, 90, 400);
            for (var night = 1; night <= _shell.NightCount; night++)
            {
                _shell.NightIndex = night;
                await Task.Delay(delay);
            }
        }
        finally { _playingNights = false; }
    }

    private void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _viewModel.IsScanning || !IsLoaded) return;
        ToastText.Text = message;
        Toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ConfirmProfile_Click(object? sender, RoutedEventArgs e) => _observatory.ConfirmProfile();
    private void ResetProfile_Click(object? sender, RoutedEventArgs e) => _observatory.ResetProfile();
    private void CancelProfile_Click(object? sender, RoutedEventArgs e) => _observatory.IsEditing = false;
    private void ApplyProfile_Click(object? sender, RoutedEventArgs e) => _observatory.ApplyEdit();

    private void ScheduleLocalization()
    {
        if (_localizationPending) return;
        _localizationPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _localizationPending = false;
            CanvasText.Language = _viewModel.UiLanguage;
            AvaloniaLocalizationAdapter.Apply(this, _viewModel.UiLanguage);
            InvalidateCanvasText(this);
        }, DispatcherPriority.Background);
    }

    private void SelectDensity()
    {
        DensitySelector.SelectedItem = DensitySelector.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _viewModel.UiDensity, StringComparison.Ordinal));
    }

    private void DensitySelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DensitySelector.SelectedItem is ComboBoxItem item)
        {
            _viewModel.UiDensity = item.Tag?.ToString() ?? "Comoda";
            _viewModel.SaveState();
        }
    }
    private void UiPreferenceChanged_Click(object? sender, RoutedEventArgs e) => _viewModel.SaveState();

    private bool _exportStacked;

    /// <summary>
    /// Wide: the picture of the stack and the nights on the left, the export and WBPP on the right. Narrow: one column, with the export first
    /// (it is what the step is for), so the pipeline and the keyword table keep the room they need to be read.
    /// </summary>
    private void ArrangeExportColumns(bool stacked)
    {
        if (stacked == _exportStacked) return;
        _exportStacked = stacked;
        ExportGrid.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : new ColumnDefinitions("1.3*,*");
        ExportGrid.RowDefinitions = stacked ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        ExportGrid.RowSpacing = stacked ? 20 : 0;
        Grid.SetColumn(ExportRight, stacked ? 0 : 1);
        Grid.SetRow(ExportRight, 0);
        Grid.SetColumn(ExportLeft, 0);
        Grid.SetRow(ExportLeft, stacked ? 1 : 0);
    }

    /// <summary>The keyword table opens at the foot of the page: bring it into view instead of leaving it below the fold.</summary>
    private void KeywordsExpander_Expanded(object? sender, RoutedEventArgs e)
    {
        // its rows are created now, after the last translation pass
        ScheduleLocalization();
        Dispatcher.UIThread.Post(() => KeywordsExpander.BringIntoView(), DispatcherPriority.Background);
    }

    private void SetupChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }) _observatory.SelectSetup(key);
    }

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (PaletteHost.IsVisible) ClosePalette();
            else if (SettingsPanel.IsVisible) SettingsPanel.IsVisible = false;
            else if (SheetHost.IsVisible) CloseSheet();
            else return;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) OpenUrl(GuideUrl);
            else StartTour(SheetHost.IsVisible ? -1 : _shell.CurrentStep);
            e.Handled = true;
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (control && e.Key == Key.K)
        {
            if (PaletteHost.IsVisible) ClosePalette(); else OpenPalette();
            e.Handled = true;
        }
        else if (control && e.Key == Key.O)
        {
            OpenProject_Click(sender, e);
            e.Handled = true;
        }
        else if (control && e.Key == Key.N)
        {
            await NewProjectCoreAsync();
            e.Handled = true;
        }
        else if (control && e.Key == Key.S)
        {
            await SaveProjectAsync(shift);
            e.Handled = true;
        }
        else if (control && e.Key == Key.Enter && _viewModel.CanAnalyzeProject)
        {
            await RunAsync("AF-SCAN-001", () => _viewModel.ScanAsync());
            e.Handled = true;
        }
        else if (control && e.Key == Key.OemComma)
        {
            SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
            e.Handled = true;
        }
        else if (alt && e.Key >= Key.D1 && e.Key <= Key.D4)
        {
            GoToStep((int)e.Key - (int)Key.D1);
            e.Handled = true;
        }
        else if (alt && e.Key >= Key.D5 && e.Key <= Key.D8)
        {
            OpenSheet((int)e.Key - (int)Key.D5);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && !SheetHost.IsVisible && !PaletteHost.IsVisible
                 && _shell.CurrentStep == ShellViewModel.ExportStep && FocusManager?.GetFocusedElement() is not (TextBox or Slider or Button or ComboBox or ListBoxItem or AutoCompleteBox))
        {
            PlayNights_Click(sender, e);
            e.Handled = true;
        }
    }

    private void ConfirmFilter_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FilterSlotRow row) _observatory.Confirm(row);
    }

    private void ForgetFilter_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FilterSlotRow row) _observatory.Forget(row);
    }

    private static void InvalidateCanvasText(Visual root)
    {
        foreach (var visual in root.GetVisualDescendants())
            if (visual is SpectrumBar or PassbandView or FieldOfViewView or NightsTimeline or StepperBar or NightSky or CalibrationRing or ResolvePaths or OpticalTrain or SamplingGauge or SensorFrame)
                visual.InvalidateVisual();
    }

    /// <summary>
    /// Opening sequence: the iris opens on the sky, then the interface comes into focus
    /// like a star pulled sharp by the focuser.
    /// </summary>
    private void PlayFirstLight()
    {
        if (Motion.Reduced) return;
        var blur = new BlurEffect { Radius = 18 };
        var scale = new ScaleTransform(1.02, 1.02);
        RootLayout.Opacity = 0;
        RootLayout.Effect = blur;
        RootLayout.RenderTransformOrigin = RelativePoint.Center;
        RootLayout.RenderTransform = scale;
        _ = FirstLight.PlayAsync(async () =>
        {
            await Motion.Tween(this, TimeSpan.FromMilliseconds(950), Motion.EaseOutExpo, t =>
            {
                RootLayout.Opacity = Math.Min(1, t * 1.4);
                blur.Radius = 18 * (1 - t);
                scale.ScaleX = scale.ScaleY = 1.02 - 0.02 * t;
            });
            RootLayout.Effect = null;
            RootLayout.RenderTransform = null;
            RootLayout.Opacity = 1;
        });
    }

    private void UpdateInspectorLayout()
    {
        // TabControl raises SelectionChanged while InitializeComponent is still assigning named controls.
        if (AnalysisGrid is null) return;
        AnalysisGrid.ColumnDefinitions[1].Width = new GridLength(_viewModel.HasSelection ? 5 : 0);
        AnalysisGrid.ColumnDefinitions[2].Width = new GridLength(_viewModel.HasSelection ? _viewModel.InspectorPanelWidth : 0);
    }

    private void ApplyCommandLine()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--source" && index + 1 < args.Length) _viewModel.AddSource(args[++index]);
            else if (args[index] == "--library" && index + 1 < args.Length) _viewModel.AddMasterLibrary(args[++index]);
            else if (args[index] == "--demo") Dispatcher.UIThread.Post(() => OpenDemo_Click(null, new RoutedEventArgs()), DispatcherPriority.Background);
            else if (args[index] == UpdatedArgument) _updatedOnLaunch = true;
            // The Windows file association launches the app as `AstroProjectForge.exe "%1"`.
            else if (_startupProjectPath is null && !args[index].StartsWith("--", StringComparison.Ordinal)
                && args[index].EndsWith(ProjectFileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(args[index]))
                _startupProjectPath = Path.GetFullPath(args[index]);
        }
    }

    private async Task OpenStartupProjectAsync(string path)
    {
        if (await ConfirmProjectReplacementAsync(opening: true)) await RunAsync("AF-PROJECT-OPEN-001", () => _viewModel.LoadProjectAsync(path));
    }

    private void ShowUpdateCompletedNotification()
    {
        var message = _viewModel.UiLanguage == UiLocalization.English
            ? $"Updated to {ReleaseIdentity.Version}"
            : $"Aggiornamento {ReleaseIdentity.Version} completato";
        new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 1 }
            .Show(new Notification("AstroProject Forge", message, NotificationType.Success, TimeSpan.FromSeconds(5)));
    }

    private async void AddSources_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Aggiungi cartelle FITS/XISF", AllowMultiple = true });
        foreach (var folder in folders) if (folder.TryGetLocalPath() is { } path) _viewModel.AddSource(path);
    }

    private async void AddFiles_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importa immagini astronomiche", AllowMultiple = true, FileTypeFilter = [AstroImages] });
        foreach (var file in files) if (file.TryGetLocalPath() is { } path) _viewModel.AddSource(path);
    }

    private void RemoveSourceItem_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is string path) _viewModel.RemoveSource(path);
    }

    private void RemoveLibraryItem_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MasterLibraryItem item) return;
        _viewModel.SelectedMasterLibrary = item;
        _viewModel.RemoveSelectedMasterLibrary();
    }

    private void MoveLibraryItemUp_Click(object? sender, RoutedEventArgs e) => MoveLibraryItem(sender, -1);
    private void MoveLibraryItemDown_Click(object? sender, RoutedEventArgs e) => MoveLibraryItem(sender, 1);

    private void MoveLibraryItem(object? sender, int direction)
    {
        if ((sender as Control)?.DataContext is not MasterLibraryItem item) return;
        _viewModel.SelectedMasterLibrary = item;
        _viewModel.MoveSelectedMasterLibrary(direction);
    }

    // The empty cards are big targets: a tap anywhere on them does what their button does (the button itself handles its own tap).
    private static bool TappedOnButton(PointerReleasedEventArgs e) => e.Source is Visual visual && visual.FindAncestorOfType<Button>(true) is not null;

    private void LibraryDrop_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!TappedOnButton(e)) AddLibrary_Click(sender, e);
    }

    private void SourcesDrop_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!TappedOnButton(e)) AddSources_Click(sender, e);
    }

    // ---- Drag and drop: folders and files dropped anywhere on the window ----

    private long _dragSeen;

    // The welcome on a first launch does not get in the way: dropping a folder there is the person saying they are ready. The tour, which walks the real controls, does not take drops.
    private bool CanTakeDrop(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File) && !Tour.IsRunning;

    // Left half of the window is the Master Library, right half the captures: the same order as the Import page.
    private DropTarget DropZoneAt(DragEventArgs e) => e.GetPosition(this).X < Bounds.Width / 2 ? DropTarget.MasterLibrary : DropTarget.Captures;

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        if (!CanTakeDrop(e)) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        _dragSeen = Stopwatch.GetTimestamp();
        ShowDropOverlay(DropZoneAt(e));
    }

    // Leaving the window and moving between its children both raise a leave: only a silence of the drag really means "gone".
    private void Window_DragLeave(object? sender, DragEventArgs e)
    {
        var seen = _dragSeen;
        DispatcherTimer.RunOnce(() => { if (_dragSeen == seen) HideDropOverlay(); }, TimeSpan.FromMilliseconds(180));
    }

    private void Window_Drop(object? sender, DragEventArgs e)
    {
        var zone = DropZoneAt(e);
        _dragSeen = Stopwatch.GetTimestamp();
        HideDropOverlay();
        if (!CanTakeDrop(e)) return;
        e.Handled = true;
        var paths = (e.DataTransfer.TryGetFiles() ?? []).Select(item => item.TryGetLocalPath()).OfType<string>().ToList();
        HandleDrop(paths, zone);
    }

    private static void SetClass(Control control, string name, bool on)
    {
        if (on && !control.Classes.Contains(name)) control.Classes.Add(name);
        else if (!on) control.Classes.Remove(name);
    }

    // Showing and hiding race with the fade: a hide only takes effect if nothing showed the overlay again since, whatever the speed of the animation.
    private int _dropOverlayVersion;

    internal void ShowDropOverlay(DropTarget zone)
    {
        _dropOverlayVersion++;
        if (!DropOverlay.IsVisible)
        {
            DropOverlay.IsVisible = true;
            Dispatcher.UIThread.Post(() => DropOverlay.Opacity = 1, DispatcherPriority.Render);
        }
        else DropOverlay.Opacity = 1;
        foreach (var (control, mine) in new[] { (DropLibraryZone, DropTarget.MasterLibrary), (DropCapturesZone, DropTarget.Captures) })
        {
            SetClass(control, "over", zone == mine);
            SetClass(control, "marching", zone == mine && !Motion.Reduced);
        }
    }

    internal void HideDropOverlay()
    {
        var version = ++_dropOverlayVersion;
        DropOverlay.Opacity = 0;
        foreach (var control in new[] { DropLibraryZone, DropCapturesZone }) { SetClass(control, "over", false); SetClass(control, "marching", false); }
        DispatcherTimer.RunOnce(() => { if (version == _dropOverlayVersion) DropOverlay.IsVisible = false; }, TimeSpan.FromMilliseconds(260));
    }

    /// <summary>Links what was dropped where it was dropped, says what happened, and leaves the person on the step they were on.</summary>
    internal DropPlan HandleDrop(IEnumerable<string> dropped, DropTarget target)
    {
        var english = _viewModel.UiLanguage == UiLocalization.English;
        var plan = DroppedItems.Plan(dropped, target);
        if (plan.Accepted == 0)
        {
            ShowToast(english ? "Nothing to add: drop folders or FITS and XISF files." : "Niente da aggiungere: trascina cartelle o file FITS e XISF.");
            return plan;
        }

        if (_viewModel.ShowOnboarding) _viewModel.CompleteOnboarding();
        int Count() => target == DropTarget.MasterLibrary ? _viewModel.MasterLibraries.Count : _viewModel.SourcePaths.Count;
        var before = Count();
        foreach (var folder in plan.Folders)
        {
            if (target == DropTarget.MasterLibrary) _viewModel.AddMasterLibrary(folder);
            else _viewModel.AddSource(folder);
        }
        foreach (var file in plan.Files) _viewModel.AddSource(file);

        var added = Count() - before;
        var where = target == DropTarget.MasterLibrary ? (english ? "the Master Library" : "alla Libreria Master") : (english ? "the captures" : "alle acquisizioni");
        // Gender-neutral on purpose: a folder and a file are both "elementi". One item is named, several are counted.
        var what = plan.Accepted == 1 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(plan.Paths.First())) : (english ? $"{plan.Accepted} items" : $"{plan.Accepted} elementi");
        var message = added > 0
            ? (english ? $"Added to {where}: {what}" : $"Aggiunto {where}: {what}")
            : (english ? "Already linked" : "Già collegato");
        if (plan.Ignored.Count > 0) message += english ? $" · {plan.Ignored.Count} {(plan.Ignored.Count == 1 ? "file" : "files")} ignored (not FITS/XISF)" : $" · {plan.Ignored.Count} file ignorat{(plan.Ignored.Count == 1 ? "o" : "i")} (non FITS/XISF)";
        ShowToast(message);
        if (_shell.CurrentStep == ShellViewModel.ImportStep && added > 0) PulseCard(target == DropTarget.MasterLibrary ? LibraryCard : SourcesCard);
        return plan;
    }

    // ---- The gear: every part Forge detected can be corrected ----

    private void EditProfile_Click(object? sender, RoutedEventArgs e) => OpenProfileEditor((sender as Control)?.Tag as string);

    private void OpenProfileEditor(string? part)
    {
        if (!_observatory.IsEditing) _observatory.BeginEdit();
        Dispatcher.UIThread.Post(() =>
        {
            Control? box = part switch { "camera" => CameraBox, "optics" => TelescopeBox, "reducer" => ReducerBox, "pixel" => PixelBox, _ => null };
            box?.BringIntoView();
            box?.Focus();
        }, DispatcherPriority.Background);
    }

    private void TrainView_PartInvoked(object? sender, TrainPartKind part)
    {
        switch (part)
        {
            case TrainPartKind.Camera: OpenProfileEditor("camera"); break;
            case TrainPartKind.Telescope: OpenProfileEditor("optics"); break;
            case TrainPartKind.Reducer: OpenProfileEditor("reducer"); break;
            default: PulseCard(WheelCard); break;
        }
    }

    private async void AddLibrary_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Aggiungi Master Library", AllowMultiple = true });
        foreach (var folder in folders) if (folder.TryGetLocalPath() is { } path) _viewModel.AddMasterLibrary(path);
    }

    private void RemoveLibrary_Click(object? sender, RoutedEventArgs e) => _viewModel.RemoveSelectedMasterLibrary();
    private void MoveLibraryUp_Click(object? sender, RoutedEventArgs e) => _viewModel.MoveSelectedMasterLibrary(-1);
    private void MoveLibraryDown_Click(object? sender, RoutedEventArgs e) => _viewModel.MoveSelectedMasterLibrary(1);
    private void RefreshLibraries_Click(object? sender, RoutedEventArgs e) => _viewModel.RefreshMasterLibraryStates();
    private void ClearAnalysisFilters_Click(object? sender, RoutedEventArgs e) { _viewModel.SearchText = ""; _viewModel.ShowIssuesOnly = false; }
    private void TreeMark_Click(object? sender, RoutedEventArgs e) => _viewModel.RefreshManualSelection();

    private void ToggleSettings_Click(object? sender, RoutedEventArgs e) => SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
    private void OpenOnboarding_Click(object? sender, RoutedEventArgs e)
    {
        SettingsPanel.IsVisible = false;
        _onboardingStep = 1;
        UpdateOnboarding();
        _viewModel.OpenOnboarding();
    }
    private void Tour_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!Tour.IsRunning) return;
        if (e.Key == Key.Escape) Tour.Stop();
        else if (e.Key is Key.Right or Key.Enter or Key.Space) Tour.Next();
        else if (e.Key == Key.Left) Tour.Previous();
        else return;
        e.Handled = true;
    }

    private void StartTour_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; StartTour(-1); }

    /// <summary>The tour: the steps, the one button, then each card of each step on the real control.</summary>
    private IReadOnlyList<TourStop> TourStops()
    {
        var current = _shell.CurrentStep;
        List<TourStop> stops =
        [
            new(current, () => Stepper, "Quattro passi",
                "Importa, Strumento, Calibrazioni, Esporta. Ogni passo ha la sua pagina e un segno di spunta quando non resta nulla da fare. Clic su un passo per tornarci."),
            new(current, () => NextButton, "Un solo pulsante",
                "In basso a destra, sempre nello stesso punto: fa la prossima cosa che serve al progetto. Sopra di lui una riga dice perché."),
            new(current, () => SettingsButton, "Menu e strumenti",
                "Menu: lingua, aggiornamenti, aiuto e progetto. Strumenti: statistiche, qualità dei frame, metadati e Libreria Master. Ctrl K cerca qualsiasi comando."),
            new(ShellViewModel.ImportStep, () => LibraryCard, "Libreria Master",
                "Collega una volta sola la cartella dei Dark e Bias. Resta salvata per ogni progetto; se non ce l’hai, salta."),
            new(ShellViewModel.ImportStep, () => SourcesCard, "Acquisizioni",
                "Cartelle o file FITS e XISF da qualsiasi software, anche trascinati ovunque sulla finestra. Forge legge solo gli header: gli originali restano intatti."),
        ];
        if (_shell.Analyzed)
        {
            stops.AddRange(
            [
                new(ShellViewModel.SetupStep, () => TrainCard, "Treno ottico",
                    "Telescopio, riduttore, ruota e camera come Forge li ha letti. Tocca una parte per dire cos’è davvero."),
                new(ShellViewModel.SetupStep, () => ProfileCard, "Camera e ottica",
                    "Camera, ottica, riduttore e pixel: ognuno si cambia con un tocco e vale per ogni progetto con questa camera."),
                new(ShellViewModel.SetupStep, () => WheelCard, "Filtri",
                    "Anche i filtri già riconosciuti si possono cambiare: scegli quello giusto dal catalogo e Forge archivia i frame sotto quello."),
                new(ShellViewModel.CalibrationStep, () => CalibrationCard, "Calibrazione",
                    "Quanti Light hanno Flat, Dark e Bias. Sotto, ogni filtro con le sue ore e la quota calibrata."),
                new(ShellViewModel.CalibrationStep, () => ResolveCard, "Da risolvere",
                    "Quando Forge non può scegliere da solo, ti mostra la scelta con il candidato consigliato, una alla volta."),
                new(ShellViewModel.ExportStep, () => StackCard, "Integrazione",
                    "Le ore integrate notte per notte, SNR e rumore rispetto alla prima notte. Spazio riproduce l’accumulo, HOO e SHO cambiano palette."),
                new(ShellViewModel.ExportStep, () => ExportCard, "Esporta",
                    "Nome, destinazione e un clic. Si copiano e verificano solo i file nuovi, con la cronologia del dataset."),
                new(ShellViewModel.ExportStep, () => WbppCard, "PixInsight WBPP",
                    "L’istanza WBPP con file, master e gruppi già compilati. WBPP parte solo quando premi Run."),
            ]);
        }
        stops.Add(new(current, () => MenuButton, "Comandi",
            "Ctrl K per tutti i comandi, F1 per il tour, il logo per il Menu. Gli strumenti (statistiche, qualità, Libreria Master) sono in alto a destra."));
        return stops;
    }

    /// <summary>Starts the tour: from the first stop on <paramref name="step"/>, or from the beginning when it is -1.</summary>
    private void StartTour(int step)
    {
        SettingsPanel.IsVisible = false;
        CloseSheet();
        ClosePalette();
        var stops = TourStops();
        var from = step <= 0 ? 0 : Math.Max(0, stops.Select((stop, index) => (stop, index)).Skip(2).FirstOrDefault(pair => pair.stop.Tab == step).index);
        Tour.Start(stops, from, index => { if (index >= 0 && index < ShellViewModel.StepCount) _shell.CurrentStep = index; });
    }

    /// <summary>Builds the Cygnus Loop demo in the app data folder, opens it as the current project and analyses it.</summary>
    private async Task LoadDemoAsync()
    {
        var root = AppDataPaths.Combine("Demo");
        // The demo folder is ours alone: rebuild it so a previous tour's edits never leak into the next one.
        if (Directory.Exists(root)) Directory.Delete(root, true);
        await Task.Run(() => DemoDatasetGenerator.GenerateAsync(root));
        _viewModel.NewProject();
        _viewModel.ProjectName = DemoDatasetGenerator.Target;
        _viewModel.DestinationPath = Path.Combine(root, "Export");
        foreach (var folder in new[] { "ASIAIR", "NINA", "Libreria Master" }) _viewModel.AddSource(Path.Combine(root, folder));
        await _viewModel.ScanAsync();
    }

    /// <summary>Opens the demo project and starts the tour.</summary>
    private async void OpenDemo_Click(object? sender, RoutedEventArgs e)
    {
        SettingsPanel.IsVisible = false;
        CloseSheet();
        if (_viewModel.ShowOnboarding) _viewModel.CompleteOnboarding();
        if (!await ConfirmProjectReplacementAsync(opening: true)) return;
        await RunAsync("AF-DEMO-001", async () =>
        {
            await LoadDemoAsync();
            StartTour(-1);
            // The series sky fills in while the tour walks the first steps.
            AnalyzeAllQuality_Click(null, new RoutedEventArgs());
        });
    }

    private void OpenGuide_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; OpenUrl(GuideUrl); }
    private void OpenRepository_Click(object? sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);
    private void ReportIssue_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; OpenUrl(IssueUrl); }
    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);

    private async Task CheckUpdatesAsync(bool requested)
    {
        try
        {
            UpdateStatus.Text = requested ? "Controllo in corso…" : "";
            var channel = Enum.TryParse<ReleaseChannel>(_viewModel.UpdateChannel, true, out var value) ? value : ReleaseChannel.Stable;
            var decision = await _updateService.CheckChannelAsync(ReleaseIdentity.Version, channel);
            _availableUpdate = decision.IsAvailable ? decision.Manifest.Installer : null;
            _availableUpdateSigned = decision.IsAvailable && decision.Manifest.Signed;
            UpdateStatus.Text = decision.Reason;
            UpdateButton.Content = decision.IsAvailable ? $"Installa {decision.Manifest.Version}" : "Controlla aggiornamenti";
            if (!requested && decision.IsAvailable)
            {
                SettingsPanel.IsVisible = true;
                UpdateButton.Focus();
            }
        }
        catch (Exception exception)
        {
            if (requested) UpdateStatus.Text = "Impossibile controllare gli aggiornamenti.";
            _viewModel.RecordError("AF-UPDATE-CHECK-001", exception);
        }
    }

    private async void InstallUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) { await CheckUpdatesAsync(true); return; }
        try
        {
            UpdateButton.IsEnabled = false;
            var destination = Path.Combine(Path.GetTempPath(), _availableUpdate.FileName);
            var progress = new Progress<double>(value =>
            {
                UpdateProgress.Value = value;
                UpdateStatus.Text = $"Download {value:0}%";
            });
            if (OperatingSystem.IsWindows())
            {
                await InstallWindowsUpdateAsync(_availableUpdate, progress);
                return;
            }
            await _updateService.DownloadVerifiedAsync(_availableUpdate, destination, progress);
            UpdateStatus.Text = OperatingSystem.IsMacOS()
                ? "Download completato. Apri il disco e sostituisci l’app in Applicazioni."
                : "Download completato. Segui l’installazione per sostituire la versione corrente.";
            Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            UpdateStatus.Text = "Aggiornamento non completato.";
            _viewModel.RecordError("AF-UPDATE-INSTALL-001", exception);
        }
        finally { UpdateButton.IsEnabled = true; }
    }
    /// <summary>
    /// Windows: verify the Inno Setup installer, run it with its native progress window and close
    /// the app so the files can be replaced. The installer relaunches AstroProjectForge.exe with
    /// <c>--updated</c> (see installer/AstroProjectForge.iss, IsAutomaticUpdate).
    /// </summary>
    private async Task InstallWindowsUpdateAsync(ReleaseArtifact artifact, IProgress<double> progress)
    {
        var updatesDirectory = AppDataPaths.Combine("Updates");
        Directory.CreateDirectory(updatesDirectory);
        var path = await _updateService.DownloadVerifiedAsync(
            artifact,
            Path.Combine(updatesDirectory, artifact.FileName),
            progress,
            requireAuthenticode: _availableUpdateSigned);
        _viewModel.SaveState();

        var startInfo = new ProcessStartInfo(path) { UseShellExecute = true };
        startInfo.ArgumentList.Add("/SP-");
        // /SILENT keeps the native installer progress window visible. The application
        // closes only after the installer has started, then Inno Setup relaunches it.
        startInfo.ArgumentList.Add("/SILENT");
        startInfo.ArgumentList.Add("/NORESTART");
        startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
        startInfo.ArgumentList.Add("/FORCECLOSEAPPLICATIONS");
        startInfo.ArgumentList.Add("/APFUPDATE=1");
        startInfo.ArgumentList.Add("/APFVISIBLE=1");
        startInfo.ArgumentList.Add($"/LOG={Path.Combine(updatesDirectory, "installer.log")}");
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("Impossibile avviare l'installer dell'aggiornamento.");

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        else Close();
    }

    private void OpenDiagnosticsTab_Click(object? sender, RoutedEventArgs e) => OpenSheet(LogSheet);
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void OnboardingEnglish_Click(object? sender, RoutedEventArgs e) { _viewModel.UiLanguage = UiLocalization.English; UpdateOnboarding(); }
    private void OnboardingItalian_Click(object? sender, RoutedEventArgs e) { _viewModel.UiLanguage = UiLocalization.Italian; UpdateOnboarding(); }
    private void OnboardingSkip_Click(object? sender, RoutedEventArgs e) => _viewModel.CompleteOnboarding();
    private void OnboardingBack_Click(object? sender, RoutedEventArgs e)
    {
        _onboardingStep = Math.Max(1, _onboardingStep - 1);
        UpdateOnboarding();
    }
    private void OnboardingNext_Click(object? sender, RoutedEventArgs e)
    {
        if (_onboardingStep == OnboardingSteps)
        {
            // The welcome ends where the work starts: on the import page, Master Library first.
            _viewModel.CompleteOnboarding();
            _shell.CurrentStep = ShellViewModel.ImportStep;
            return;
        }
        _onboardingStep++;
        UpdateOnboarding();
    }

    private const int OnboardingSteps = 2;

    private void UpdateOnboarding()
    {
        OnboardingStep1.IsVisible = _onboardingStep == 1;
        OnboardingStep2.IsVisible = _onboardingStep == 2;
        OnboardingProgress.Text = $"{_onboardingStep} / {OnboardingSteps}";
        OnboardingBackButton.IsVisible = _onboardingStep > 1;
        OnboardingNextButton.Content = _onboardingStep == OnboardingSteps ? "Inizia" : "Continua";
        ScheduleLocalization();
    }

    private async void Analyze_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-SCAN-001", () => _viewModel.ScanAsync());

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        var english = _viewModel.UiLanguage == UiLocalization.English;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = english ? "Open AstroProject Forge project" : "Apri progetto AstroProject Forge", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(english ? "AstroProject Forge project" : "Progetto AstroProject Forge") { Patterns = ["*.astroforge"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path && await ConfirmProjectReplacementAsync(opening: true)) await RunAsync("AF-PROJECT-OPEN-001", () => _viewModel.LoadProjectAsync(path));
    }

    private async void SaveProject_Click(object? sender, RoutedEventArgs e) => await SaveProjectAsync(false);
    private async void SaveProjectAs_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; await SaveProjectAsync(true); }
    private async void NewProject_Click(object? sender, RoutedEventArgs e) { SettingsPanel.IsVisible = false; await NewProjectCoreAsync(); }

    private async Task NewProjectCoreAsync()
    {
        if (await ConfirmProjectReplacementAsync(opening: false)) _viewModel.NewProject();
    }

    private async Task<bool> ConfirmProjectReplacementAsync(bool opening)
    {
        if (!_viewModel.HasProjectContent) return true;
        var english = _viewModel.UiLanguage == UiLocalization.English;
        var title = english ? (opening ? "Open project" : "New project") : (opening ? "Apri progetto" : "Nuovo progetto");
        var question = english ? (opening ? "Open the selected project?" : "Create a new project?") : (opening ? "Aprire il progetto selezionato?" : "Creare un nuovo progetto?");
        var detail = english
            ? "Unsaved project data will be discarded. Source files and Master Libraries will not be changed."
            : "I dati del progetto non salvati verranno persi. I file sorgente e le Master Library non verranno modificati.";
        var cancel = new Button { Content = english ? "Cancel" : "Annulla", MinWidth = 100 };
        var confirm = new Button { Content = english ? "Continue" : "Continua", MinWidth = 100, Classes = { "primary" } };
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        actions.Children.Add(cancel); actions.Children.Add(confirm);
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = question, FontSize = 21, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(actions);
        var dialog = new Window { Title = title, Width = 470, Height = 210, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task SaveProjectAsync(bool saveAs)
    {
        if (!saveAs && !string.IsNullOrWhiteSpace(_viewModel.CurrentProjectFile))
        {
            Try("AF-PROJECT-SAVE-001", () => _viewModel.SaveProject(_viewModel.CurrentProjectFile));
            return;
        }

        var english = _viewModel.UiLanguage == UiLocalization.English;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = english ? "Save AstroProject Forge project" : "Salva progetto AstroProject Forge", SuggestedFileName = string.IsNullOrWhiteSpace(_viewModel.ProjectName) ? (english ? "New project.astroforge" : "Nuovo progetto.astroforge") : _viewModel.ProjectName + ".astroforge",
            DefaultExtension = "astroforge", FileTypeChoices = [new FilePickerFileType(english ? "AstroProject Forge project" : "Progetto AstroProject Forge") { Patterns = ["*.astroforge"] }]
        });
        if (file?.TryGetLocalPath() is { } path) Try("AF-PROJECT-SAVE-001", () => _viewModel.SaveProject(path));
    }

    private async void ChooseDestination_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Destinazione progetto", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _viewModel.DestinationPath = path;
    }

    private void BuildPlan_Click(object? sender, RoutedEventArgs e) => Try("AF-PLAN-001", _viewModel.BuildPlan);
    private async void ExportPreflight_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-EXPORT-PREFLIGHT-001", () => _viewModel.RunExportPreflightAsync());
    private async void Export_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-EXPORT-001", () => _viewModel.ExportAsync());
    private void PauseExport_Click(object? sender, RoutedEventArgs e) => _viewModel.PauseExport();
    private void ResumeExport_Click(object? sender, RoutedEventArgs e) => _viewModel.ResumeExport();
    private void CancelExport_Click(object? sender, RoutedEventArgs e) => _viewModel.CancelExport();

    private async void ExportStatistics_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Esporta statistiche", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) Try("AF-STATS-001", () => _viewModel.ExportStatistics(path));
    }

    private async void AnalyzeQuality_Click(object? sender, RoutedEventArgs e)
    {
        StopBlink();
        _qualityCancellation?.Cancel();
        _qualityCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.AnalyzeQualityAsync(_qualityCancellation.Token);
            await RefreshQualityPreviewAsync(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record("AF-QUALITY-001", exception); }
        finally { _qualityCancellation?.Dispose(); _qualityCancellation = null; }
    }

    private async void AnalyzeAllQuality_Click(object? sender, RoutedEventArgs e)
    {
        StopBlink();
        _qualityCancellation?.Cancel();
        _qualityCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.AnalyzeAllQualityAsync(_qualityCancellation.Token);
            await RefreshQualityPreviewAsync(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record("AF-QUALITY-ALL-001", exception); }
        finally { _qualityCancellation?.Dispose(); _qualityCancellation = null; }
    }
    private void CancelQuality_Click(object? sender, RoutedEventArgs e) => _qualityCancellation?.Cancel();

    private void ExcludeQualitySuspects_Click(object? sender, RoutedEventArgs e) => _viewModel.ExcludeQualitySuspects();
    private void ExcludeSelectedQuality_Click(object? sender, RoutedEventArgs e) => _viewModel.ExcludeSelectedQualityFrames(QualityGrid.SelectedItems.Cast<QualityFrameRow>().ToArray());
    private void RestoreQuality_Click(object? sender, RoutedEventArgs e) => _viewModel.RestoreAllQualityFrames();

    private async void QualityGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var rows = QualityGrid.SelectedItems.Cast<QualityFrameRow>().ToArray();
        _viewModel.SetQualitySelection(rows);
        if (!_viewModel.IsQualityAnalyzing) await RefreshQualityPreviewAsync(true);
    }

    private async void QualitySeries_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        StopBlink();
        if (!_viewModel.IsQualityAnalyzing) await RefreshQualityPreviewAsync(true);
    }

    private void QualityThreshold_Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Value") QualityChart?.InvalidateVisual();
    }

    private async void QualityChart_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });
        QualityGrid.SelectedItem = _viewModel.SelectedQualityFrame;
        await RefreshQualityPreviewAsync(true);
    }

    private async void QualityPreviewOptions_Click(object? sender, RoutedEventArgs e) => await RefreshQualityPreviewAsync();
    private async void QualityStretch_Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Value" && IsLoaded) await RefreshQualityPreviewAsync();
    }

    private void Blink_Click(object? sender, RoutedEventArgs e)
    {
        if (_blinkTimer.IsEnabled) { StopBlink(); return; }
        _blinkIndex = -1;
        _blinkTimer.Start();
        BlinkButton.Content = "Ferma Blink";
        BlinkTimer_Tick(this, EventArgs.Empty);
    }
    private void ToggleQualityExclusion_Click(object? sender, RoutedEventArgs e) => _viewModel.ToggleSelectedQualityExclusion();
    private async void QualityFullResolution_Click(object? sender, RoutedEventArgs e)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        var cancellation = _previewCancellation = new CancellationTokenSource();
        try { await _viewModel.RenderQualityPreviewAsync(_viewModel.SelectedQualityFrame, cancellation.Token, true); SetZoom(1); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record("AF-QUALITY-FULLRES-001", exception); }
    }

    private async Task RefreshQualityPreviewAsync(bool fit = false)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        var cancellation = _previewCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.RenderQualityPreviewAsync(_viewModel.SelectedQualityFrame, cancellation.Token);
            if (fit) ZoomFit();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record("AF-QUALITY-PREVIEW-001", exception); }
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => SetZoom(_qualityZoom * 1.25);
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => SetZoom(_qualityZoom / 1.25);
    private void ZoomFit_Click(object? sender, RoutedEventArgs e) => ZoomFit();
    private void ZoomFit()
    {
        var bitmap = _viewModel.SelectedQualityFrame?.Preview;
        if (bitmap is null || QualityPreviewScroll.Bounds.Width <= 20 || QualityPreviewScroll.Bounds.Height <= 20) return;
        SetZoom(Math.Clamp(Math.Min((QualityPreviewScroll.Bounds.Width - 20) / bitmap.Size.Width, (QualityPreviewScroll.Bounds.Height - 20) / bitmap.Size.Height), .05, 16));
    }
    private void SetZoom(double value)
    {
        _qualityZoom = Math.Clamp(value, .05, 16);
        if (QualityPreviewImage.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = _qualityZoom;
            scale.ScaleY = _qualityZoom;
        }
    }

    private async void BlinkTimer_Tick(object? sender, EventArgs e)
    {
        var rows = QualityGrid.SelectedItems.Cast<QualityFrameRow>().Where(row => row.Preview is not null).ToArray();
        if (rows.Length < 2) rows = _viewModel.QualityFrames.Where(row => row.IsSuspect && row.Preview is not null).ToArray();
        if (rows.Length == 0) { StopBlink(); return; }
        _blinkIndex = (_blinkIndex + 1) % rows.Length;
        QualityGrid.SelectedItem = rows[_blinkIndex];
        _viewModel.SelectedQualityFrame = rows[_blinkIndex];
        await RefreshQualityPreviewAsync();
    }
    private void StopBlink() { _blinkTimer.Stop(); _blinkIndex = -1; if (BlinkButton is not null) BlinkButton.Content = "Avvia Blink"; }

    private void Tree_DoubleTapped(object? sender, TappedEventArgs e) => Reveal(_viewModel.SelectedNode?.Frames.FirstOrDefault()?.Path);
    private void QualityGrid_DoubleTapped(object? sender, TappedEventArgs e) => Reveal(_viewModel.SelectedQualityFrame?.Path);
    private void ReviewQueue_DoubleTapped(object? sender, TappedEventArgs e) => Reveal((sender as ListBox)?.SelectedItem is ReviewQueueItem item ? item.Frame.Path : null);
    private void MasterOrganizer_DoubleTapped(object? sender, TappedEventArgs e) => Reveal((sender as DataGrid)?.SelectedItem is MasterOrganizerItem item ? item.Frame.Path : null);

    /// <summary>Feeds the calibration map and the WBPP flow from the latest analysis.</summary>
    private void RefreshCalibrationVisuals()
    {
        var analysis = _viewModel.Analysis;
        var colours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        Color ColourOf(FrameMetadata frame)
        {
            var raw = frame.RawFilterName ?? frame.FilterName.Value ?? "";
            var camera = PhysicalFilterResolver.CameraKey(frame);
            if (colours.TryGetValue($"{camera}|{raw}", out var known)) return known;
            var identity = FilterRecognizer.Recognize(raw.Length == 0 ? null : raw, EquipmentRecognizer.Camera(frame.Camera.Value).IsColor, _viewModel.WheelProfileFor(camera));
            return colours[$"{camera}|{raw}"] = SpectrumColors.Glass(SpectrumColors.BandsOf(identity), identity.Kind);
        }
        CalibrationMapView.Rows = CalibrationRow.Build(analysis, ColourOf);
        PipelineFlowView.Keywords = _viewModel.WbppKeywords.Select(keyword => $"{keyword.Keyword} · PRE {keyword.Pre}").ToList();
        PipelineFlowView.Streams = analysis is null ? [] : analysis.Lights
            .GroupBy(item => item.Light.FilterName.Value ?? "—", StringComparer.OrdinalIgnoreCase)
            .Select(group => new PipelineStream(group.Key, ColourOf(group.First().Light), group.Count(), group.Sum(item => item.Light.ExposureSeconds.Value ?? 0)))
            .OrderByDescending(stream => stream.Colour.R - stream.Colour.B)
            .ToList();
    }

    private bool _darkCoverageQueued;

    private void ScheduleDarkCoverage()
    {
        if (_darkCoverageQueued) return;
        _darkCoverageQueued = true;
        Dispatcher.UIThread.Post(() => { _darkCoverageQueued = false; RefreshDarkCoverage(); });
    }

    /// <summary>Lays the Dark masters on the shelf (library scan and project candidates) against the Lights that need one, per gain setting.</summary>
    private void RefreshDarkCoverage()
    {
        var analysis = _viewModel.Analysis;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var shelf = _viewModel.MasterOrganizerItems.Select(item => item.Frame)
            .Concat(analysis?.Lights.SelectMany(item => item.Dark.Candidates.Concat(item.Bias.Candidates)).Select(candidate => candidate.Frame) ?? [])
            .Where(frame => frame.Kind is FrameKind.Dark or FrameKind.Bias)
            .DistinctBy(frame => frame.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        static (double? Gain, double? Offset) Setting(FrameMetadata frame) => (frame.Gain.Value, frame.Offset.Value);
        static double Slot(double? value, double step) => value is { } v ? Math.Round(v / step) * step : double.NaN;
        var lights = analysis?.Lights ?? [];
        var settings = shelf.Where(frame => frame.Kind == FrameKind.Dark).Select(Setting).Concat(lights.Select(item => Setting(item.Light))).Distinct()
            .OrderBy(setting => setting.Gain ?? double.MaxValue).ThenBy(setting => setting.Offset ?? double.MaxValue).ToList();
        var groups = new List<CoverageGroup>();
        foreach (var setting in settings)
        {
            var masters = shelf.Where(frame => frame.Kind == FrameKind.Dark && Setting(frame) == setting && frame.ExposureSeconds.Value is not null && frame.EffectiveTemperatureC is not null)
                .GroupBy(frame => (Exposure: Slot(frame.ExposureSeconds.Value, 0.001), Temperature: Slot(frame.EffectiveTemperatureC, 1)))
                .ToDictionary(group => group.Key, group => group.Count());
            var demand = lights.Where(item => Setting(item.Light) == setting && item.Light.ExposureSeconds.Value is not null && item.Light.EffectiveTemperatureC is not null)
                .GroupBy(item => (Exposure: Slot(item.Light.ExposureSeconds.Value, 0.001), Temperature: Slot(item.Light.EffectiveTemperatureC, 1)))
                .ToDictionary(group => group.Key, group => (Count: group.Count(), Covered: group.All(item => item.Dark.IsAccepted)));
            var cells = masters.Keys.Union(demand.Keys)
                .Select(key => new CoverageCell(key.Exposure, key.Temperature, masters.GetValueOrDefault(key), demand.TryGetValue(key, out var need) ? need.Count : 0, need.Covered))
                .ToList();
            if (cells.Count == 0) continue;
            var title = setting.Gain is { } gain ? string.Format(culture, CanvasText.T("Gain {0}"), gain.ToString("0.##", culture)) : CanvasText.T("Gain sconosciuto");
            if (setting.Offset is { } offset) title += " · " + string.Format(culture, CanvasText.T("Offset {0}"), offset.ToString("0.##", culture));
            var bias = shelf.Count(frame => frame.Kind == FrameKind.Bias && Setting(frame) == setting);
            groups.Add(new(title, cells, bias));
        }
        DarkCoverageView.Groups = groups;
        var needed = groups.Sum(group => group.Cells.Count(cell => cell.Lights > 0));
        var covered = groups.Sum(group => group.Cells.Count(cell => cell.Lights > 0 && cell.Covered));
        var darkMasters = groups.Sum(group => group.Cells.Sum(cell => cell.Masters));
        DarkCoverageSummary.Text = needed > 0
            ? string.Format(culture, CanvasText.T("{0}/{1} combinazioni coperte"), covered, needed)
            : darkMasters > 0 ? string.Format(culture, CanvasText.T("{0} Dark master"), darkMasters) : "";
    }

    private bool _exportVisualsQueued;

    private void ScheduleExportVisuals()
    {
        if (_exportVisualsQueued) return;
        _exportVisualsQueued = true;
        Dispatcher.UIThread.Post(() => { _exportVisualsQueued = false; RefreshExportVisuals(); });
    }

    /// <summary>A calibrated project with a name and a destination gets its real plan straight away, so Export shows the true folders.</summary>
    private void PreparePlanPreview()
    {
        if (_viewModel.Analysis?.Ready != true || string.IsNullOrWhiteSpace(_viewModel.ProjectName) || string.IsNullOrWhiteSpace(_viewModel.DestinationPath) || _viewModel.HasExportPlan) return;
        try { _viewModel.BuildPlan(); }
        catch (Exception) { /* The Export tab still shows the expected layout. */ }
    }

    private void RefreshExportVisuals()
    {
        var name = string.IsNullOrWhiteSpace(_viewModel.ProjectName) ? CanvasText.T("Progetto") : _viewModel.ProjectName;
        var colours = new Dictionary<string, Color?>(StringComparer.OrdinalIgnoreCase);
        Color? FilterColour(IReadOnlyList<FrameMetadata> frames)
        {
            var filters = frames.Select(frame => frame.FilterName.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (frames.Count == 0 || filters.Count != 1 || filters[0] is null) return null;
            if (colours.TryGetValue(filters[0]!, out var known)) return known;
            var frame = frames[0];
            var raw = frame.RawFilterName ?? frame.FilterName.Value ?? "";
            var camera = PhysicalFilterResolver.CameraKey(frame);
            var identity = FilterRecognizer.Recognize(raw, EquipmentRecognizer.Camera(frame.Camera.Value).IsColor, _viewModel.WheelProfileFor(camera));
            return colours[filters[0]!] = SpectrumColors.Glass(SpectrumColors.BandsOf(identity), identity.Kind);
        }

        var fresh = _viewModel.Novelty?.NewPaths;
        int NewIn(IEnumerable<FrameMetadata> frames) => fresh is null ? 0 : frames.Count(frame => fresh.Contains(frame.Path));
        ExportNode FromPlan(ProjectTreeNode node, int depth) => new(node.Name, node.Count, depth >= 1 && node.Name.StartsWith("FILTER_", StringComparison.Ordinal) || depth >= 2 ? FilterColour(node.Frames) : null,
            depth >= 3 ? [] : node.Children.Where(child => !child.IsLeaf || child.Icon != "·").Select(child => FromPlan(child, depth + 1)).ToList(), NewIn(node.Frames));

        if (_viewModel.PlannedTreeRoots.Count > 0)
        {
            ExportMapView.IsPreview = false;
            ExportMapView.Root = new ExportNode(name, _viewModel.PlannedTreeRoots.Sum(node => node.Count), null, _viewModel.PlannedTreeRoots.Select(node => FromPlan(node, 1)).ToList(),
                _viewModel.PlannedTreeRoots.Sum(node => NewIn(node.Frames)));
        }
        else if (_viewModel.Analysis is { Lights.Count: > 0 } analysis)
        {
            // Before a plan exists, show the layout the export will follow: filters, then nights.
            ExportNode Branch(string role, IEnumerable<FrameMetadata> frames) => new(role, frames.Count(), null, frames
                .GroupBy(frame => frame.FilterName.Value ?? "—", StringComparer.OrdinalIgnoreCase)
                .Select(filter => new ExportNode($"FILTER_{filter.Key}", filter.Count(), FilterColour(filter.ToList()), filter
                    .GroupBy(frame => frame.SessionId.Value ?? "—").OrderBy(night => night.Key, StringComparer.Ordinal)
                    .Select(night => new ExportNode($"NIGHT_{night.Key}", night.Count(), null, [], NewIn(night))).ToList(), NewIn(filter)))
                .ToList(), NewIn(frames));
            var lights = analysis.Lights.Select(item => item.Light).ToList();
            var flats = analysis.Lights.SelectMany(item => item.FlatGroup?.Frames ?? []).Distinct().ToList();
            var roles = new List<ExportNode> { Branch("Light", lights) };
            if (flats.Count > 0) roles.Add(Branch("Flat", flats));
            var darks = analysis.Lights.Select(item => item.Dark.Selected?.Frame).OfType<FrameMetadata>().Distinct().Count();
            var biases = analysis.Lights.Select(item => item.Bias.Selected?.Frame).OfType<FrameMetadata>().Distinct().Count();
            if (darks > 0) roles.Add(new ExportNode("Dark", darks, null, []));
            if (biases > 0) roles.Add(new ExportNode("Bias", biases, null, []));
            ExportMapView.IsPreview = true;
            ExportMapView.Root = new ExportNode(name, roles.Sum(role => role.Files), null, roles, roles.Sum(role => role.New));
        }
        else ExportMapView.Root = null;
        RefreshExportProgress();
    }

    private void RefreshExportProgress()
    {
        ExportMapView.Progress = _viewModel.ExportProgress / 100;
        ExportMapView.IsRunning = _viewModel.ExportState == ExportRunState.Running;
        ExportPercentText.Text = _viewModel.ExportState is ExportRunState.Running or ExportRunState.Paused or ExportRunState.Completed or ExportRunState.Cancelled
            ? $"{_viewModel.ExportProgress:0}%" : "";
        // The finished export hands over to PixInsight: the WBPP card comes into view once.
        var finished = _viewModel.ExportState == ExportRunState.Completed;
        if (finished && !_exportCelebrated) Dispatcher.UIThread.Post(() => PulseCard(WbppCard));
        _exportCelebrated = finished;
    }

    private void CalibrationMap_CellActivated(object? sender, (CalibrationRow Row, CalibrationCell Cell) activated)
    {
        var lights = activated.Row.Lights.ToHashSet();
        var item = _viewModel.ReviewQueue.FirstOrDefault(entry => entry.Calibration == activated.Cell.Calibration && entry.Targets.Any(lights.Contains));
        if (item is null) return;
        ReviewList.SelectedItem = item;
        ReviewList.ScrollIntoView(item);
    }

    private void ReviewQueue_SelectionChanged(object? sender, SelectionChangedEventArgs e) => _viewModel.SelectReviewItem((sender as ListBox)?.SelectedItem as ReviewQueueItem);
    private void EditReviewMetadata_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.SelectReviewItem((sender as Control)?.DataContext as ReviewQueueItem);
        OpenSheet(MetadataSheet);
    }
    private void AssignLight_Click(object? sender, RoutedEventArgs e) => _viewModel.AssignReviewCandidate((sender as Control)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Light);
    private void AssignNight_Click(object? sender, RoutedEventArgs e) => _viewModel.AssignReviewCandidate((sender as Control)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Night);
    private void AssignSession_Click(object? sender, RoutedEventArgs e) => _viewModel.AssignReviewCandidate((sender as Control)?.DataContext as ReviewQueueItem, ReviewAssignmentScope.Configuration);

    private async void ScanMasterLibraries_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-MASTER-SCAN-001", () => _viewModel.ScanMasterLibrariesAsync());
    private async void PreviewMasterOrganizer_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-MASTER-PREFLIGHT-001", () => _viewModel.PreviewMasterOrganizerAsync());
    private async void OrganizeMasterLibrary_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-MASTER-ORGANIZE-001", () => _viewModel.OrganizeMasterLibraryAsync());
    private async void RollbackMasterOrganizer_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-MASTER-ROLLBACK-001", () => _viewModel.RollbackMasterOrganizerAsync());
    private async void ChooseMasterOrganizerDestination_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Destinazione nuova Master Library", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _viewModel.MasterOrganizerDestination = path;
    }

    private void ClearCache_Click(object? sender, RoutedEventArgs e) => _viewModel.ClearHeaderCache();
    private void OpenWbppInstance_Click(object? sender, RoutedEventArgs e) => Try("AF-WBPP-001", () =>
        AstroForge.Core.Wbpp.PixInsightLauncher.Open(_viewModel.GenerateWbppInstance()));
    private void RefreshDiagnostics_Click(object? sender, RoutedEventArgs e) => _viewModel.RefreshDiagnostics();
    private async void RestoreRecovery_Click(object? sender, RoutedEventArgs e) => await RunAsync("AF-RECOVERY-001", () => _viewModel.RestoreRecoveryAsync());
    private void DiscardRecovery_Click(object? sender, RoutedEventArgs e) => _viewModel.DiscardRecovery();
    private async void ExportSupport_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Esporta diagnostica", SuggestedFileName = $"AstroProjectForge-Support-{DateTime.Now:yyyyMMdd-HHmmss}.zip", DefaultExtension = "zip" });
        if (file?.TryGetLocalPath() is { } path) await RunAsync("AF-SUPPORT-001", () => _viewModel.ExportSupportBundleAsync(path));
    }

    private static void Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var info = new ProcessStartInfo { UseShellExecute = true };
        if (OperatingSystem.IsWindows()) { info.FileName = "explorer.exe"; info.ArgumentList.Add($"/select,{path}"); }
        else if (OperatingSystem.IsMacOS()) { info.FileName = "open"; info.ArgumentList.Add("-R"); info.ArgumentList.Add(path); }
        else { info.FileName = "xdg-open"; info.ArgumentList.Add(Path.GetDirectoryName(path)!); }
        Process.Start(info);
    }

    private async Task RunAsync(string code, Func<Task> operation)
    {
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record(code, exception); }
    }
    private async Task RunAsync(string code, Func<Task<string>> operation)
    {
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Record(code, exception); }
    }
    private void Try(string code, Action operation) { try { operation(); } catch (Exception exception) { Record(code, exception); } }
    private void Try(string code, Func<string> operation) { try { _ = operation(); } catch (Exception exception) { Record(code, exception); } }
    private void Record(string code, Exception exception) => _viewModel.RecordError(code, exception);
}
