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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        Unloaded += (_, _) => CopilotSettingsUserControlModel.EditModeEntered -= OnEditModeEntered;
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
    /// 重命名输入框按回车确认：移出焦点以触发保存并退出编辑状态。
    /// </summary>
    private void StepNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box)
        {
            return;
        }

        box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
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
