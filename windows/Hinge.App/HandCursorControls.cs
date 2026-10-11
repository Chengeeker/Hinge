using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

internal static class HandCursorResource
{
    internal static readonly InputCursor Cursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}

public class HandCursorButton : Button
{
    public HandCursorButton() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
    }
}

public class HandCursorAppBarButton : AppBarButton
{
    public HandCursorAppBarButton() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
    }
}

public class HandCursorNavigationViewItem : NavigationViewItem
{
    public HandCursorNavigationViewItem() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
    }
}

public class HandCursorListViewItem : ListViewItem
{
    public HandCursorListViewItem() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
    }
}

public class HandCursorGridViewItem : GridViewItem
{
    public HandCursorGridViewItem() => InitializeHandCursor();

    private void InitializeHandCursor()
    {
        ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
        IsEnabledChanged += (_, _) => ProtectedCursor = IsEnabled ? HandCursorResource.Cursor : null;
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
