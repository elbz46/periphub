using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PeriphHub.Core;
using PeriphHub.Core.Deck;
using PeriphHub.Core.Scenes;

namespace PeriphHub.UI;

/// <summary>Create / edit a scene: name, icon, when it runs, and its list of steps.</summary>
public sealed class SceneEditor : Window
{
    private readonly SceneModel _scene;
    private readonly TextBox _name = new(), _icon = new() { Width = 80 }, _app = new(), _arg = new(), _delay = new() { Width = 70, Text = "100" };
    private readonly ComboBox _trigger = new(), _exit = new(), _action = new(), _argPick = new() { MinWidth = 220 };
    private readonly CheckBox _enabled = new() { Content = "On (runs automatically)" };
    private readonly ObservableCollection<DeckStep> _steps = new();
    private readonly ListBox _list = new() { Height = 180 };
    private readonly StackPanel _appPanel = new(), _argPanel = new();
    private readonly TextBlock _argHint = new() { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

    private static IReadOnlyList<ActionDef> Actions =>
        ActionCatalog.Common.Where(a => a.Id is not ("default" or "disabled") && !a.Id.StartsWith("mouse:left") && !a.Id.StartsWith("device:")).ToList();

    public SceneEditor(SceneModel? scene)
    {
        _scene = scene ?? new SceneModel();
        Title = scene == null ? "New scene" : $"Scene: {scene.Name}";
        Width = 660; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new StackPanel { Margin = new Thickness(20) };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Field("Name", _name));
        var iconField = Field("Icon (emoji)", _icon);
        ((FrameworkElement)iconField).Margin = new Thickness(12, 0, 0, 10);
        Grid.SetColumn(iconField, 1);
        top.Children.Add(iconField);
        root.Children.Add(top);

        _trigger.ItemsSource = SceneModel.Triggers.Select(t => t.label).ToList();
        root.Children.Add(Field("Runs", _trigger));
        var appRow = new DockPanel();
        var pick = new Button { Content = "Pick a running app…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        pick.Click += (_, _) => { var exe = AppPicker.Pick(this); if (!string.IsNullOrWhiteSpace(exe)) _app.Text = exe; };
        DockPanel.SetDock(pick, Dock.Right);
        appRow.Children.Add(pick);
        appRow.Children.Add(_app);
        _appPanel.Children.Add(Field("Program (e.g. valorant.exe, cs2.exe, spotify.exe)", appRow));
        _exit.ItemsSource = new[] { "(nothing)" }.Concat(SceneStore.Scenes.Where(s => s != scene).Select(s => s.Name)).ToList();
        _appPanel.Children.Add(Field("When it closes, run scene", _exit));
        root.Children.Add(_appPanel);
        root.Children.Add(_enabled);

        root.Children.Add(new TextBlock { Text = "Steps (run in order)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 4) });
        _list.ItemsSource = _steps;
        _list.DisplayMemberPath = null;
        _list.ItemTemplate = StepTemplate();
        root.Children.Add(_list);
        var listBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 10) };
        listBtns.Children.Add(Btn("Remove", () => { if (_list.SelectedItem is DeckStep s) _steps.Remove(s); }));
        listBtns.Children.Add(Btn("Move up", () => Move(-1)));
        listBtns.Children.Add(Btn("Move down", () => Move(1)));
        listBtns.Children.Add(Btn("Run this step now", () => { if (_list.SelectedItem is DeckStep s) { ActionRunner.Run(s.ToSpec(), true); ActionRunner.Run(s.ToSpec(), false); } }));
        root.Children.Add(listBtns);

        var add = new Border { BorderThickness = new Thickness(1), BorderBrush = System.Windows.Media.Brushes.Gray, CornerRadius = new CornerRadius(8), Padding = new Thickness(12) };
        var addPanel = new StackPanel();
        addPanel.Children.Add(new TextBlock { Text = "Add a step", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        _action.ItemsSource = Actions;
        _action.ItemTemplate = new DataTemplate { VisualTree = LabelFactory() };
        addPanel.Children.Add(Field("Action", _action));
        _argPanel.Children.Add(Field("Details", _arg));
        _argPanel.Children.Add(Field("Suggestions", _argPick));
        _argPanel.Children.Add(_argHint);
        addPanel.Children.Add(_argPanel);
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        addRow.Children.Add(new TextBlock { Text = "then wait", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        addRow.Children.Add(_delay);
        addRow.Children.Add(new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) });
        addRow.Children.Add(Btn("Add step", AddStep));
        addPanel.Children.Add(addRow);
        add.Child = addPanel;
        root.Children.Add(add);

        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        var test = Btn("Try it now", () => _ = SceneEngine.Instance.RunAsync(Build()));
        DockPanel.SetDock(test, Dock.Left);
        footer.Children.Add(test);
        var cancel = Btn("Cancel", () => DialogResult = false);
        var save = Btn("Save", Save);
        save.IsDefault = true;
        if (TryFindResource("AccentButtonStyle") is Style accent) save.Style = accent;
        DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(save, Dock.Right);
        footer.Children.Add(cancel); footer.Children.Add(save);
        root.Children.Add(footer);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 820 };

        // load
        _name.Text = _scene.Name; _icon.Text = _scene.Icon; _app.Text = _scene.TriggerArg ?? "";
        _trigger.SelectedIndex = Math.Max(0, Array.FindIndex(SceneModel.Triggers, t => t.id == _scene.Trigger));
        _exit.SelectedItem = string.IsNullOrEmpty(_scene.OnExitScene) ? "(nothing)" : _scene.OnExitScene;
        if (_exit.SelectedItem == null) _exit.SelectedIndex = 0;
        _enabled.IsChecked = _scene.Enabled;
        foreach (var s in _scene.Steps) _steps.Add(new DeckStep { Id = s.Id, Arg = s.Arg, DelayMs = s.DelayMs });
        _trigger.SelectionChanged += (_, _) => Refresh();
        _action.SelectionChanged += (_, _) => Refresh();
        _argPick.SelectionChanged += (_, _) => { if (_argPick.SelectedItem is string v) _arg.Text = v; };
        _action.SelectedIndex = 0;
        Refresh();
    }

    private static FrameworkElementFactory LabelFactory()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Label"));
        return f;
    }

    private static DataTemplate StepTemplate()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new StepText() });
        return new DataTemplate { VisualTree = f };
    }

    private sealed class StepText : System.Windows.Data.IValueConverter
    {
        public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
        {
            if (v is not DeckStep s) return "";
            var name = ActionCatalog.Find(s.Id)?.Label.TrimEnd('…') ?? s.Id;
            return $"{name}{(string.IsNullOrEmpty(s.Arg) ? "" : $":  {s.Arg}")}{(s.DelayMs > 100 ? $"   (then wait {s.DelayMs} ms)" : "")}";
        }
        public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }

    private static FrameworkElement Field(string label, UIElement control)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4), Opacity = 0.85 });
        p.Children.Add(control);
        return p;
    }

    private static Button Btn(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private string TriggerId => SceneModel.Triggers[Math.Max(0, _trigger.SelectedIndex)].id;

    private void Refresh()
    {
        _appPanel.Visibility = TriggerId == "app" ? Visibility.Visible : Visibility.Collapsed;
        _enabled.Visibility = TriggerId == "manual" ? Visibility.Collapsed : Visibility.Visible;
        var act = _action.SelectedItem as ActionDef;
        _argPanel.Visibility = act?.NeedsArg == true ? Visibility.Visible : Visibility.Collapsed;
        _argHint.Text = act?.ArgHint ?? "";
        var sugg = act == null ? new List<string>() : ArgSuggestions.For(act.Id);
        _argPick.ItemsSource = sugg;
        ((FrameworkElement)_argPick.Parent).Visibility = sugg.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _arg.AcceptsReturn = act?.Id is "deck:paste" or "text";
    }

    private void AddStep()
    {
        if (_action.SelectedItem is not ActionDef act) return;
        if (act.NeedsArg && string.IsNullOrWhiteSpace(_arg.Text)) { MessageBox.Show(this, "Fill in the details for this step.", "Scene"); return; }
        int.TryParse(_delay.Text, out var ms);
        _steps.Add(new DeckStep { Id = act.Id, Arg = act.NeedsArg ? _arg.Text.Trim() : null, DelayMs = Math.Clamp(ms, 0, 60000) });
        _arg.Text = "";
    }

    private void Move(int dir)
    {
        if (_list.SelectedItem is not DeckStep s) return;
        var i = _steps.IndexOf(s);
        var j = i + dir;
        if (j < 0 || j >= _steps.Count) return;
        _steps.Move(i, j);
        _list.SelectedItem = s;
    }

    private SceneModel Build() => new()
    {
        Id = _scene.Id,
        Name = _name.Text.Trim().Length == 0 ? "Scene" : _name.Text.Trim(),
        Icon = _icon.Text.Trim(),
        Trigger = TriggerId,
        TriggerArg = TriggerId == "app" ? _app.Text.Trim().ToLowerInvariant() : null,
        OnExitScene = TriggerId == "app" && _exit.SelectedItem is string x && x != "(nothing)" ? x : null,
        Enabled = TriggerId == "manual" || _enabled.IsChecked == true,
        Steps = _steps.ToList(),
    };

    public SceneModel? Result { get; private set; }

    private void Save()
    {
        var r = Build();
        if (r.Trigger == "app" && string.IsNullOrWhiteSpace(r.TriggerArg)) { MessageBox.Show(this, "Type the program name, e.g. valorant.exe", "Scene"); return; }
        if (!r.TriggerArg?.EndsWith(".exe") == true && r.TriggerArg != null) r.TriggerArg += ".exe";
        Result = r;
        DialogResult = true;
    }
}
