using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;

namespace Hinge.App;

internal static class HandCursorResource
{
    internal static readonly InputCursor Cursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    private static readonly InputCursor ArrowCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    private static readonly System.Reflection.PropertyInfo? NativeCursor = typeof(UIElement).GetProperty(
        "ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> NativeControls = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, object> CursorElements = new();

    internal static void AttachNative(Control control)
    {
        if (NativeCursor is null || IsTextInput(control)) return;

        if (IsDefaultCursorSelectionControl(control))
        {
            Observe(control, () => ApplyArrowCursorTree(control));
            ApplyArrowCursorTree(control);
            return;
        }

        if (control is ContentDialog dialog)
        {
            Observe(control, () => ApplyCursorTree(dialog, false, IsEnabledInVisualTree(dialog), true));
            ApplyCursorTree(dialog, false, observeDescendants: true);
            return;
        }

        if (control is ListViewBase list)
        {
            if (NativeControls.TryGetValue(control, out _)) return;
            Observe(control, () => { });
            list.ContainerContentChanging += (_, args) =>
            {
                if (!args.InRecycleQueue && args.ItemContainer is Control container) AttachNative(container);
            };
            return;
        }

        if (!IsInteractive(control)) return;
        Observe(control, () => ApplyCursorTree(control, false, IsEnabledInVisualTree(control)));
        ApplyCursorTree(control, false);
    }

    private static void Observe(Control control, Action refresh)
    {
        if (NativeControls.TryGetValue(control, out _)) return;
        NativeControls.Add(control, new object());
        control.IsEnabledChanged += (_, _) =>
        {
            refresh();
            control.DispatcherQueue.TryEnqueue(() => refresh());
        };
        control.Loaded += (_, _) =>
        {
            ThemeBrushes.ApplyControlResources(control);
            refresh();
            control.DispatcherQueue.TryEnqueue(() => refresh());
        };
        if (IsDefaultCursorSelectionControl(control))
        {
            PointerEventHandler restoreArrow = (_, args) =>
            {
                if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
                var point = args.GetCurrentPoint(control).Position;
                if (point.X < 0 || point.Y < 0 || point.X >= control.ActualWidth || point.Y >= control.ActualHeight) return;
                ApplyArrowCursorTree(control);
                WinUiPopupCursor.ShowArrowForSelectionInput();
            };
            control.AddHandler(UIElement.PointerPressedEvent, restoreArrow, true);
            control.AddHandler(UIElement.PointerReleasedEvent, restoreArrow, true);
        }
        if (control is ComboBox combo)
        {
            combo.DropDownOpened += (_, _) =>
            {
                ApplyComboBoxArrowCursor(combo);
                combo.DispatcherQueue.TryEnqueue(() =>
                {
                    if (combo.IsDropDownOpen) ApplyComboBoxArrowCursor(combo);
                });
            };
            combo.DropDownClosed += (_, _) => ApplyArrowCursorTree(combo);
        }
    }

    private static void ApplyComboBoxArrowCursor(ComboBox combo)
    {
        ApplyArrowCursorTree(combo);
        for (var index = 0; index < combo.Items.Count; index++)
            if (combo.ContainerFromIndex(index) is ComboBoxItem item) AttachNative(item);
        WinUiPopupCursor.ApplyToCurrentThread();
    }

    private static void ApplyCursorTree(UIElement element, bool inheritedHand, bool inheritedEnabled = true, bool observeDescendants = false)
    {
        if (IsTextInput(element)) return;
        if (IsDefaultCursorSelectionControl(element))
        {
            ApplyArrowCursorTree(element);
            return;
        }
        if (element is ListViewBase list)
        {
            AttachNative(list);
            var listEnabled = inheritedEnabled && list.IsEnabled;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(list); index++)
                ApplyCursorTree((UIElement)VisualTreeHelper.GetChild(list, index), inheritedHand, listEnabled, observeDescendants);
            return;
        }

        var enabled = inheritedEnabled && (element is not Control enabledControl || enabledControl.IsEnabled);
        var hand = inheritedHand || (element is Control interactiveControl && IsInteractive(interactiveControl));
        if (observeDescendants && !inheritedHand && element is Control observedControl && IsInteractive(observedControl))
            Observe(observedControl, () => ApplyCursorTree(observedControl, false, IsEnabledInVisualTree(observedControl)));
        if (hand)
        {
            if (element is not FrameworkElement framework || framework.IsLoaded)
                NativeCursor!.SetValue(element, enabled ? Cursor : null);
            if (!CursorElements.TryGetValue(element, out _))
            {
                CursorElements.Add(element, new object());
                element.PointerEntered += (_, _) => NativeCursor!.SetValue(element,
                    IsInDefaultCursorSelectionControl(element) ? ArrowCursor :
                    IsEnabledInVisualTree(element) ? Cursor : null);
            }
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            ApplyCursorTree((UIElement)VisualTreeHelper.GetChild(element, index), hand, enabled, observeDescendants);
    }

    private static bool IsTextInput(UIElement element) =>
        element is TextBox or PasswordBox or RichEditBox or NumberBox or AutoSuggestBox;

    private static bool IsDefaultCursorSelectionControl(UIElement element) =>
        element is ComboBox or ComboBoxItem or RadioButton or RadioButtons;

    private static bool IsInDefaultCursorSelectionControl(UIElement element)
    {
        for (DependencyObject? current = element; current is UIElement visual; current = VisualTreeHelper.GetParent(visual))
            if (IsDefaultCursorSelectionControl(visual)) return true;
        return false;
    }

    private static void ApplyArrowCursorTree(UIElement element)
    {
        if (IsTextInput(element)) return;
        NativeCursor?.SetValue(element, ArrowCursor);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            ApplyArrowCursorTree((UIElement)VisualTreeHelper.GetChild(element, index));
    }

    private static bool IsInteractive(Control control) =>
        control is not (ListViewBase or ContentDialog or ComboBox or ComboBoxItem or RadioButton or RadioButtons) &&
        (control.IsTabStop || control is SelectorItem);

    private static bool IsEnabledInVisualTree(UIElement element)
    {
        for (DependencyObject? current = element; current is UIElement visual; current = VisualTreeHelper.GetParent(visual))
            if (visual is Control control && !control.IsEnabled) return false;
        return true;
    }

    internal static void Attach(Control control, Action<InputCursor?> apply)
    {
        apply(control.IsEnabled ? Cursor : null);
        control.IsEnabledChanged += (_, _) => apply(control.IsEnabled ? Cursor : null);
        control.Loaded += (_, _) =>
        {
            apply(control.IsEnabled ? Cursor : null);
            ThemeBrushes.ApplyControlResources(control);
        };
        control.PointerEntered += (_, _) => apply(IsEnabledInVisualTree(control) ? Cursor : null);
    }
}

public class HandCursorSurface : ContentControl
{
    public static readonly DependencyProperty IsHandCursorEnabledProperty = DependencyProperty.Register(
        nameof(IsHandCursorEnabled), typeof(bool), typeof(HandCursorSurface),
        new PropertyMetadata(true, (owner, args) => ((HandCursorSurface)owner).UpdateCursor((bool)args.NewValue)));
    public bool IsHandCursorEnabled
    {
        get => (bool)GetValue(IsHandCursorEnabledProperty);
        set => SetValue(IsHandCursorEnabledProperty, value);
    }
    public HandCursorSurface()
    {
        UpdateCursor(true);
        Loaded += (_, _) => UpdateCursor(IsHandCursorEnabled);
        IsEnabledChanged += (_, _) => UpdateCursor(IsHandCursorEnabled);
        PointerEntered += (_, _) => UpdateCursor(IsHandCursorEnabled);
    }
    private void UpdateCursor(bool enabled) => ProtectedCursor = enabled && IsEnabled ? HandCursorResource.Cursor : null;
}

public class HandCursorDatePicker : DatePicker
{
    public HandCursorDatePicker()
    {
        HandCursorResource.Attach(this, cursor => ProtectedCursor = cursor);
        Loaded += (_, _) => BalanceNumericMonth();
        SizeChanged += (_, _) => BalanceNumericMonth();
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DispatcherQueue.TryEnqueue(BalanceNumericMonth);
    }
    private void BalanceNumericMonth()
    {
        // Keep the native wider month for languages that spell out long month names.
        if (GetTemplateChild("MonthTextBlock") is not TextBlock month || !month.Text.EndsWith("月", StringComparison.Ordinal)) return;
        if (GetTemplateChild("YearColumn") is ColumnDefinition year) year.Width = new GridLength(1.2, GridUnitType.Star);
        if (GetTemplateChild("MonthColumn") is ColumnDefinition column) column.Width = new GridLength(1, GridUnitType.Star);
        if (GetTemplateChild("DayColumn") is ColumnDefinition day) day.Width = new GridLength(1, GridUnitType.Star);
    }
}

public class HandCursorCalendarDatePicker : CalendarDatePicker
{
    public HandCursorCalendarDatePicker() => HandCursorResource.Attach(this, cursor => ProtectedCursor = cursor);
}
public class HandCursorTimePicker : TimePicker
{
    public HandCursorTimePicker() => HandCursorResource.Attach(this, cursor => ProtectedCursor = cursor);
}
public class HandCursorCheckBox : CheckBox
{
    public HandCursorCheckBox() => HandCursorResource.Attach(this, cursor => ProtectedCursor = cursor);
}

public class HandCursorButton : Button
{
    public HandCursorButton() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        HandCursorResource.AttachNative(this);
    }
}

public class HandCursorAppBarButton : AppBarButton
{
    public HandCursorAppBarButton() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        HandCursorResource.AttachNative(this);
    }
}

public class HandCursorNavigationViewItem : NavigationViewItem
{
    public HandCursorNavigationViewItem() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        HandCursorResource.AttachNative(this);
    }
}

public class HandCursorListViewItem : ListViewItem
{
    public HandCursorListViewItem() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        HandCursorResource.AttachNative(this);
    }
}

public class HandCursorGridViewItem : GridViewItem
{
    public HandCursorGridViewItem() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        HandCursorResource.AttachNative(this);
    }
}

public class HandCursorListView : ListView
{
    protected override DependencyObject GetContainerForItemOverride() => new HandCursorListViewItem();
}

public class HandCursorGridView : GridView
{
    protected override DependencyObject GetContainerForItemOverride() => new HandCursorGridViewItem();
}
