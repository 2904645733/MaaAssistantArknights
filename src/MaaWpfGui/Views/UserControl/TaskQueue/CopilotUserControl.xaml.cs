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

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
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
    /// 正在重命名时点到输入框以外的任何地方（含列表外的空白处）就保存并退出编辑；
    /// 同时：点到搜索框外面 = 退出搜索状态（收起下拉 + 把焦点从搜索框拿走）。
    /// </summary>
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;

        // 判断"在不在搜索框上"用鼠标命中测试（搜索框自身 / 它展开的下拉内容），不要走树上溯：
        // 下拉内容在独立 Popup 里，可视父链会断在 PopupRoot、逻辑父链也不一定回到 ComboBox ——
        // 按树判断会把"点下拉里的目标"误判成"点外面"，先把下拉关掉，结果怎么点都选不中。
        if (NavTargetComboBox is { IsKeyboardFocusWithin: true } navBox)
        {
            var popupContent = FindDescendant<Popup>(navBox)?.Child as UIElement;
            var overComboBox = navBox.IsMouseOver;
            var overPopup = popupContent?.IsMouseOver == true;
            if (!overComboBox && !overPopup)
            {
                ExitNavSearch(navBox);
                _navSearchLogger.Information(
                    "[NavSearch] 点到搜索框外面：退出搜索状态（框内={OverBox} 下拉内={OverPopup} 命中={Source}）",
                    overComboBox,
                    overPopup,
                    origin?.GetType().Name ?? "(null)");
            }
        }

        var model = CopilotSettingsUserControlModel.Instance;
        var box = FindEditingTextBox(model);
        if (box is null || IsDescendantOf(origin, box))
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

    #region 导航搜索框（打字过滤、回车添加/列出结果、点外面退出搜索状态）

    private bool _navSearchBoxHooked;

    /// <summary>最近一次文本变化是不是键盘敲出来的（用来和"点下拉里的目标"引起的文本变化区分开）。</summary>
    private bool _navSearchTyping;

    /// <summary>按下回车那一刻框里的文字（回车后要把它换回来，抵掉 ComboBox 自动填入的高亮项文字）。</summary>
    private string _navTextBeforeEnter = string.Empty;

    /// <summary>输入法正在组字（拼音还没上屏）：这时按回车是"确认输入"，不能当成"退出搜索"。</summary>
    private bool _navImeComposing;

    private static readonly Serilog.ILogger _navSearchLogger = Serilog.Log.ForContext<CopilotUserControl>();

    /// <summary>
    /// 导航搜索框：进可视化树后，把它里面那个可编辑的 TextBox 的 TextChanged 挂上，
    /// 输入什么就直接写进界面逻辑。不指望 ComboBox.Text 双向绑定和 MAA 的 MakeComboBoxSearchable：
    /// 前者在这个框上没把输入传进来（实测打字后下拉不过滤），后者要拿控件模板换 ItemsSource 也没挂住。
    /// </summary>
    /// <param name="sender">搜索框。</param>
    /// <param name="e">事件参数。</param>
    private void NavTargetComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        // 模板可能还没实例化，等布局跑完再找里面的 TextBox
        comboBox.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new System.Action(() => {
                if (_navSearchBoxHooked)
                {
                    return;
                }

                if (FindDescendant<TextBox>(comboBox) is not { } textBox)
                {
                    _navSearchLogger.Information("[NavSearch] 没找到输入框（搜索不会被过滤）");
                    return;
                }

                _navSearchBoxHooked = true;
                textBox.TextChanged += (_, _) => OnNavSearchTextChanged(comboBox, textBox);
                textBox.AddHandler(
                    TextCompositionManager.TextInputStartEvent,
                    new TextCompositionEventHandler((_, _) => {
                        // 输入法开始组字（拼音还没上屏）：这期间的回车是"确认输入"，不是"退出搜索"
                        _navImeComposing = true;
                        _navSearchLogger.Information("[NavSearch] 输入法开始组字");
                    }));
                textBox.AddHandler(
                    TextCompositionManager.TextInputEvent,
                    new TextCompositionEventHandler((_, _) => {
                        if (_navImeComposing)
                        {
                            _navImeComposing = false;
                            _navSearchLogger.Information("[NavSearch] 输入法上屏完成");
                        }
                    }));
                comboBox.SelectionChanged += (_, _) => OnNavOptionSelected(comboBox, textBox);
                comboBox.DropDownOpened += (_, _) => {
                    // 展开时不要动过滤：列表内容始终跟搜索框里的文字走 ——
                    // 框里有字就显示这几个搜索结果（比如打"b"再展开还是那 7 个），空框才是完整列表。
                    // 可编辑下拉框展开时会把框里的文字全选（本来是为了方便直接覆盖输入），
                    // 但打字时下拉是自动打开的，于是"刚打的字"看起来像被选中了；
                    // 统一把光标放到文字末尾（零长度选区 = 什么都没选中）。
                    textBox.Dispatcher.BeginInvoke(
                        new System.Action(() => textBox.Select(textBox.Text.Length, 0)),
                        System.Windows.Threading.DispatcherPriority.Background);
                };
                comboBox.DropDownClosed += (_, _) => _navSearchLogger.Information(
                    "[NavSearch] 下拉收起；框内文本「{Text}」；选中={Item}",
                    comboBox.Text,
                    (comboBox.SelectedItem as NavChapterOption)?.Display ?? "(无)");
                _navSearchLogger.Information("[NavSearch] 已挂上输入监听");
            }));
    }

    /// <summary>
    /// 在下拉里选好了一个目标。照 MAA 原本那套可搜索下拉框的做法收尾：
    /// <list type="bullet">
    /// <item>清掉列表过滤 —— 下次展开还是完整列表，而不是只剩刚选中的那一个；</item>
    /// <item>光标移到文字末尾 —— 不要全选（整块高亮看着像还在搜索/输入状态）。</item>
    /// </list>
    /// </summary>
    /// <param name="comboBox">搜索框。</param>
    /// <param name="textBox">搜索框里那个可编辑的输入框。</param>
    private void OnNavOptionSelected(ComboBox comboBox, TextBox textBox)
    {
        CopilotSettingsUserControlModel.Instance.ClearNavFilter();

        textBox.Dispatcher.BeginInvoke(
            new System.Action(() => textBox.Select(textBox.Text.Length, 0)),
            System.Windows.Threading.DispatcherPriority.Background);

        _navSearchLogger.Information(
            "[NavSearch] 选中变化：{Item}；框内文本「{Text}」",
            (comboBox.SelectedItem as NavChapterOption)?.Display ?? "(无)",
            comboBox.Text);
    }

    /// <summary>
    /// 搜索框内容变化：写回界面逻辑；如果这次变化是"敲键盘"造成的，按输入过滤下拉列表
    /// （不展开也能边打边出结果），并在开始输入新内容时撤掉旧选中项。
    /// 点下拉里的目标时 ComboBox 也会改文本（先清空、再填上目标的显示文本），那两轮不是打字，
    /// 这里一概不动选中项、也不去开下拉。
    /// </summary>
    /// <param name="comboBox">搜索框。</param>
    /// <param name="textBox">搜索框里那个可编辑的输入框。</param>
    private void OnNavSearchTextChanged(ComboBox comboBox, TextBox textBox)
    {
        CopilotSettingsUserControlModel.Instance.NavSearchText = textBox.Text;

        var typing = _navSearchTyping;
        _navSearchTyping = false;

        // 先判"这是不是点下拉里的目标引起的文本变化"（文本正好等于当前选中项的显示文本）：
        // 这种变化必须直接放过 —— 否则会把"选中目标的显示文本"当成搜索词去过滤，
        // 结果列表只剩刚选中的那一个，后面再怎么打字都搜不到别的。
        // 注意这个判断必须排在按键标志前面：有些按键不改动文本（方向键、输入法的中间状态），
        // 标志不会被消费掉、会残留成 true，于是"点选目标"就会被误判成"打字"。
        if (comboBox.SelectedItem is NavChapterOption selected
            && string.Equals(selected.Display, textBox.Text, StringComparison.Ordinal))
        {
            return;
        }

        if (!typing)
        {
            return;
        }

        // 打字：按输入过滤下拉列表（不展开也能边打边出结果）
        CopilotSettingsUserControlModel.Instance.ApplyNavFilter(textBox.Text);

        if (comboBox.SelectedItem is not null)
        {
            comboBox.SelectedItem = null;
        }

        if (textBox.IsKeyboardFocusWithin && !comboBox.IsDropDownOpen)
        {
            comboBox.IsDropDownOpen = true;
            _navSearchLogger.Information("[NavSearch] 打字时下拉被收起，已重新打开");
        }
    }

    /// <summary>
    /// 搜索框的键盘行为：只处理回车，其它按键一律不动输入框内容 ——
    /// 主动改 Text / SelectedItem 会让下拉收起、也会打断中文输入法上屏。
    /// <para>
    /// 回车只做一件事：<b>退出搜索状态</b>（收起下拉、把焦点从搜索框拿走），
    /// 不选中任何目标、不把匹配项的文字填进框里（框里保留你打进去的内容）。
    /// </para>
    /// 中文输入法上屏用的回车是 <see cref="Key.ImeProcessed"/>：放行让输入法上屏，
    /// 但 ComboBox 会顺手把"高亮项"的文字填进框里（就是那个"排第二的 BI"），所以上屏后要把它撤掉。
    /// </summary>
    /// <param name="sender">搜索框。</param>
    /// <param name="e">按键事件参数。</param>
    private void NavTargetComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        // 输入法处理过的键：真实按键在 ImeProcessedKey 里
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (key is not (Key.Enter or Key.Return))
        {
            // 非回车 = 用户在打字（含输入法的拼音键）：记一下，供 TextChanged 里区分"打字"和"点选目标"
            _navSearchTyping = true;
            return;
        }

        // 输入法正在组字时的回车 = "确认输入"（把拼音上屏），不是"退出搜索"：
        // 放行给输入法处理，也不退出搜索 —— 用户要按第二个回车才是退出。
        // 判断用 IMM 接口问系统（WPF 的事件不可靠）；另外把之前记的标志也用上、并用掉，
        // 双保险：接口查不到时靠标志，标志残留时也不会挡住下一次回车。
        if (e.Key == Key.ImeProcessed && (IsImeComposing(Window.GetWindow(comboBox)) || _navImeComposing))
        {
            _navImeComposing = false;
            _navSearchLogger.Information("[NavSearch] 回车：输入法确认上屏（不退出搜索）");

            // 这个回车要留给输入法上屏，但它同时也会被 ComboBox 用来"提交"当前高亮项
            //（把那一项的文字填进框里、并把它选中）—— 所以上屏完成后再把这一步撤掉。
            var textBefore = comboBox.Text;
            comboBox.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Action(() => UndoNavComboBoxCommit(comboBox, textBefore)));
            return;
        }

        _navSearchLogger.Information("[NavSearch] 回车：退出搜索（组字中={Composing}）", _navImeComposing);

        // 回车前框里是什么（万一 ComboBox 用它提交了高亮项，好把文字换回来）
        _navTextBeforeEnter = comboBox.Text;
        e.Handled = true;
        FinishNavSearchAfterEnter(comboBox);
    }

    /// <summary>
    /// 去掉输入法在拼音音节之间塞的隔音符号（打 "ad" 会变成 "a'd"）：回车后把框里的文字换回来时用。
    /// </summary>
    /// <param name="text">原文本。</param>
    /// <returns>去掉隔音符号的文本。</returns>
    private static string StripImeSeparator(string text)
        => text.Replace("'", string.Empty).Replace("’", string.Empty).Replace("‘", string.Empty);

    /// <summary>
    /// 输入法当前是不是正在组字（拼音还没上屏）。
    /// <para>
    /// WPF 自带的 TextInputStart/TextInput 事件在这台机器上靠不住（实测上屏时 TextInput 不触发），
    /// 所以直接问 Windows 的 IMM 接口：取窗口的输入法上下文，看它有没有"组字中"的字符串。
    /// 这样"组字中的回车"和"组字结束后的回车"就能准确分开：
    /// 前者交给输入法上屏，后者才是我们的"退出搜索"。
    /// </para>
    /// </summary>
    /// <param name="window">搜索框所在的窗口（IMM 的输入法上下文挂在窗口上）。</param>
    /// <returns>正在组字则为 true。</returns>
    private static bool IsImeComposing(Window? window)
    {
        if (window is null)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var immContext = ImmGetContext(hwnd);
        if (immContext == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            // 返回组字字符串的字节数：> 0 表示正在组字
            return ImmGetCompositionStringW(immContext, GcsCompStr, null, 0) > 0;
        }
        finally
        {
            _ = ImmReleaseContext(hwnd, immContext);
        }
    }

    private const int GcsCompStr = 0x0008;

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hImmContext);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    private static extern int ImmGetCompositionStringW(IntPtr hImmContext, int index, byte[]? buffer, int bufferLength);

    /// <summary>
    /// 撤掉 ComboBox 用回车（输入法上屏那次）顺手做的"提交"：把选中项去掉、文字换回上屏前的内容。
    /// 不然打完 b 一回车，框里就变成高亮项（BB / BP / …）了。
    /// </summary>
    /// <param name="comboBox">搜索框。</param>
    /// <param name="textBefore">上屏前框里的文字。</param>
    private void UndoNavComboBoxCommit(ComboBox comboBox, string textBefore)
    {
        var wanted = StripImeSeparator(textBefore);

        if (comboBox.SelectedItem is NavChapterOption selected
            && !string.Equals(selected.Display, wanted, StringComparison.Ordinal))
        {
            comboBox.SelectedItem = null;
        }

        if (!string.Equals(comboBox.Text, wanted, StringComparison.Ordinal))
        {
            comboBox.Text = wanted;
        }

        CopilotSettingsUserControlModel.Instance.NavSearchText = comboBox.Text;
        _navSearchLogger.Information("[NavSearch] 上屏后撤掉 ComboBox 的提交（框内文本「{Text}」）", comboBox.Text);
    }

    /// <summary>
    /// 回车（含输入法上屏那个回车）收尾：先退出搜索状态，再把 ComboBox 自动填进去的高亮项文字换回用户输入。
    /// 顺序很重要：先失焦，之后改文本不会再触发"打字"分支把下拉打开回来。
    /// </summary>
    /// <param name="comboBox">搜索框。</param>
    private void FinishNavSearchAfterEnter(ComboBox comboBox)
    {
        ExitNavSearchAndDropSelection(comboBox);

        // ComboBox 可能已经用回车把"当前高亮项"选中并填进框里了：撤掉选中项、把文字换回去
        //（顺便去掉输入法塞进来的隔音符号）
        var wanted = StripImeSeparator(_navTextBeforeEnter);

        if (comboBox.SelectedItem is NavChapterOption selected
            && !string.Equals(selected.Display, wanted, StringComparison.Ordinal))
        {
            comboBox.SelectedItem = null;
        }

        if (!string.Equals(comboBox.Text, wanted, StringComparison.Ordinal))
        {
            comboBox.Text = wanted;
        }

        CopilotSettingsUserControlModel.Instance.NavSearchText = comboBox.Text;
        _navSearchLogger.Information("[NavSearch] 回车：退出搜索（框内文本「{Text}」）", comboBox.Text);
    }

    /// <summary>
    /// 退出搜索状态：收起下拉 + 把键盘焦点和逻辑焦点都从搜索框拿走（光标停闪、蓝色边框恢复）。
    /// 点搜索框外面（包括点「添加」按钮）走这里 —— <b>不动选中项</b>，否则「添加」就拿不到目标了。
    /// </summary>
    /// <param name="comboBox">搜索框。</param>
    private static void ExitNavSearch(ComboBox comboBox)
    {
        comboBox.IsDropDownOpen = false;
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(comboBox), null);
    }

    /// <summary>
    /// 回车退出搜索用：退出前先撤掉"当前高亮项"和"选中项"，免得可编辑下拉框在失焦时
    /// 把高亮项的文字填回框里（就是"回车把 BB / BI 选上去"）。
    /// 注意点「添加」按钮不要用这个 —— 那也算"点搜索框外面"，撤掉选中项会导致添加失败。
    /// </summary>
    /// <param name="comboBox">搜索框。</param>
    private static void ExitNavSearchAndDropSelection(ComboBox comboBox)
    {
        comboBox.SelectedItem = null;
        if (comboBox.ItemsSource is { } source)
        {
            CollectionViewSource.GetDefaultView(source)?.MoveCurrentTo(null);
        }

        ExitNavSearch(comboBox);
    }

    /// <summary>
    /// 焦点离开搜索框时收起下拉。主要靠窗口级鼠标事件兜底（点到不可聚焦的地方键盘焦点根本不会移走），
    /// 这里是补充。
    /// </summary>
    /// <param name="sender">搜索框。</param>
    /// <param name="e">焦点事件参数。</param>
    private void NavTargetComboBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            comboBox.IsDropDownOpen = false;
        }
    }

    /// <summary>
    /// 「添加」按钮点完后把搜索框清空：和界面逻辑里"加完清空搜索词"一致。
    /// 打字过程中不敢动输入框内容（会打断输入法上屏、收起下拉），所以清空只能放在"加完"这个时机。
    /// <para>
    /// 注意：Button 的 <c>Click</c> 事件是在 <c>Command</c> 之前执行的，所以这里不能立刻清空 ——
    /// 那会把"要添加的目标"先清掉，AddSelectedNav 就再也拿不到目标（表现就是"添加不了"）。
    /// 放到 dispatcher 里，等 Command 跑完再清。
    /// </para>
    /// </summary>
    /// <param name="sender">按钮。</param>
    /// <param name="e">点击事件参数。</param>
    private void AddNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (NavTargetComboBox is null)
        {
            return;
        }

        NavTargetComboBox.Dispatcher.BeginInvoke(
            new System.Action(() => {
                NavTargetComboBox.SelectedItem = null;
                NavTargetComboBox.Text = string.Empty;
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    #endregion

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
