using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SteamBacklogPicker.UI;

/// <summary>Short, event-driven feedback. No timers, layout animations or idle rendering.</summary>
public static class VisualMotion
{
    public static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached(
        "Feedback", typeof(bool), typeof(VisualMotion), new PropertyMetadata(false, FeedbackChanged));
    public static readonly DependencyProperty EnterKeyProperty = DependencyProperty.RegisterAttached(
        "EnterKey", typeof(object), typeof(VisualMotion), new PropertyMetadata(null, EnterKeyChanged));
    public static readonly DependencyProperty ClipRadiusProperty = DependencyProperty.RegisterAttached(
        "ClipRadius", typeof(double), typeof(VisualMotion), new PropertyMetadata(0d, ClipRadiusChanged));
    private static readonly DependencyProperty PendingEnterProperty = DependencyProperty.RegisterAttached(
        "PendingEnter", typeof(DispatcherOperation), typeof(VisualMotion));

    public static bool GetFeedback(DependencyObject target) => (bool)target.GetValue(FeedbackProperty);
    public static void SetFeedback(DependencyObject target, bool value) => target.SetValue(FeedbackProperty, value);
    public static object? GetEnterKey(DependencyObject target) => target.GetValue(EnterKeyProperty);
    public static void SetEnterKey(DependencyObject target, object? value) => target.SetValue(EnterKeyProperty, value);
    public static double GetClipRadius(DependencyObject target) => (double)target.GetValue(ClipRadiusProperty);
    public static void SetClipRadius(DependencyObject target, double value) => target.SetValue(ClipRadiusProperty, value);

    public static bool ReducedMotionRequested =>
        string.Equals(Environment.GetEnvironmentVariable("SBP_REDUCED_MOTION"), "1", StringComparison.Ordinal);
    public static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation && !ReducedMotionRequested;

    private static void FeedbackChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ButtonBase button) return;
        if ((bool)args.OldValue)
        {
            button.MouseEnter -= FeedbackChanged;
            button.MouseLeave -= FeedbackChanged;
            button.PreviewMouseLeftButtonDown -= FeedbackChanged;
            button.PreviewMouseLeftButtonUp -= FeedbackChanged;
            button.LostMouseCapture -= FeedbackChanged;
            button.IsEnabledChanged -= EnabledChanged;
            button.Unloaded -= OnUnloaded;
        }
        if (!(bool)args.NewValue) return;
        button.MouseEnter += FeedbackChanged;
        button.MouseLeave += FeedbackChanged;
        button.PreviewMouseLeftButtonDown += FeedbackChanged;
        button.PreviewMouseLeftButtonUp += FeedbackChanged;
        button.LostMouseCapture += FeedbackChanged;
        button.IsEnabledChanged += EnabledChanged;
        button.Unloaded += OnUnloaded;
    }

    private static void EnabledChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdateFeedback((ButtonBase)sender);
    private static void FeedbackChanged(object sender, MouseEventArgs args) => UpdateFeedback((ButtonBase)sender);

    private static void UpdateFeedback(ButtonBase button)
    {
        if (button.Template?.FindName("HoverLayer", button) is not Border overlay) return;
        var hovered = button.IsEnabled && button.IsMouseOver;
        var pressed = hovered && Mouse.LeftButton == MouseButtonState.Pressed;
        Animate(overlay, UIElement.OpacityProperty, hovered ? pressed ? .12 : .06 : 0, 140);
        if (button.Template.FindName("FeedbackRoot", button) is FrameworkElement root)
            Animate(MutableOffset(root), TranslateTransform.YProperty, pressed ? 1 : 0, 100);
    }

    private static void EnterKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element || Equals(args.OldValue, args.NewValue)) return;
        if (element.GetValue(PendingEnterProperty) is DispatcherOperation pending) pending.Abort();
        // Visibility/bindings settle once before animating. Repeated updates coalesce.
        element.SetValue(PendingEnterProperty, element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            element.ClearValue(PendingEnterProperty);
            if (!element.IsVisible || !AnimationsEnabled) return;
            var offset = MutableOffset(element);
            Animate(offset, TranslateTransform.YProperty, 0, 190, 6);
            Animate(element, UIElement.OpacityProperty, 1, 190, .86);
        })));
        element.Unloaded -= OnUnloaded;
        element.Unloaded += OnUnloaded;
    }

    private static TranslateTransform MutableOffset(FrameworkElement element)
    {
        // WPF freezes Freezables declared in shared control templates.
        // Clone per visual before animating; never mutate the template's shared instance.
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } existing) return existing;
        var offset = element.RenderTransform is TranslateTransform frozen
            ? frozen.CloneCurrentValue()
            : new TranslateTransform();
        element.RenderTransform = offset;
        return offset;
    }

    private static void Animate(Animatable target, DependencyProperty property, double value, int milliseconds, double? from = null)
    {
        var current = from ?? (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
        if (!AnimationsEnabled || Math.Abs(current - value) < .001) return;
        var animation = new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Animate(UIElement target, DependencyProperty property, double value, int milliseconds, double? from = null)
    {
        var current = from ?? (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
        if (!AnimationsEnabled || Math.Abs(current - value) < .001) return;
        target.BeginAnimation(property, new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if (element.GetValue(PendingEnterProperty) is DispatcherOperation pending) pending.Abort();
        element.ClearValue(PendingEnterProperty);
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } offset) offset.BeginAnimation(TranslateTransform.YProperty, null);
        if (element is ButtonBase button && button.Template?.FindName("HoverLayer", button) is Border overlay)
        {
            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            overlay.Opacity = 0;
            if (button.Template.FindName("FeedbackRoot", button) is FrameworkElement root && root.RenderTransform is TranslateTransform { IsFrozen: false } press)
            {
                press.BeginAnimation(TranslateTransform.YProperty, null);
                press.Y = 0;
            }
        }
    }

    private static void ClipRadiusChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.SizeChanged -= UpdateClip;
        if ((double)args.NewValue > 0) element.SizeChanged += UpdateClip;
        UpdateClip(element, null!);
    }

    private static void UpdateClip(object sender, SizeChangedEventArgs args)
    {
        var element = (FrameworkElement)sender;
        var radius = GetClipRadius(element);
        if (radius <= 0) { element.Clip = null; return; }
        var clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        clip.Freeze();
        element.Clip = clip;
    }
}
