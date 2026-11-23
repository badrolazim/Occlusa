using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Standardized True Modal Popup shell with header, Esc key close support,
/// zero browser-style back navigation, and built-in HasUnsavedChanges guard.
/// </summary>
[ContentProperty(nameof(DialogContent))]
public partial class ModalDialogShell : UserControl
{
    private Window? _hostWindow;
    private bool _isCloseApproved;
    private bool _isEvaluatingClose;

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(ModalDialogShell),
        new PropertyMetadata("Details"));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(ModalDialogShell),
        new PropertyMetadata(string.Empty, OnHeaderMetadataChanged));

    public static readonly DependencyProperty BadgeTextProperty = DependencyProperty.Register(
        nameof(BadgeText),
        typeof(string),
        typeof(ModalDialogShell),
        new PropertyMetadata(string.Empty, OnHeaderMetadataChanged));

    public static readonly DependencyProperty DialogContentProperty = DependencyProperty.Register(
        nameof(DialogContent),
        typeof(object),
        typeof(ModalDialogShell),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FooterContentProperty = DependencyProperty.Register(
        nameof(FooterContent),
        typeof(object),
        typeof(ModalDialogShell),
        new PropertyMetadata(null, OnFooterVisibilityChanged));

    public static readonly DependencyProperty FooterLeftContentProperty = DependencyProperty.Register(
        nameof(FooterLeftContent),
        typeof(object),
        typeof(ModalDialogShell),
        new PropertyMetadata(null, OnFooterVisibilityChanged));

    public static readonly DependencyProperty ShowDefaultCloseButtonProperty = DependencyProperty.Register(
        nameof(ShowDefaultCloseButton),
        typeof(bool),
        typeof(ModalDialogShell),
        new PropertyMetadata(true, OnFooterVisibilityChanged));

    public static readonly DependencyProperty CloseButtonTextProperty = DependencyProperty.Register(
        nameof(CloseButtonText),
        typeof(string),
        typeof(ModalDialogShell),
        new PropertyMetadata("Close"));

    public static readonly DependencyProperty HasUnsavedChangesProperty = DependencyProperty.Register(
        nameof(HasUnsavedChanges),
        typeof(bool),
        typeof(ModalDialogShell),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty UnsavedPromptTitleProperty = DependencyProperty.Register(
        nameof(UnsavedPromptTitle),
        typeof(string),
        typeof(ModalDialogShell),
        new PropertyMetadata("Unsaved Changes"));

    public static readonly DependencyProperty UnsavedPromptMessageProperty = DependencyProperty.Register(
        nameof(UnsavedPromptMessage),
        typeof(string),
        typeof(ModalDialogShell),
        new PropertyMetadata("Save changes before closing?"));

    public static readonly DependencyProperty SaveCommandProperty = DependencyProperty.Register(
        nameof(SaveCommand),
        typeof(ICommand),
        typeof(ModalDialogShell),
        new PropertyMetadata(null));

    public static readonly DependencyProperty SaveCommandParameterProperty = DependencyProperty.Register(
        nameof(SaveCommandParameter),
        typeof(object),
        typeof(ModalDialogShell),
        new PropertyMetadata(null));

    public static readonly DependencyProperty AutoAttachWindowHooksProperty = DependencyProperty.Register(
        nameof(AutoAttachWindowHooks),
        typeof(bool),
        typeof(ModalDialogShell),
        new PropertyMetadata(true));

    public static readonly DependencyProperty HasSubtitleProperty = DependencyProperty.Register(
        nameof(HasSubtitle),
        typeof(bool),
        typeof(ModalDialogShell),
        new PropertyMetadata(false));

    public static readonly DependencyProperty HasBadgeTextProperty = DependencyProperty.Register(
        nameof(HasBadgeText),
        typeof(bool),
        typeof(ModalDialogShell),
        new PropertyMetadata(false));

    public static readonly DependencyProperty ShowFooterBarProperty = DependencyProperty.Register(
        nameof(ShowFooterBar),
        typeof(bool),
        typeof(ModalDialogShell),
        new PropertyMetadata(true));

    public event EventHandler<CancelEventArgs>? SaveBeforeCloseRequested;
    public event EventHandler<CancelEventArgs>? CloseRequested;

    public ModalDialogShell()
    {
        InitializeComponent();
        Loaded += ModalDialogShell_Loaded;
        Unloaded += ModalDialogShell_Unloaded;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public object? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    public object? FooterLeftContent
    {
        get => GetValue(FooterLeftContentProperty);
        set => SetValue(FooterLeftContentProperty, value);
    }

    public bool ShowDefaultCloseButton
    {
        get => (bool)GetValue(ShowDefaultCloseButtonProperty);
        set => SetValue(ShowDefaultCloseButtonProperty, value);
    }

    public string CloseButtonText
    {
        get => (string)GetValue(CloseButtonTextProperty);
        set => SetValue(CloseButtonTextProperty, value);
    }

    public bool HasUnsavedChanges
    {
        get => (bool)GetValue(HasUnsavedChangesProperty);
        set => SetValue(HasUnsavedChangesProperty, value);
    }

    public string UnsavedPromptTitle
    {
        get => (string)GetValue(UnsavedPromptTitleProperty);
        set => SetValue(UnsavedPromptTitleProperty, value);
    }

    public string UnsavedPromptMessage
    {
        get => (string)GetValue(UnsavedPromptMessageProperty);
        set => SetValue(UnsavedPromptMessageProperty, value);
    }

    public ICommand? SaveCommand
    {
        get => (ICommand?)GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    public object? SaveCommandParameter
    {
        get => GetValue(SaveCommandParameterProperty);
        set => SetValue(SaveCommandParameterProperty, value);
    }

    public bool AutoAttachWindowHooks
    {
        get => (bool)GetValue(AutoAttachWindowHooksProperty);
        set => SetValue(AutoAttachWindowHooksProperty, value);
    }

    public bool HasSubtitle
    {
        get => (bool)GetValue(HasSubtitleProperty);
        private set => SetValue(HasSubtitleProperty, value);
    }

    public bool HasBadgeText
    {
        get => (bool)GetValue(HasBadgeTextProperty);
        private set => SetValue(HasBadgeTextProperty, value);
    }

    public bool ShowFooterBar
    {
        get => (bool)GetValue(ShowFooterBarProperty);
        private set => SetValue(ShowFooterBarProperty, value);
    }

    /// <summary>
    /// Triggers modal closure with the built-in HasUnsavedChanges confirmation guard.
    /// </summary>
    public void RequestClose()
    {
        _ = RequestCloseInternalAsync();
    }

    /// <summary>
    /// Marks the current close as approved (e.g., immediately after saving) and closes the host window.
    /// </summary>
    public void ForceCloseWithoutPrompt()
    {
        _isCloseApproved = true;
        SetCurrentValue(HasUnsavedChangesProperty, false);
        _hostWindow ??= Window.GetWindow(this);
        _hostWindow?.Close();
    }

    private void ModalDialogShell_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateHeaderAndFooterFlags();
        AttachWindowHooks();
    }

    private void ModalDialogShell_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachWindowHooks();
    }

    private void AttachWindowHooks()
    {
        if (!AutoAttachWindowHooks)
        {
            return;
        }

        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, _hostWindow))
        {
            return;
        }

        DetachWindowHooks();
        _hostWindow = window;
        if (_hostWindow != null)
        {
            _hostWindow.PreviewKeyDown += HostWindow_PreviewKeyDown;
            _hostWindow.Closing += HostWindow_Closing;
        }
    }

    private void DetachWindowHooks()
    {
        if (_hostWindow != null)
        {
            _hostWindow.PreviewKeyDown -= HostWindow_PreviewKeyDown;
            _hostWindow.Closing -= HostWindow_Closing;
            _hostWindow = null;
        }
    }

    private void HostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (Keyboard.FocusedElement is DependencyObject focused &&
            ItemsControl.ItemsControlFromItemContainer(focused) is ComboBox { IsDropDownOpen: true })
        {
            return;
        }

        if (e.OriginalSource is DependencyObject src && IsInsideOpenComboBox(src))
        {
            return;
        }

        e.Handled = true;
        RequestClose();
    }

    private void HostWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isCloseApproved || !HasUnsavedChanges)
        {
            return;
        }

        if (_isEvaluatingClose)
        {
            e.Cancel = true;
            return;
        }

        // Handle async or sync save cleanly by cancelling the native close first if dirty,
        // then running the unified prompt & closing once approved.
        e.Cancel = true;
        _ = RequestCloseInternalAsync();
    }

    private async Task RequestCloseInternalAsync()
    {
        if (_isEvaluatingClose)
        {
            return;
        }

        _isEvaluatingClose = true;
        try
        {
            bool canClose = await EvaluateUnsavedChangesAsync();
            if (!canClose)
            {
                return;
            }

            var closeArgs = new CancelEventArgs(false);
            CloseRequested?.Invoke(this, closeArgs);
            if (closeArgs.Cancel)
            {
                return;
            }

            _isCloseApproved = true;
            _hostWindow ??= Window.GetWindow(this);
            _hostWindow?.Close();
        }
        finally
        {
            _isEvaluatingClose = false;
        }
    }

    private async Task<bool> EvaluateUnsavedChangesAsync()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var owner = _hostWindow ?? Window.GetWindow(this);
        var result = owner != null
            ? MessageBox.Show(owner, UnsavedPromptMessage, UnsavedPromptTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning)
            : MessageBox.Show(UnsavedPromptMessage, UnsavedPromptTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.No)
        {
            SetCurrentValue(HasUnsavedChangesProperty, false);
            return true;
        }

        // MessageBoxResult.Yes -> Attempt save before closing
        if (SaveBeforeCloseRequested != null)
        {
            var saveArgs = new CancelEventArgs(false);
            SaveBeforeCloseRequested.Invoke(this, saveArgs);
            if (saveArgs.Cancel)
            {
                return false;
            }

            SetCurrentValue(HasUnsavedChangesProperty, false);
            return true;
        }

        if (SaveCommand != null)
        {
            if (!SaveCommand.CanExecute(SaveCommandParameter))
            {
                return false;
            }

            SaveCommand.Execute(SaveCommandParameter);

            // Support CommunityToolkit.Mvvm IAsyncRelayCommand cleanly without hard dependency
            var executionTaskProp = SaveCommand.GetType().GetProperty("ExecutionTask");
            if (executionTaskProp?.GetValue(SaveCommand) is Task runningTask && !runningTask.IsCompleted)
            {
                await runningTask;
            }

            return !HasUnsavedChanges;
        }

        // No save handler wired; treat Yes as consent to proceed via CloseRequested handler
        return true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        RequestClose();
    }

    private static void OnHeaderMetadataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ModalDialogShell)d).UpdateHeaderAndFooterFlags();
    }

    private static void OnFooterVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ModalDialogShell)d).UpdateHeaderAndFooterFlags();
    }

    private void UpdateHeaderAndFooterFlags()
    {
        HasSubtitle = !string.IsNullOrWhiteSpace(Subtitle);
        HasBadgeText = !string.IsNullOrWhiteSpace(BadgeText);
        ShowFooterBar = ShowDefaultCloseButton || FooterContent != null || FooterLeftContent != null;
    }

    private static bool IsInsideOpenComboBox(DependencyObject current)
    {
        DependencyObject? node = current;
        while (node != null)
        {
            if (node is ComboBox cb && cb.IsDropDownOpen)
            {
                return true;
            }

            node = LogicalTreeHelper.GetParent(node) ?? System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        return false;
    }
}
