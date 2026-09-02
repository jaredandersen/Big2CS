using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Big2.Core;

namespace Big2.App;

internal static class DialogSupport
{
    /// <summary>The game's icon, loaded once. Null if the resource is missing.</summary>
    public static BitmapSource? AppIcon { get; } = LoadIcon(16);

    /// <summary>The 128 frame, for the About box. Scaled DOWN to 64, never up.</summary>
    public static BitmapSource? AppIconLarge { get; } = LoadIcon(128);

    /// <summary>
    /// Picks one frame out of the .ico by pixel size.
    ///
    /// A plain BitmapImage over a multi-size .ico chooses a frame for you, and
    /// it chose the 16x16 -- which the About box then scaled 4x, so the whole
    /// point of hand-placing the 32x32's pixels was thrown away and the chunky
    /// 16 was shown instead. Ask for the size you actually want.
    /// </summary>
    private static BitmapSource? LoadIcon(int pixels)
    {
        try
        {
            var decoder = BitmapDecoder.Create(
                new Uri("pack://application:,,,/Assets/big2.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            var frame = decoder.Frames.FirstOrDefault(f => f.PixelWidth == pixels)
                        ?? decoder.Frames.OrderBy(f => f.PixelWidth).FirstOrDefault();
            frame?.Freeze();
            return frame;
        }
        catch
        {
            return null;   // an icon is not worth failing a dialog over
        }
    }

    /// <summary>
    /// Common chrome for every dialog here.
    ///
    /// WindowStartupLocation is set DIRECTLY on each window and never through a
    /// Style. It is a plain CLR property with no DependencyProperty behind it,
    /// so a Setter targeting it throws ArgumentNullException while App.xaml is
    /// being parsed -- which takes the whole application down at startup, before
    /// any window exists. The build succeeds and every launch dies.
    /// </summary>
    public static void Prepare(Window w, Window? owner, string title)
    {
        w.Title = title;
        w.ResizeMode = ResizeMode.NoResize;
        w.ShowInTaskbar = false;
        w.SizeToContent = SizeToContent.WidthAndHeight;
        w.Background = Brushes.White;
        w.Icon = AppIcon;
        w.WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;

        // Owner can only be set on a window that HAS BEEN SHOWN, and setting it
        // on one that has not throws. The render-dump harness never shows its
        // window, so this must tolerate that rather than assume an owner.
        if (owner is { IsVisible: true }) w.Owner = owner;
    }

    public static Button OkButton(string text = "OK")
    {
        var b = new Button
        {
            Content = text,
            Width = 88,
            Height = 26,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        return b;
    }

    /// <summary>The full-bleed footer strip the house About format uses.</summary>
    public static Border Footer(UIElement content) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
        BorderThickness = new Thickness(0, 1, 0, 0),
        Margin = new Thickness(-24, 22, -24, 0),
        Padding = new Thickness(24, 14, 24, 14),
        Child = content,
    };
}

/// <summary>
/// A yes/no prompt.
///
/// NOT MessageBox. A message box is centred on the SCREEN, not on its owner, and
/// passing an owner does not change that -- measured once at (1930,1090) while
/// the window's centre was (632,496). On a wide desktop that puts the question
/// most of a metre from where the player is looking.
/// </summary>
public sealed class MessagePrompt : Window
{
    private bool _result;

    private MessagePrompt(Window? owner, string title, string message, string yes, string no)
    {
        DialogSupport.Prepare(this, owner, title);

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Width = 320,               // explicit: SizeToContent plus MaxWidth clips instead of wrapping
            Margin = new Thickness(0, 0, 0, 4),
        };

        var yesButton = new Button { Content = yes, Width = 88, Height = 26, IsDefault = true };
        var noButton = new Button { Content = no, Width = 88, Height = 26, IsCancel = true,
                                    Margin = new Thickness(8, 0, 0, 0) };

        yesButton.Click += (_, _) => { _result = true; Close(); };
        noButton.Click += (_, _) => { _result = false; Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(yesButton);
        buttons.Children.Add(noButton);

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 0) };
        root.Children.Add(text);
        root.Children.Add(DialogSupport.Footer(buttons));
        Content = root;
    }

    /// <summary>Asks, and returns true for the affirmative. Falls back to true when there is no UI.</summary>
    public static bool Ask(Window? owner, string title, string message,
                           string yes = "Yes", string no = "No")
    {
        if (owner is { IsVisible: false }) return true;   // headless: nothing to ask

        var dlg = new MessagePrompt(owner, title, message, yes, no);
        dlg.ShowDialog();
        return dlg._result;
    }
}

/// <summary>
/// Player names and the optional target score. Everything here is also editable
/// in big2.ini by hand, which is most of the point of using a text format.
/// </summary>
public sealed class OptionsDialog : Window
{
    private readonly TextBox[] _names = new TextBox[Dealer.Seats];
    private readonly TextBox _target = new() { Width = 60 };
    private readonly ComboBox _speed = new() { Width = 110 };
    private readonly ComboBox _difficulty = new() { Width = 110 };

