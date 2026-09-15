// <copyright file="CopilotUserControl.xaml.cs" company="MaaAssistantArknights">
// Part of the MaaWpfGui project, maintained by the MaaAssistantArknights team (Maa Team)
// Copyright (C) 2021-2025 MaaAssistantArknights Contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License v3.0 only as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY
// </copyright>

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaaWpfGui.Extensions;
using MaaWpfGui.Models;
using MaaWpfGui.ViewModels.UserControl.TaskQueue;

namespace MaaWpfGui.Views.UserControl.TaskQueue;

/// <summary>
/// CopilotUserControl.xaml 的交互逻辑（拖拽排序由 dd:DragDrop 提供）。
/// </summary>
public partial class CopilotUserControl : System.Windows.Controls.UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotUserControl"/> class.
    /// </summary>
    public CopilotUserControl()
    {
        InitializeComponent();
        CopilotSettingsUserControlModel.EditModeEntered += OnEditModeEntered;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private Window? _hookedWindow;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面来回切换时静态事件要重新挂上
        CopilotSettingsUserControlModel.EditModeEntered -= OnEditModeEntered;
        CopilotSettingsUserControlModel.EditModeEntered += OnEditModeEntered;
        TaskSettingVisibilityInfo.Instance.PropertyChanged -= OnTaskSettingVisibilityChanged;
        TaskSettingVisibilityInfo.Instance.PropertyChanged += OnTaskSettingVisibilityChanged;
        HookWindow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CopilotSettingsUserControlModel.EditModeEntered -= OnEditModeEntered;
        TaskSettingVisibilityInfo.Instance.PropertyChanged -= OnTaskSettingVisibilityChanged;
        if (_hookedWindow is not null)
        {
            _hookedWindow.PreviewMouseDown -= OnWindowPreviewMouseDown;
            _hookedWindow = null;
        }
    }

    /// <summary>
    /// 切到「高级设置」面板后（点小任务的设置图标，或直接点上面的「高级设置」按钮），
    /// 把外面的设置页滚回最上面：面板一换，内容高度就变了，滚动位置会停在中间。
    /// 等布局跑完再滚，免得被"把焦点元素滚进视野"顶回去。
    /// </summary>
    private void OnTaskSettingVisibilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TaskSettingVisibilityInfo.EnableAdvancedSettings)
            || !TaskSettingVisibilityInfo.Instance.EnableAdvancedSettings)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new System.Action(ScrollHostToTop));
    }

    private void ScrollHostToTop()
    {
        FindAncestor<ScrollViewer>(this)?.ScrollToVerticalOffset(0);
    }

    /// <summary>
    /// 把窗口级的鼠标按下事件挂上：点列表外面的空白处时焦点不会移走，
    /// TextBox 的 LostFocus 收不了尾，所以在这里兜底。
    /// </summary>
    private void HookWindow()
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, _hookedWindow))
        {
            return;
        }

        if (_hookedWindow is not null)
        {
            _hookedWindow.PreviewMouseDown -= OnWindowPreviewMouseDown;
        }

        _hookedWindow = window;
        if (_hookedWindow is not null)
        {
            _hookedWindow.PreviewMouseDown += OnWindowPreviewMouseDown;
        }
    }

    /// <summary>
    /// 正在重命名时点到输入框以外的任何地方（含列表外的空白处）就保存并退出编辑。
    /// </summary>
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var model = CopilotSettingsUserControlModel.Instance;
        var box = FindEditingTextBox(model);
        if (box is null || IsDescendantOf(e.OriginalSource as DependencyObject, box))
        {
            return;
        }

        model.SaveName();
    }

    private TextBox? FindEditingTextBox(CopilotSettingsUserControlModel model)
    {
        foreach (var item in model.Items)
        {
            if (!item.IsEditing)
            {
                continue;
            }

            if (FindContainer(StepList, item) is { } container)
            {
                return FindDescendant<TextBox>(container);
            }
        }

        return null;
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }

    /// <summary>
    /// 列表滚到两端后，把滚轮事件交给外层设置页面继续滚动。
    /// </summary>
    private void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox list)
        {
            return;
        }

        var inner = FindDescendant<ScrollViewer>(list);
        if (inner is null)
        {
            return;
        }

        var atTop = inner.VerticalOffset <= 0;
        var atBottom = inner.VerticalOffset >= inner.ScrollableHeight - 0.5;
        if ((e.Delta > 0 && !atTop) || (e.Delta < 0 && !atBottom))
        {
            return; // 列表自身还能滚动，交给列表处理
        }

        var outer = FindAncestor<ScrollViewer>(this);
        if (outer is null || ReferenceEquals(outer, inner))
        {
            return;
        }

        e.Handled = true;
        outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta);
    }

    /// <summary>
    /// 重命名输入框按回车确认：保存名字并退出编辑状态。
    /// </summary>
    private void StepNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox)
        {
            return;
        }

        // 收掉键盘焦点（留着的焦点框会画虚线），保存走显式调用：
        // ClearFocus 只清键盘焦点、不动逻辑焦点，TextBox 的 LostFocus 不会触发，光靠它退不出编辑状态。
        Keyboard.ClearFocus();
        CopilotSettingsUserControlModel.Instance.SaveName();
        e.Handled = true;
    }

    /// <summary>
    /// 导航目标下拉框做成可搜索（和"自动肉鸽 · 开局干员"同一套 MakeComboBoxSearchable）。
    /// 选择器平时是折叠的：Collapsed 的元素不走布局，控件模板还没实例化，Loaded 时取不到
    /// PART_EditableTextBox，搜索会挂不上；所以等它真正可见、布局跑完后再补挂一次
    /// （MakeComboBoxSearchable 内部有去重标记，重复调用安全）。
    /// </summary>
    private void NavTargetComboBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ComboBox { IsVisible: true } comboBox)
        {
            return;
        }

        comboBox.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new System.Action(comboBox.MakeComboBoxSearchable));
    }

    private void OnEditModeEntered(CopilotSubTaskItem item)
    {
        // 等模板里的编辑框可见后再聚焦
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new System.Action(() => {
                var container = FindContainer(StepList, item);
                var box = container is null ? null : FindDescendant<TextBox>(container);
                box?.Focus();
                box?.SelectAll();
            }));
    }

    private static ListBoxItem? FindContainer(ItemsControl list, object dataContext)
    {
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container &&
                ReferenceEquals(container.DataContext, dataContext))
            {
                return container;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var deeper = FindDescendant<T>(child);
            if (deeper is not null)
            {
                return deeper;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
