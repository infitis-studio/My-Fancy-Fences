using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace My_Fancy_Fences;

internal static class SmoothScrollService
{
    private const double WheelDistanceMultiplier = 0.74;
    private const double MinimumWheelDistance = 78;
    private const double Easing = 0.22;
    private const double SnapDistance = 0.6;

    private static readonly Dictionary<ScrollViewer, SmoothScrollState> States = [];
    private static bool _isRegistered;

    public static void Register()
    {
        if (_isRegistered)
            return;

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnUnloaded));

        _isRegistered = true;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer ||
            e.Handled ||
            Keyboard.Modifiers != ModifierKeys.None ||
            scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var direction = e.Delta > 0 ? -1 : 1;
        var isAtTop = scrollViewer.VerticalOffset <= 0;
        var isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight;
        if ((direction < 0 && isAtTop) || (direction > 0 && isAtBottom))
            return;

        var state = GetState(scrollViewer);
        var distance = Math.Max(MinimumWheelDistance, Math.Abs(e.Delta) * WheelDistanceMultiplier);
        var start = state.IsAnimating ? state.TargetOffset : scrollViewer.VerticalOffset;
        state.TargetOffset = Math.Clamp(
            start + direction * distance,
            0,
            scrollViewer.ScrollableHeight);

        state.IsAnimating = true;
        state.Timer.Start();
        e.Handled = true;
    }

    private static SmoothScrollState GetState(ScrollViewer scrollViewer)
    {
        if (States.TryGetValue(scrollViewer, out var state))
            return state;

        state = new SmoothScrollState(scrollViewer);
        States[scrollViewer] = state;
        return state;
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer ||
            !States.Remove(scrollViewer, out var state))
        {
            return;
        }

        state.Timer.Stop();
    }

    private sealed class SmoothScrollState
    {
        private readonly ScrollViewer _scrollViewer;

        public SmoothScrollState(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            Timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            Timer.Tick += Tick;
        }

        public DispatcherTimer Timer { get; }

        public double TargetOffset { get; set; }

        public bool IsAnimating { get; set; }

        private void Tick(object? sender, EventArgs e)
        {
            if (_scrollViewer.ScrollableHeight <= 0)
            {
                Stop();
                return;
            }

            TargetOffset = Math.Clamp(TargetOffset, 0, _scrollViewer.ScrollableHeight);
            var current = _scrollViewer.VerticalOffset;
            var delta = TargetOffset - current;

            if (Math.Abs(delta) <= SnapDistance)
            {
                _scrollViewer.ScrollToVerticalOffset(TargetOffset);
                Stop();
                return;
            }

            _scrollViewer.ScrollToVerticalOffset(current + delta * Easing);
        }

        private void Stop()
        {
            IsAnimating = false;
            Timer.Stop();
        }
    }
}