    public OptionsDialog(Window? owner, Settings settings)
    {
        DialogSupport.Prepare(this, owner, "Options");

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string[] labels = { "You", "Opponent on your right", "Opponent across", "Opponent on your left" };
        for (int i = 0; i < Dealer.Seats; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            AddRow(grid, i, labels[i],
                   _names[i] = new TextBox { Text = settings.SeatNames[i], Width = 180, MaxLength = 16 });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        grid.RowDefinitions.Add(new RowDefinition());
        _target.Text = settings.TargetScore.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AddRow(grid, Dealer.Seats + 1, "End the series at", _target);

        grid.RowDefinitions.Add(new RowDefinition());
        foreach (var v in Enum.GetValues<AnimationSpeed>()) _speed.Items.Add(v);
        _speed.SelectedItem = settings.AnimationSpeed;
        AddRow(grid, Dealer.Seats + 2, "Card movement", _speed);

        grid.RowDefinitions.Add(new RowDefinition());
        foreach (var v in Enum.GetValues<Difficulty>()) _difficulty.Items.Add(v);
        _difficulty.SelectedItem = settings.Difficulty;
        AddRow(grid, Dealer.Seats + 3, "Opponents", _difficulty);

        var note = new TextBlock
        {
            Text = "0 means the series never ends on its own. Lowest total wins."
                 + "\n\nEasy opponents look at only their two cheapest plays and never "
                 + "notice someone about to go out. Hard weighs every legal play and "
                 + "starts defending from five cards out.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            TextWrapping = TextWrapping.Wrap,
            Width = 320,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var ok = new Button { Content = "OK", Width = 88, Height = 26, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 88, Height = 26, IsCancel = true,
                                  Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => { Accepted = true; Close(); };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal,
                                       HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 0) };
        root.Children.Add(grid);
        root.Children.Add(note);
        root.Children.Add(DialogSupport.Footer(buttons));
        Content = root;
    }

    public bool Accepted { get; private set; }

    /// <summary>Writes the chosen values back. Only call when <see cref="Accepted"/>.</summary>
    public void ApplyTo(Settings settings)
    {
        var names = new string[Dealer.Seats];
        for (int i = 0; i < Dealer.Seats; i++)
        {
            string t = _names[i].Text.Trim();
            names[i] = t.Length > 0 ? t : settings.SeatNames[i];
        }
        settings.SeatNames = names;

        settings.TargetScore =
            int.TryParse(_target.Text.Trim(), System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture, out int n) && n > 0
                ? n : 0;

        if (_speed.SelectedItem is AnimationSpeed s) settings.AnimationSpeed = s;
        if (_difficulty.SelectedItem is Difficulty d) settings.Difficulty = d;
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement field)
    {
        var t = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 12, 3),
        };
        Grid.SetRow(t, row);
        Grid.SetColumn(t, 0);
        grid.Children.Add(t);

        field.Margin = new Thickness(0, 3, 0, 3);
        field.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }
}

/// <summary>
/// The About box, in the classic Windows format: a credits panel, not a splash
/// screen.
///
/// It is a real Window rather than a MessageBox because a MessageBox cannot
/// carry a custom icon, and because of the centring problem above.
/// </summary>
public sealed class AboutDialog : Window
{
    public AboutDialog(Window? owner)
    {
        DialogSupport.Prepare(this, owner, "About Big 2");

        var grid = new Grid { Margin = new Thickness(24, 22, 24, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The icon is antialiased artwork, not pixel art, so it gets WPF's normal
        // filtering -- NearestNeighbor here would alias the curves. It also takes
        // the 128 frame and scales DOWN to 64, so a 150% display still has pixels
        // to spare rather than magnifying a 64.
        var icon = new Image
        {
            Source = DialogSupport.AppIconLarge,
            Width = 64,
            Height = 64,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 20, 0),
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var right = new StackPanel { Width = 340 };

        right.Children.Add(new TextBlock
        {
            Text = "Big 2",
            FontSize = 26,
            FontWeight = FontWeights.Light,
        });

        right.Children.Add(new TextBlock
        {
            Text = "鋤大弟 — shed your hand, and the two of spades beats everything.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20),
        });

        // Authorship leads. Big 2 is a traditional game with no author and this
        // is not a port of anything, so the first credit is for THIS game rather
        // than for an original. Code and deck are one credit because they are one
        // author -- splitting them implied a provenance that no longer exists.
        AddCredit(right, "Game & Card Artwork", "Jared Andersen, 2026");

        // The licence carries the only link in the panel. It is the one thing
        // someone handed a build cannot find out any other way, and Apache 2.0
        // section 4(d) is why it is here rather than only in the repo: a
        // derivative has to reproduce the NOTICE wherever it shows notices of
        // this kind, so this dialog is the place that obligation points at.
        AddCreditWithLink(right, "Licence",
                          "Apache 2.0 for the code, CC BY 4.0 for the card artwork. " +
                          "Free to use, modify and redistribute, including commercially, " +
                          "with credit to the author.",
                          "apache.org/licenses/LICENSE-2.0",
                          "https://www.apache.org/licenses/LICENSE-2.0");

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var ok = DialogSupport.OkButton();
        var footer = DialogSupport.Footer(ok);
        Grid.SetColumnSpan(footer, 2);
        Grid.SetRow(footer, 1);

        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(footer);

        Content = grid;
    }

    /// <summary>
    /// A credit pair with a link under it. The link shells out with
    /// UseShellExecute, which is what actually opens a browser -- Process.Start
    /// on a bare URL does nothing without it on .NET Core.
    /// </summary>
    private static void AddCreditWithLink(Panel into, string label, string value,
                                          string linkText, string url)
    {
        into.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        into.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            TextWrapping = TextWrapping.Wrap,
        });

        var link = new Hyperlink(new Run(linkText)) { NavigateUri = new Uri(url) };
        link.RequestNavigate += (_, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // No browser, or the shell refused. Not worth failing the dialog.
            }
            e.Handled = true;
        };

        into.Children.Add(new TextBlock(link)
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 14),
        });
    }

    private static void AddCredit(Panel into, string label, string value)
    {
        into.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
        });
        into.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
    }
}
