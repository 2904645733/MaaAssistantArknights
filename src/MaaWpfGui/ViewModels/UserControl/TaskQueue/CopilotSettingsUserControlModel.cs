// <copyright file="CopilotSettingsUserControlModel.cs" company="MaaAssistantArknights">
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

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using MaaWpfGui.Configuration.Single.MaaTask;
using MaaWpfGui.Helper;
using MaaWpfGui.Models;
using MaaWpfGui.Models.AsstTasks;
using MaaWpfGui.ViewModels.Items;
using MaaWpfGui.ViewModels.UI;
using Serilog;
using Stylet;
using static MaaWpfGui.Main.AsstProxy;

namespace MaaWpfGui.ViewModels.UserControl.TaskQueue;

/// <summary>
/// 主界面任务队列中的"战斗任务"设置模型。
/// 战斗任务 = 若干小任务，按顺序执行；列表可拖拽排序。
/// 小任务有两种：
/// ① 战斗（作业页快照）：点"记录当前作业页"生成，可"编辑"（把快照载入作业页修改）/"保存"（写回）。
/// ② 导航（章节入口）：选一个章节（第 0~17 章）添加，复用理智作战的章节导航（Episode{N}），只切页面、不开始战斗。
/// </summary>
public class CopilotSettingsUserControlModel : TaskSettingsViewModel, CopilotSettingsUserControlModel.ISerialize
{
    private static readonly ILogger _logger = Log.ForContext<CopilotSettingsUserControlModel>();

    static CopilotSettingsUserControlModel()
    {
        Instance = new();
    }

    public static CopilotSettingsUserControlModel Instance { get; }

    public CopilotSettingsUserControlModel()
    {
        Items.CollectionChanged += (_, _) => {
            OnPropertyChanged(nameof(HasItems));
            if (!_isRefreshing)
            {
                SaveItems();
            }
        };
        AdvancedItems.CollectionChanged += (_, _) => {
            if (!_isRefreshing && AdvancedOwner?.Model.Battle is { } battle)
            {
                battle.Jobs = [.. AdvancedItems.Select(i => i.Model)];
            }
        };

        // 导航下拉框的初始内容 = 全部目标（之后随输入实时过滤）
        RefreshNavFilteredOptions();
    }

    private bool _isRefreshing;

    /// <summary>
    /// Gets 小任务列表（顺序即执行顺序，可拖拽调整）。
    /// </summary>
    public ObservableCollection<CopilotSubTaskItem> Items { get; } = [];

    private string _statusMessage = string.Empty;

    /// <summary>
    /// Gets or sets 状态提示（仅错误/警告时显示）。
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set {
            SetAndNotify(ref _statusMessage, value);
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    /// <summary>
    /// Gets a value indicating whether 状态提示非空（控制状态栏显隐）。
    /// </summary>
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    private string _battleAddHint = string.Empty;

    /// <summary>
    /// Gets or sets 「添加自动战斗」右边那行小提示（点了按钮、但作业页里没作业可记录时显示）。
    /// </summary>
    public string BattleAddHint
    {
        get => _battleAddHint;
        set {
            SetAndNotify(ref _battleAddHint, value);
            OnPropertyChanged(nameof(HasBattleAddHint));
        }
    }

    /// <summary>
    /// Gets a value indicating whether 显示「添加自动战斗」旁边的小提示。
    /// </summary>
    public bool HasBattleAddHint => !string.IsNullOrEmpty(_battleAddHint);

    /// <summary>
    /// Gets a value indicating whether 列表非空（控制拖拽提示显隐）。
    /// </summary>
    public bool HasItems => Items.Count > 0;

    #region 导航小任务（章节选择器）

    /// <summary>
    /// 可导航的章节数量（第 0 章 ~ 第 17 章，对应核心 Episode0 ~ Episode17）。
    /// </summary>
    private const int NavChapterCount = 18;

    /// <summary>
    /// 活动入口（SideStory）：代号 → 中文名 → 该活动的关卡模式。
    /// 只列已有入口模板图的活动（resource/template/StageNavigation/SideStory/{代码}/{代码}@EnterSideStoryNew.png）。
    /// Modes 来自 D:\桌面\ss.xlsx 的 C 列：EX 全部活动都有，S 只有一部分（"EX" 或 "EXS"）；
    /// 空字符串表示这个活动暂时不给模式选项（叙拉古人 IS / 愚人号 SN 按需求先关掉）。
    /// </summary>
    private static readonly (string Code, string Name, string Modes)[] NavSideStories = [
        ("AD", "红丝绒", "EX"), ("AS", "太阳甩在身后", "EXS"), ("AT", "墟", "EXS"), ("BB", "巴别塔", "EXS"), ("BI", "风雪过境", "EX"),
        ("BP", "生路", "EX"), ("CB", "喧闹法则", "EX"), ("CW", "孤星", "EXS"), ("CV", "不义之财", "EX"), ("DH", "多索雷斯夏日", "EXS"),
        ("DM", "生于黑夜", "EXS"), ("DV", "绿野幻梦", "EX"), ("EA", "挽歌燃烧殆尽", "EX"), ("EP", "出苍白海", "EX"), ("FC", "照我以火", "EX"),
        ("GA", "吾导先路", "EX"), ("GO", "追迹日落以西", "EX"), ("GT", "骑兵与猎人", "EX"), ("HE", "空想花庭", "EX"), ("HS", "怀黍离", "EXS"),
        ("IC", "理想城 夏日狂欢季", "EXS"), ("IS", "叙拉古人", ""), ("IW", "将进酒", "EX"), ("LE", "尘影余音", "EX"), ("MB", "孤岛风云", "EX"),
        ("MN", "玛莉娅·临光", "EX"), ("MT", "众生行记", "EXS"), ("NL", "长夜临光", "EXS"), ("OF", "火蓝之心", "EXS"), ("OR", "相见欢", "EXS"),
        ("PV", "揭幕者们", "EXS"), ("RI", "密林悍将归来", "EX"), ("RS", "银心湖列车", "EX"), ("SL", "火山旅梦", "EXS"), ("SN", "愚人号", ""),
        ("SV", "覆潮之下", "EX"), ("TW", "沃伦姆德的薄暮", "EXS"), ("WB", "登临意", "EX"), ("WD", "遗尘漫步", "EX"), ("WR", "画中人", "EX"),
        ("ZT", "崔林特尔梅之金", "EXS"),
    ];

    /// <summary>
    /// 资源关（资源本）：关卡代号 → 产物文案的本地化键 → 理智作战里的关卡代号。
    /// 代号就是游戏里的两字母代号（芯片是两个字母-一个字母：PR-A / PR-B / PR-C / PR-D），和活动代号一个风格。
    /// 关卡代号列表的第一个就是理智作战那个资源关任务名（CE-6 / LS-6 / … / PR-A-1），核心直接跑它：
    /// 先点「资源」标签进资源关页面，再"认该产物的入口卡片，认不到就左滑/右滑"，但不会选具体关卡；
    /// 其余的（PR-A-2 这种同页面的另一关）只作为搜索别名。
    /// 产物文案沿用理智作战的 *Tip（"CE-6: 龙门币"），只取冒号后面的产物部分。
    /// </summary>
    private static readonly (string Code, string TipKey, string[] Stages)[] NavResourceStages = [
        ("CE", "CETip", ["CE-6"]),
        ("LS", "LSTip", ["LS-6"]),
        ("CA", "CATip", ["CA-5"]),
        ("AP", "APTip", ["AP-5"]),
        ("SK", "SKTip", ["SK-5"]),
        ("PR-A", "PR-ATip", ["PR-A-1", "PR-A-2"]),
        ("PR-B", "PR-BTip", ["PR-B-1", "PR-B-2"]),
        ("PR-C", "PR-CTip", ["PR-C-1", "PR-C-2"]),
        ("PR-D", "PR-DTip", ["PR-D-1", "PR-D-2"]),
    ];

    /// <summary>
    /// 从理智作战的产物文案里取"产物"部分："CE-6: 龙门币" → "龙门币"、"PR-A-1/2: 奶&amp;盾芯片" → "奶&amp;盾芯片"。
    /// 中英文冒号都认；万一没有冒号就整条当产物（不至于显示成空）。
    /// </summary>
    /// <param name="tip">理智作战的 *Tip 文案。</param>
    /// <returns>产物文本。</returns>
    private static string ResourceProduct(string tip)
    {
        var index = tip.IndexOfAny([':', '：']);
        return (index >= 0 ? tip[(index + 1)..] : tip).Trim();
    }

    /// <summary>
    /// Gets 导航目标列表：最上面是「剿灭作战」，然后是第 0~17 章，接着是活动入口
    /// （和游戏里"第 17 章往下就是活动"一致），最后是资源关（放在活动导航下面）。
    /// </summary>
    public List<NavChapterOption> NavChapterOptions { get; } = [
        new NavChapterOption(),
        .. Enumerable.Range(0, NavChapterCount).Select(i => new NavChapterOption(i)),
        .. NavSideStories.Select(s => new NavChapterOption(s.Code, s.Name, s.Modes)),
        .. NavResourceStages.Select(s => new NavChapterOption(
            s.Stages[0],
            $"{s.Code} {ResourceProduct(LocalizationHelper.GetString(s.TipKey))}",
            s.Stages)),
    ];

    private NavChapterOption? _selectedNavOption;

    /// <summary>
    /// Gets or sets 选择器里选中的目标（某一章 / 某个活动）。
    /// </summary>
    public NavChapterOption? SelectedNavOption
    {
        get => _selectedNavOption;
        set
        {
            SetAndNotify(ref _selectedNavOption, value);

            // 只有主线 10~14 章才显示「标准 / 磨难」，只有活动才显示「EX / S」
            OnPropertyChanged(nameof(ShowNavDifficulty));
            OnPropertyChanged(nameof(ShowNavMode));
        }
    }

    private string _navSearchText = string.Empty;

    /// <summary>
    /// Gets or sets 搜索框里的文本（显示用；"添加"按钮和回车也按它找目标）。
    /// 它和下拉列表的过滤是分开的：选好目标、或手动展开列表时会清掉过滤回到完整列表，
    /// 但框里的文字保持不动（和 MAA 原本那套可搜索下拉框一致）。
    /// </summary>
    public string NavSearchText
    {
        get => _navSearchText;
        set => SetAndNotify(ref _navSearchText, value);
    }

    /// <summary>下拉列表当前按什么过滤；空 = 显示全部目标。</summary>
    private string _navFilterKeyword = string.Empty;

    /// <summary>
    /// Gets 下拉框实际列出来的目标（内容随 <see cref="ApplyNavFilter"/> / <see cref="ClearNavFilter"/> 变化）：
    /// 过滤词为空时是全部，否则只留关键字匹配到的那几个（包含匹配、忽略空格，所以"巴别塔"能搜到"BB 巴别塔"）。
    /// 用 ObservableCollection 原地增删、不整体替换 ItemsSource —— 换 ItemsSource 会让 ComboBox 把
    /// 选中项/输入文本清掉；也不能整体 Clear 再 Add，那会把当前选中项从列表里删掉（表现就是"选不了"）。
    /// </summary>
    public ObservableCollection<NavChapterOption> NavFilteredOptions { get; } = [];

    /// <summary>
    /// 打字时调用：按输入过滤下拉列表（不展开也能边打边出结果）。
    /// </summary>
    /// <param name="keyword">输入的关键字。</param>
    public void ApplyNavFilter(string? keyword)
    {
        var text = keyword?.Trim() ?? string.Empty;
        if (string.Equals(_navFilterKeyword, text, StringComparison.Ordinal))
        {
            return;
        }

        _navFilterKeyword = text;
        RefreshNavFilteredOptions();
    }

    /// <summary>
    /// 选好一个目标、或者手动展开下拉列表时调用：清掉过滤，回到完整列表
    /// （不然下次展开只剩刚选的那一个）。
    /// </summary>
    public void ClearNavFilter() => ApplyNavFilter(string.Empty);

    /// <summary>
    /// 按当前过滤词重算下拉框内容（空 = 全部目标）：差量更新，只增删必要的项。
    /// </summary>
    private void RefreshNavFilteredOptions()
    {
        var wanted = string.IsNullOrEmpty(_navFilterKeyword)
            ? NavChapterOptions
            : FindNavMatches(_navFilterKeyword).ToList();

        if (wanted.Count == NavFilteredOptions.Count && wanted.SequenceEqual(NavFilteredOptions))
        {
            return;
        }

        foreach (var stale in NavFilteredOptions.Where(option => !wanted.Contains(option)).ToList())
        {
            NavFilteredOptions.Remove(stale);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var existing = NavFilteredOptions.IndexOf(wanted[i]);
            if (existing < 0)
            {
                NavFilteredOptions.Insert(i, wanted[i]);
            }
            else if (existing != i)
            {
                NavFilteredOptions.Move(existing, i);
            }
        }

        while (NavFilteredOptions.Count > wanted.Count)
        {
            NavFilteredOptions.RemoveAt(NavFilteredOptions.Count - 1);
        }

        _logger.Information("[NavSearch] 过滤「{Keyword}」→ 下拉列出 {Count} 个", _navFilterKeyword, wanted.Count);
    }

    /// <summary>
    /// 按关键字找导航目标（可能 0 个 / 1 个 / 多个），给"添加"按钮和搜索框回车用。
    /// </summary>
    /// <param name="keyword">搜索词。</param>
    /// <returns>匹配到的目标。</returns>
    public IReadOnlyList<NavChapterOption> FindNavMatches(string? keyword)
    {
        var text = keyword?.Trim();
        return string.IsNullOrEmpty(text)
            ? []
            : NavChapterOptions.Where(option => MatchesNavKeyword(option, text)).ToList();
    }

    private const string NavDifficultyNormal = "Normal";
    private const string NavDifficultyHard = "Hard";
    private const string NavModeEX = "EX";
    private const string NavModeS = "S";

    private string? _navDifficulty;

    /// <summary>
    /// Gets or sets 选中的难度（只对主线 10~14 章有效）："Normal" = 标准、"Hard" = 磨难；
    /// 两个都不勾选时为 null = 不切难度（点完「前往章节」这一步就结束）。
    /// </summary>
    public string? NavDifficulty
    {
        get => _navDifficulty;
        set
        {
            SetAndNotify(ref _navDifficulty, value);
            OnPropertyChanged(nameof(NavDifficultyIsNormal));
            OnPropertyChanged(nameof(NavDifficultyIsHard));
        }
    }

    private bool _navModeIsEX;

    /// <summary>
    /// Gets or sets a value indicating whether 选的是活动里的 EX 模式（和 S 互斥；都不勾选 = 普通关）。
    /// </summary>
    public bool NavModeIsEX
    {
        get => _navModeIsEX;
        set
        {
            SetAndNotify(ref _navModeIsEX, value);
            if (value)
            {
                // EX 和 S 是同一个活动里的两套关卡，只能落在一套上
                NavModeIsS = false;
            }
        }
    }

    private bool _navModeIsS;

    /// <summary>
    /// Gets or sets a value indicating whether 选的是活动里的 S 模式。
    /// </summary>
    public bool NavModeIsS
    {
        get => _navModeIsS;
        set
        {
            SetAndNotify(ref _navModeIsS, value);
            if (value)
            {
                NavModeIsEX = false;
            }
        }
    }

    /// <summary>
    /// Gets 当前选中的活动模式（两个都没勾选时为 null = 不切模式，进活动后就是普通关）。
    /// </summary>
    private string? SelectedNavMode => NavModeIsEX ? NavModeEX : NavModeIsS ? NavModeS : null;

    /// <summary>
    /// Gets a value indicating whether 当前选中的章节有「标准 / 磨难」两个模式（主线 10~14 章）。
    /// </summary>
    public bool ShowNavDifficulty => SelectedNavOption?.HasDifficulty == true;

    /// <summary>
    /// Gets a value indicating whether 当前选中的活动有关卡模式可选（EX / S）。
    /// </summary>
    public bool ShowNavMode => SelectedNavOption?.HasStageMode == true;

    /// <summary>
    /// Gets or sets a value indicating whether 选的是「标准」（和「磨难」互斥；两个都不勾 = 不切难度）。
    /// </summary>
    public bool NavDifficultyIsNormal
    {
        get => string.Equals(_navDifficulty, NavDifficultyNormal, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                NavDifficulty = NavDifficultyNormal;
            }
            else if (string.Equals(_navDifficulty, NavDifficultyNormal, StringComparison.OrdinalIgnoreCase))
            {
                // 取消勾选「标准」→ 两个都不勾（不切难度）；勾着「磨难」时取消「标准」不动「磨难」
                NavDifficulty = null;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether 选的是「磨难」（和「标准」互斥；两个都不勾 = 不切难度）。
    /// </summary>
    public bool NavDifficultyIsHard
    {
        get => string.Equals(_navDifficulty, NavDifficultyHard, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                NavDifficulty = NavDifficultyHard;
            }
            else if (string.Equals(_navDifficulty, NavDifficultyHard, StringComparison.OrdinalIgnoreCase))
            {
                NavDifficulty = null;
            }
        }
    }

    /// <summary>
    /// 难度在界面与日志里的名字（Hard → 磨难，其余 → 标准）。
    /// </summary>
    /// <param name="difficulty">"Hard" / "Normal" / null。</param>
    /// <returns>本地化的难度名。</returns>
    private static string DifficultyName(string? difficulty) =>
        string.Equals(difficulty, NavDifficultyHard, StringComparison.OrdinalIgnoreCase)
            ? LocalizationHelper.GetString("CopilotNavDifficultyHard")
            : LocalizationHelper.GetString("CopilotNavDifficultyNormal");

    /// <summary>
    /// 导航小任务在列表里的名字：活动是"代号 + 活动名"（带模式时再加（EX）/（S））；
    /// 10~14 章记录了难度时加上（标准 / 磨难）；剿灭导航带上从作业里读到的关卡名；
    /// 其余就是选项本身的显示文本。
    /// </summary>
    /// <param name="option">选中的目标。</param>
    /// <param name="difficulty">这一步记录的难度（老配置里可能为空）。</param>
    /// <param name="mode">这一步记录的活动模式（老配置里可能为空）。</param>
    /// <param name="annihilationStage">剿灭导航读到的关卡名（读不到时为空）。</param>
    /// <returns>用于显示的名字。</returns>
    private static string NavStepName(NavChapterOption option, string? difficulty, string? mode, string? annihilationStage = null)
    {
        if (option.IsAnnihilation)
        {
            // 读到了就直接用剿灭关卡名（如"龙门市区"），不用"剿灭作战（…）"这种前缀
            return string.IsNullOrEmpty(annihilationStage) ? option.Display : annihilationStage;
        }

        if (option.HasStageMode && !string.IsNullOrEmpty(mode))
        {
            return LocalizationHelper.GetStringFormat("CopilotNavActivityItemMode", option.Display, mode);
        }

        return option.HasDifficulty && !string.IsNullOrEmpty(difficulty)
            ? LocalizationHelper.GetStringFormat(
                "CopilotNavChapterItemDifficulty",
                option.Chapter,
                DifficultyName(difficulty))
            : option.Display;
    }

    /// <summary>
    /// 把选择器里选中的目标（剿灭作战 / 某一章 / 某个活动）添加成一个"导航"小任务。
    /// </summary>
    public void AddSelectedNav()
    {
        // 允许"打字搜索 → 直接点添加"：没在下拉框里点选时，用输入的文本找一个明确的目标
        var option = SelectedNavOption ?? ResolveNavOption(NavSearchText);
        if (option is null)
        {
            // 没选到目标：除了状态栏，也写一行日志（不然点了"添加"像是没反应）
            StatusMessage = LocalizationHelper.GetString("CopilotNavNeedPick");
            Instances.TaskQueueViewModel.AddLog(StatusMessage, MaaWpfGui.Constants.UiLogColor.Error);
            return;
        }

        // 10~14 章有标准 / 磨难，活动有 EX / S：把选中的记进这一步（没有的、没勾的都留空）
        var difficulty = option.HasDifficulty ? NavDifficulty : null;
        var mode = option.HasStageMode ? SelectedNavMode : null;

        var subTask = new CopilotSubTask {
            Kind = CopilotSubTaskKind.Nav,
            Name = NavStepName(option, difficulty, mode),
            NavChapter = option.Chapter ?? 0,
            NavSideStory = option.SideStory,
            NavAnnihilation = option.IsAnnihilation,
            NavResourceStage = option.ResourceStage,
            Difficulty = difficulty,
            Mode = mode,
        };

        Items.Add(new CopilotSubTaskItem(subTask));
        SaveItems();

        // 加完清空选择器状态，方便接着加下一个目标
        SelectedNavOption = null;
        NavSearchText = string.Empty;
        NavModeIsEX = false;
        NavModeIsS = false;
        NavDifficulty = null;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// 按输入文本找导航目标：完全一致优先，其次"只有一个候选"时也算
    /// （章节名、活动代号、活动名，以及资源关的代号/产物/完整关卡代号都能搜）。
    /// </summary>
    /// <param name="text">输入框里的搜索词。</param>
    /// <returns>能唯一确定的目标；不确定时返回 null。</returns>
    private NavChapterOption? ResolveNavOption(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var keyword = text.Trim();
        var matches = NavChapterOptions
            .Where(option => MatchesNavKeyword(option, keyword))
            .ToList();

        return matches.FirstOrDefault(option => string.Equals(option.Display, keyword, StringComparison.CurrentCultureIgnoreCase))
               ?? (matches.Count == 1 ? matches[0] : null);
    }

    /// <summary>
    /// 关键字能不能匹配这个导航目标：显示文本（"SL 火山旅梦"、"CE 龙门币"），
    /// 或资源关项的完整关卡代号别名（"CE-6"）。忽略空格的写法也算匹配
    /// （「火山旅梦」「SL火山旅梦」「火山 旅梦」都能搜到）。
    /// </summary>
    /// <param name="option">导航目标。</param>
    /// <param name="keyword">搜索词。</param>
    /// <returns>匹配则为 true。</returns>
    private static bool MatchesNavKeyword(NavChapterOption option, string keyword)
    {
        if (option.Display.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
            || option.SearchAliases.Any(alias => alias.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)))
        {
            return true;
        }

        var compact = Compact(keyword);
        return compact.Length > 0
               && (Compact(option.Display).Contains(compact, StringComparison.CurrentCultureIgnoreCase)
                   || option.SearchAliases.Any(alias => Compact(alias).Contains(compact, StringComparison.CurrentCultureIgnoreCase)));
    }

    /// <summary>
    /// 去掉空格、制表符和拼音隔音符号（输入法打 "ad" 会变成 "a'd"）：用于"忽略这些字符"的搜索
    /// （"SL 火山旅梦" → "SL火山旅梦"、"a'd" → "ad"）。
    /// </summary>
    /// <param name="text">原文本。</param>
    /// <returns>去掉空白字符和隔音符号的文本。</returns>
    private static string Compact(string text)
        => string.Concat(text.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('\'' or '’' or '‘')));

    #endregion

    /// <summary>
    /// Gets 高级设置中的作业列表（对应当前点开设置图标的小任务）。
    /// </summary>
    public ObservableCollection<CopilotJobItem> AdvancedItems { get; } = [];

    private CopilotSubTaskItem? _advancedOwner;

    /// <summary>
    /// Gets 当前正在高级设置中编辑的小任务（未选择时为 null）。
    /// </summary>
    public CopilotSubTaskItem? AdvancedOwner
    {
        get => _advancedOwner;
        private set {
            if (_advancedOwner is { } old)
            {
                old.PropertyChanged -= OnAdvancedOwnerPropertyChanged;
            }

            SetAndNotify(ref _advancedOwner, value);
            if (value is { } current)
            {
                current.PropertyChanged += OnAdvancedOwnerPropertyChanged;
            }

            OnPropertyChanged(nameof(HasAdvancedTarget));
            OnPropertyChanged(nameof(NoAdvancedTarget));
            OnPropertyChanged(nameof(AdvancedOwnerName));
        }
    }

    private void OnAdvancedOwnerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CopilotSubTaskItem.Name))
        {
            OnPropertyChanged(nameof(AdvancedOwnerName));
        }
    }

    /// <summary>
    /// Gets a value indicating whether 已选择要编辑的小任务。
    /// </summary>
    public bool HasAdvancedTarget => _advancedOwner is not null;

    /// <summary>
    /// Gets a value indicating whether 尚未选择小任务（高级设置显示提示）。
    /// </summary>
    public bool NoAdvancedTarget => _advancedOwner is null;

    /// <summary>
    /// Gets 当前高级设置对应的小任务名称（随重命名实时同步）。
    /// </summary>
    public string AdvancedOwnerName => AdvancedOwner?.Name ?? string.Empty;

    /// <summary>
    /// 点击小任务的"重命名"：进入名字编辑状态。
    /// </summary>
    public void RenameItem(CopilotSubTaskItem item)
    {
        if (item is null)
        {
            return;
        }

        item.IsEditing = true;
        EditModeEntered?.Invoke(item);
    }

    /// <summary>
    /// 名字编辑框获得焦点请求（供界面层聚焦使用）。
    /// </summary>
    public static event Action<CopilotSubTaskItem>? EditModeEntered;

    /// <summary>
    /// 点击小任务的设置图标：选中该小任务并切换到队列任务的"高级设置"面板。
    /// </summary>
    public void OpenAdvanced(CopilotSubTaskItem item)
    {
        if (item?.Model.Kind != CopilotSubTaskKind.Battle || item.Model.Battle is not { } battle)
        {
            StatusMessage = LocalizationHelper.GetString("CopilotEditNavNotReady");
            return;
        }

        // 重建列表期间抑制同步回调，避免清空时把该小任务的作业列表一起清掉
        _isRefreshing = true;
        AdvancedItems.Clear();
        foreach (var job in battle.Jobs)
        {
            AdvancedItems.Add(new CopilotJobItem(job));
        }

        _isRefreshing = false;

        AdvancedOwner = item;
        TaskSettingVisibilityInfo.Instance.EnableAdvancedSettings = true;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// 把高级设置中的某个作业载入作业页（与作业页列表的"载入"图标行为一致）。
    /// </summary>
    public void LoadJobToPage(CopilotJobItem item)
    {
        if (item is null)
        {
            return;
        }

        var page = Instances.CopilotViewModel;
        page.CopilotTabIndex = 0;
        page.Filename = item.Model.FilePath;
    }

    /// <summary>
    /// 从高级设置中删除某个作业。
    /// </summary>
    public void RemoveAdvancedJob(CopilotJobItem item)
    {
        if (item is null)
        {
            return;
        }

        AdvancedItems.Remove(item);
        if (AdvancedOwner?.Model.Battle is { } battle)
        {
            battle.Jobs = [.. AdvancedItems.Select(i => i.Model)];
        }
    }

    /// <summary>
    /// 高级设置作业拖拽排序（兼容旧调用）。
    /// </summary>
    public void MoveAdvancedJob(CopilotJobItem source, CopilotJobItem? target)
    {
        if (source is null || AdvancedOwner?.Model.Battle is not { } battle)
        {
            return;
        }

        var from = AdvancedItems.IndexOf(source);
        if (from < 0)
        {
            return;
        }

        var to = target is null ? AdvancedItems.Count - 1 : AdvancedItems.IndexOf(target);
        if (to < 0)
        {
            to = AdvancedItems.Count - 1;
        }

        if (from == to)
        {
            return;
        }

        AdvancedItems.Move(from, to);
        battle.Jobs = [.. AdvancedItems.Select(i => i.Model)];
    }

    #region 运行进度追踪（打完一关自动取消勾选，支持断点续跑）

    private sealed class RunInfo
    {
        public CopilotTask Task { get; init; } = null!;

        public CopilotSubTask SubTask { get; init; } = null!;

        public List<CopilotSnapshotJob> SentJobs { get; init; } = [];

        public int LastStarted { get; set; } = -1;

        /// <summary>Gets a value indicating whether 这条任务是「章尾剧情」那几条（战斗部分跑完才跑的那些）。</summary>
        public bool EndPlotRun { get; init; }

        /// <summary>
        /// Gets a value indicating whether 这条任务跑完时要把整个小任务标记为已完成。
        /// 勾了「章尾剧情」时，战斗部分跑完先不标记，等最后一条章尾剧情任务跑完才标记。
        /// </summary>
        public bool MarkStepDone { get; init; } = true;

        /// <summary>Gets 标记完成时写进日志的文案（空则用默认文案）。</summary>
        public string? DoneMessage { get; init; }
    }

    private static readonly Dictionary<int, RunInfo> _runMap = [];

    private static void RegisterRun(
        int taskId,
        CopilotTask task,
        CopilotSubTask subTask,
        List<CopilotSnapshotJob> sentJobs,
        bool endPlotRun = false,
        bool markStepDone = true,
        string? doneMessage = null)
    {
        _runMap[taskId] = new RunInfo {
            Task = task,
            SubTask = subTask,
            SentJobs = sentJobs,
            LastStarted = -1,
            EndPlotRun = endPlotRun,
            MarkStepDone = markStepDone,
            DoneMessage = doneMessage,
        };
    }

    /// <summary>
    /// 把一个小任务标记为已完成（取消勾选，可带一条日志）。列表里有界面包装时会一起刷新界面。
    /// </summary>
    private static void MarkStepDone(CopilotSubTask subTask, string? logMessage = null)
    {
        var item = Instance.Items.FirstOrDefault(i => ReferenceEquals(i.Model, subTask));
        if (item is not null)
        {
            item.IsChecked = false;
        }
        else
        {
            subTask.IsChecked = false;
        }

        if (!string.IsNullOrEmpty(logMessage))
        {
            Instances.TaskQueueViewModel.AddLog(logMessage, MaaWpfGui.Constants.UiLogColor.Success);
        }
    }

    private static string JobTitle(CopilotSnapshotJob job) =>
        string.IsNullOrEmpty(job.StageName) ? Path.GetFileName(job.FilePath) : job.StageName!;

    /// <summary>
    /// 核心开始执行某个作业时调用：把此前已开始的作业标记为已完成（取消勾选），并写日志。
    /// </summary>
    public static void HandleJobStarted(int taskId, int jobIndex)
    {
        if (!_runMap.TryGetValue(taskId, out var info))
        {
            return;
        }

        for (var i = 0; i < jobIndex && i < info.SentJobs.Count; i++)
        {
            info.SentJobs[i].IsChecked = false;
            Instances.TaskQueueViewModel.AddLog(
                LocalizationHelper.GetStringFormat("CopilotJobDone", JobTitle(info.SentJobs[i])),
                MaaWpfGui.Constants.UiLogColor.Success);
        }

        if (jobIndex >= 0 && jobIndex < info.SentJobs.Count)
        {
            Instances.TaskQueueViewModel.AddLog(
                LocalizationHelper.GetStringFormat("CopilotJobRunning", info.SubTask.Name, JobTitle(info.SentJobs[jobIndex])));
        }

        info.LastStarted = jobIndex;
    }

    /// <summary>
    /// 任务链结束时调用：把最后开始的作业标记为已完成，并把整条小任务标记为已完成（写日志）。
    /// 勾了「章尾剧情」时，战斗部分跑完先不标记，等最后一条章尾剧情任务跑完才标记。
    /// </summary>
    public static void HandleTaskFinished(int taskId)
    {
        if (!_runMap.Remove(taskId, out var info))
        {
            return;
        }

        if (info.EndPlotRun)
        {
            // 章尾剧情任务：只有这一组里的最后一条跑完，这一步才算完成
            if (info.MarkStepDone)
            {
                MarkStepDone(
                    info.SubTask,
                    info.DoneMessage ?? LocalizationHelper.GetStringFormat("CopilotSubDone", info.SubTask.Name));
            }

            return;
        }

        if (info.LastStarted >= 0 && info.LastStarted < info.SentJobs.Count)
        {
            info.SentJobs[info.LastStarted].IsChecked = false;
            Instances.TaskQueueViewModel.AddLog(
                LocalizationHelper.GetStringFormat("CopilotJobDone", JobTitle(info.SentJobs[info.LastStarted])),
                MaaWpfGui.Constants.UiLogColor.Success);
        }
        else if (info.SubTask.Kind == CopilotSubTaskKind.Battle && info.SentJobs.Count == 0)
        {
            // 轮到这一步时才发现没有可执行的作业：这时候才算它完成
            MarkStepDone(info.SubTask, LocalizationHelper.GetStringFormat("CopilotSubNoJob", info.SubTask.Name));
            return;
        }

        if (!info.MarkStepDone)
        {
            // 战斗部分跑完了，但这一步还勾着「章尾剧情」→ 先不取消勾选，等剧情跑完再标记
            if (info.LastStarted < 0 && info.SentJobs.Count == 1)
            {
                // 单作业模式的战斗任务没有"某个作业开始"的回调（那是多作业那套），这里补上取消勾选，
                // 免得剧情失败后重跑时又把这个作业打一遍
                info.SentJobs[0].IsChecked = false;
                Instances.TaskQueueViewModel.AddLog(
                    LocalizationHelper.GetStringFormat("CopilotJobDone", JobTitle(info.SentJobs[0])),
                    MaaWpfGui.Constants.UiLogColor.Success);
            }

            return;
        }

        // 这条小任务（不论战斗还是导航）已经跑完 → 取消勾选，中断后继续执行不会重复跑
        MarkStepDone(
            info.SubTask,
            info.DoneMessage ?? (info.SubTask.Kind == CopilotSubTaskKind.Nav
                ? NavDoneText(info.SubTask)
                : LocalizationHelper.GetStringFormat("CopilotSubDone", info.SubTask.Name)));
    }

    /// <summary>
    /// 任务链失败时调用：如果是战斗任务里的步骤（导航 / 作战 / 章尾剧情）失败就返回 true，
    /// 调用方据此停止整条战斗任务（后面的步骤不再执行）；别的任务链返回 false，保持原来的行为。
    /// 日志沿用界面本来就有的"任务出错"，只有"没认出剧情关"这种核心看不出来的情况才补一条短的。
    /// </summary>
    /// <param name="taskId">失败的任务 id。</param>
    /// <returns>是否是战斗任务里的步骤。</returns>
    public static bool HandleTaskFailed(int taskId)
    {
        if (!_runMap.Remove(taskId, out var info))
        {
            return false;
        }

        if (info.EndPlotRun)
        {
            Instances.TaskQueueViewModel.AddLog(
                LocalizationHelper.GetStringFormat("CopilotEndPlotFailed", info.SubTask.Name),
                MaaWpfGui.Constants.UiLogColor.Error);
        }

        return true;
    }

    /// <summary>
    /// 导航小任务完成的日志文本：章节写"已在第 N 章"（10~14 章带模式时写"已在第 N 章（标准/磨难）"），
    /// 活动写"已在&lt;活动中文名&gt;"（不带活动代号），剿灭导航写"已在剿灭作战（&lt;关卡名&gt;）"。
    /// </summary>
    /// <param name="sub">导航小任务。</param>
    /// <returns>日志文本。</returns>
    private static string NavDoneText(CopilotSubTask sub)
    {
        // 资源关导航：这一步的名字就是"CE-6: 龙门币"这种（沿用理智作战的产物文案），直接写进日志
        if (!string.IsNullOrEmpty(sub.NavResourceStage))
        {
            return LocalizationHelper.GetStringFormat("CopilotNavDoneSideStory", sub.Name);
        }

        // 剿灭导航：这一步的名字里已经带了关卡名（"剿灭作战（龙门市区）"），直接用
        if (sub.NavAnnihilation)
        {
            return LocalizationHelper.GetStringFormat("CopilotNavDoneAnnihilation", sub.Name);
        }

        if (!string.IsNullOrEmpty(sub.NavSideStory))
        {
            // 活动中文名：先按代号查活动表（NavSideStories），查不到再退回"步骤名去掉代号前缀"
            var name = NavSideStories.FirstOrDefault(s => s.Code == sub.NavSideStory).Name;
            if (string.IsNullOrEmpty(name))
            {
                var prefix = sub.NavSideStory + " ";
                name = sub.Name.StartsWith(prefix, StringComparison.Ordinal) ? sub.Name[prefix.Length..] : sub.Name;
            }

            // 选了 EX / S 模式时，日志里也写清楚落在哪一套关卡
            return string.IsNullOrEmpty(sub.Mode)
                ? LocalizationHelper.GetStringFormat("CopilotNavDoneSideStory", name)
                : LocalizationHelper.GetStringFormat("CopilotNavDoneSideStoryMode", name, sub.Mode);
        }

        // 10~14 章记录了难度时，日志里也写清楚是标准还是磨难
        if (!string.IsNullOrEmpty(sub.Difficulty))
        {
            return LocalizationHelper.GetStringFormat(
                "CopilotNavDoneChapterWithDifficulty",
                sub.NavChapter,
                DifficultyName(sub.Difficulty));
        }

        return LocalizationHelper.GetStringFormat("CopilotNavDone", sub.NavChapter);
    }

    #endregion

    /// <summary>
    /// 把作业页当前状态记录为一个新的"战斗"小任务。
    /// </summary>
    public void AddBattleFromPage()
    {
        if (!TryCaptureFromPage(out var snapshot, out var reasonKey))
        {
            // 就近显示在「添加自动战斗」右边，下面那条通用状态栏留给导航之类的提示
            BattleAddHint = reasonKey;
            return;
        }

        BattleAddHint = string.Empty;
        var subTask = new CopilotSubTask {
            Kind = CopilotSubTaskKind.Battle,
            Name = NextBattleName(),
            Battle = snapshot,
        };
        Items.Add(new CopilotSubTaskItem(subTask));
        SaveItems();
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// 编辑小任务：把快照载入作业页（列表 + 设置），供用户在作业页修改。
    /// </summary>
    public void EditItem(CopilotSubTaskItem item)
    {
        if (item?.Model.Kind != CopilotSubTaskKind.Battle || item.Model.Battle is not { } snapshot)
        {
            StatusMessage = LocalizationHelper.GetString("CopilotEditNavNotReady");
            return;
        }

        var page = Instances.CopilotViewModel;
        page.CopilotTabIndex = 0;

        // 只有 1 个作业的快照按"单作业"载入（不勾"多作业模式"），2 个及以上才用多作业列表 ——
        // 和记录时用的模式保持一致：单作业记录 → 编辑时也是单作业（改完"保存"回去仍是单作业）。
        page.UseCopilotList = snapshot.Jobs.Count > 1;

        // 先把作业页作业目录清空，再把这一步的作业复制进去（页面看到的就正好是这一步的作业）
        ClearCopilotJobDir();
        LoadSnapshotToPage(snapshot);

        // 老版本的编辑区（config\copilot_edit\）现在不用了：页面已经改指向 config\copilot\，顺手清掉没人引用的旧副本
        CleanupLegacyEditArea();

        // 右边日志里留一条（和自动肉鸽"选了开局干员"一样）：告诉用户去哪儿改、改完要更新。
        // 两边都写：任务队列页的日志（和自动肉鸽那条一致）＋ 自动战斗页自己的日志（切过去就能看见）
        var editLog = LocalizationHelper.GetStringFormat("CopilotEditLoadedLog", item.Name);
        Instances.TaskQueueViewModel.AddLog(editLog);
        Instances.CopilotViewModel.AddLog(editLog);

        // 顺手跳到「自动战斗」页，方便马上改（等于点左侧菜单切过去）
        if (System.Windows.Application.Current?.MainWindow?.DataContext is RootViewModel root)
        {
            root.ActiveItem = page;
        }
    }

    /// <summary>
    /// 保存小任务：把作业页当前状态（列表 + 设置）写回该小任务。
    /// </summary>
    public void SaveItem(CopilotSubTaskItem item)
    {
        if (item?.Model.Kind != CopilotSubTaskKind.Battle)
        {
            StatusMessage = LocalizationHelper.GetString("CopilotEditNavNotReady");
            return;
        }

        // 更新 = 先删掉这一步旧的快照文件，再从作业页复制新的过来。
        // 顺序很关键：同名文件先删掉，新副本才能叫原来的名字（否则只能退化成 xxx_2）；
        // 还被别的小任务 / 作业页引用着的文件会留着（DeleteSnapshotCopies 里有判断）。
        var oldBattle = item.Model.Battle;
        var oldPaths = oldBattle is { } old ? old.Jobs.Select(j => j.FilePath).ToList() : [];
        item.Model.Battle = null;
        DeleteSnapshotCopies(oldPaths);

        if (!TryCaptureFromPage(out var snapshot, out var reasonKey))
        {
            item.Model.Battle = oldBattle; // 没抓到作业：把这一步还原回去
            StatusMessage = reasonKey;
            return;
        }

        item.Model.Battle = snapshot;
        SaveItems();
        StatusMessage = LocalizationHelper.GetString("CopilotSaved");

        // 高级设置正开着这个小任务时，立即同步作业列表
        if (ReferenceEquals(AdvancedOwner, item))
        {
            _isRefreshing = true;
            AdvancedItems.Clear();
            foreach (var job in snapshot.Jobs)
            {
                AdvancedItems.Add(new CopilotJobItem(job));
            }

            _isRefreshing = false;
        }
    }

    /// <summary>
    /// 高级设置里的「清除」：把这一小任务的作业全部清掉（它自己的快照副本也按引用情况删掉）。
    /// </summary>
    /// <param name="item">要清除作业的小任务。</param>
    public void ClearItemJobs(CopilotSubTaskItem? item)
    {
        if (item?.Model.Kind != CopilotSubTaskKind.Battle || item.Model.Battle is not { } battle)
        {
            StatusMessage = LocalizationHelper.GetString("CopilotEditNavNotReady");
            return;
        }

        var oldPaths = battle.Jobs.Select(j => j.FilePath).ToList();
        battle.Jobs = [];

        // 高级设置正开着它 → 列表跟着清空（用 _isRefreshing 抑制回写，避免又把旧的写回去）
        if (ReferenceEquals(AdvancedOwner, item))
        {
            _isRefreshing = true;
            AdvancedItems.Clear();
            _isRefreshing = false;
        }

        DeleteSnapshotCopies(oldPaths);
        SaveItems();
        Instances.TaskQueueViewModel.AddLog(LocalizationHelper.GetStringFormat("CopilotJobsCleared", item.Name));
    }

    /// <summary>
    /// 生成不与现有小任务重名的默认名（战斗1、战斗2…）：编号只数战斗小任务，插了导航步骤也不会带偏。
    /// </summary>
    private string NextBattleName()
    {
        var prefix = LocalizationHelper.GetString("CopilotSubBattle");
        var index = Items.Count(i => i.IsBattle) + 1;
        string candidate;
        do
        {
            candidate = prefix + index++;
        }
        while (Items.Any(i => string.Equals(i.Name, candidate, StringComparison.Ordinal)));

        return candidate;
    }

    /// <summary>
    /// 老版本的默认名是"战斗-1"，现在改成"战斗1"：读旧配置时统一一下（自定义的名字原样保留）。
    /// </summary>
    private static string NormalizeBattleName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name ?? string.Empty;
        }

        var prefix = LocalizationHelper.GetString("CopilotSubBattle");
        var withDash = prefix + "-";
        if (name.StartsWith(withDash, StringComparison.Ordinal) && int.TryParse(name.AsSpan(withDash.Length), out _))
        {
            return prefix + name[withDash.Length..];
        }

        return name;
    }

    /// <summary>
    /// 删除一个小任务。
    /// </summary>
    public void DeleteItem(CopilotSubTaskItem item)
    {
        Items.Remove(item);
        if (item.Model.Battle is { } battle)
        {
            // 这一步没了，它专用的作业副本也一起清掉（本任务里还有别的小任务引用时不会删）
            DeleteSnapshotCopies(battle.Jobs.Select(j => j.FilePath));
        }

        SaveItems();
    }

    /// <summary>
    /// 收集"正在被使用"的作业文件，统一转成绝对路径再比较（相对 / 绝对对不上会导致该保护的没保护住）：
    /// 所有小任务引用的 + 作业页当前列表 / 输入框指向的。
    /// </summary>
    private static HashSet<string> CollectReferencedJobFiles()
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                referenced.Add(Path.GetFullPath(Path.Combine(PathsHelper.BaseDir, path)));
            }
        }

        foreach (var step in Instance.Items)
        {
            if (step.Model.Battle is not { } battle)
            {
                continue;
            }

            foreach (var job in battle.Jobs)
            {
                Add(job.FilePath);
            }
        }

        // 作业页当前正在用的文件也算"在用"：更新时如果先把页面指向的文件删了，就抓不到作业了
        foreach (var pageJob in Instances.CopilotViewModel.CopilotItemViewModels)
        {
            Add(pageJob.FilePath);
        }

        Add(Instances.CopilotViewModel.Filename);
        return referenced;
    }

    /// <summary>
    /// 删除作业快照副本。只删 config\copilot_snapshot\ 目录内、且"没有任何小任务 / 作业页引用"的文件，
    /// 绝不碰作业页下载的 config\copilot\。删完顺手把没人引用的副本也扫一遍。
    /// </summary>
    /// <param name="paths">候选路径（一般是这个小任务保存前的旧作业）。</param>
    private static void DeleteSnapshotCopies(IEnumerable<string> paths)
    {
        var referenced = CollectReferencedJobFiles();
        var snapshotDir = Path.Combine(PathsHelper.BaseDir, "config", "copilot_snapshot");
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var absolute = Path.GetFullPath(Path.Combine(PathsHelper.BaseDir, path));
            if (referenced.Contains(absolute) || !absolute.StartsWith(snapshotDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(absolute))
            {
                continue;
            }

            try
            {
                File.Delete(absolute);
            }
            catch (IOException)
            {
                // 删不掉就算了（文件被占用等情况）
            }
        }

        SweepUnusedSnapshotCopies();
    }

    /// <summary>
    /// 清掉快照目录里已经没有任何小任务 / 作业页引用的文件
    /// （历史版本反复"删除再更新"会攒下 xxx_2、xxx_3、xxx_4 这类副本）。
    /// </summary>
    private static void SweepUnusedSnapshotCopies()
    {
        var snapshotDir = Path.Combine(PathsHelper.BaseDir, "config", "copilot_snapshot");
        if (!Directory.Exists(snapshotDir))
        {
            return;
        }

        var referenced = CollectReferencedJobFiles();
        foreach (var file in Directory.EnumerateFiles(snapshotDir))
        {
            if (referenced.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // 删不掉就算了（被占用等情况）
            }
        }
    }

    /// <summary>
    /// 修历史遗留的坏快照：老版本的「更新」会误删自己刚引用上的快照副本，
    /// 而「作业」页的同名作业通常还在（config\copilot\），缺了就补一份回来。
    /// </summary>
    private static void RepairMissingSnapshotCopies()
    {
        var snapshotDir = Path.Combine(PathsHelper.BaseDir, "config", "copilot_snapshot");
        var sourceDir = Path.Combine(PathsHelper.BaseDir, "config", "copilot");
        var repaired = new List<string>();

        foreach (var step in Instance.Items)
        {
            if (step.Model.Battle is not { } battle)
            {
                continue;
            }

            foreach (var job in battle.Jobs)
            {
                if (string.IsNullOrWhiteSpace(job.FilePath))
                {
                    continue;
                }

                var snapshotPath = Path.Combine(PathsHelper.BaseDir, job.FilePath);
                if (!snapshotPath.StartsWith(snapshotDir, StringComparison.OrdinalIgnoreCase) || File.Exists(snapshotPath))
                {
                    continue;
                }

                var fallback = Path.Combine(sourceDir, Path.GetFileName(snapshotPath));
                if (!File.Exists(fallback))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(snapshotDir);
                    File.Copy(fallback, snapshotPath);
                    repaired.Add(Path.GetFileName(snapshotPath));
                }
                catch (IOException)
                {
                    // 复制不了就算了，序列化时照旧提示"作业文件不存在"
                }
            }
        }

        if (repaired.Count > 0)
        {
            Instances.TaskQueueViewModel.AddLog(LocalizationHelper.GetStringFormat(
                "CopilotSnapshotRepaired",
                string.Join("、", repaired.Distinct(StringComparer.OrdinalIgnoreCase))));
        }
    }

    /// <summary>
    /// 拖拽排序：把 source 移到 target 的位置（target 为空则移到末尾）。
    /// </summary>
    public void MoveItem(CopilotSubTaskItem source, CopilotSubTaskItem? target)
    {
        if (source is null)
        {
            return;
        }

        var from = Items.IndexOf(source);
        if (from < 0)
        {
            return;
        }

        var to = target is null ? Items.Count - 1 : Items.IndexOf(target);
        if (to < 0)
        {
            to = Items.Count - 1;
        }

        if (from == to)
        {
            return;
        }

        Items.Move(from, to);
        SaveItems();
    }

    /// <summary>
    /// 小任务名编辑完成时保存（名称经双向绑定已写入模型，这里持久化列表）。
    /// </summary>
    public void SaveName()
    {
        foreach (var item in Items)
        {
            item.IsEditing = false;
        }

        SaveItems();
    }

    private bool TryCaptureFromPage(out CopilotBattleSnapshot? snapshot, out string reasonKey)
    {
        snapshot = null;
        var page = Instances.CopilotViewModel;
        if (page.CopilotTabIndex != 0)
        {
            // 保全派驻（1）/ 悖论模拟（2）是另一种任务类型，战斗小任务只支持普通作战
            reasonKey = LocalizationHelper.GetString("CopilotNeedMultiMode");
            return false;
        }

        // 「作业」页有两种用法，都要能记录成战斗小任务：
        //  ① 勾了"多作业模式"（列表）：记列表里所有勾选的作业；
        //  ② 没勾（单作业）：记当前页面上那一个作业（本地文件，或从作业站载入时页面写的临时文件）。
        //     以前这里写死了必须多作业模式，所以单作业时点"记录当前作业页"会直接报错。
        List<CopilotSnapshotJob> picked;
        if (page.UseCopilotList)
        {
            picked = [.. page.CopilotItemViewModels.Where(i => i.IsChecked).Select(job => new CopilotSnapshotJob {
                FilePath = job.FilePath,
                IsRaid = job.IsRaid,
                StageName = job.IsNavNameOverride ? job.Name : null,
            })];
        }
        else if (page.TryGetSingleJobFilePath(out var singleFilePath))
        {
            // 单作业没有"突袭"开关：IsRaid 留 false（等于不覆盖），核对照作业文件自己的 difficulty 走，
            // 和直接在作业页点"开始"完全一致
            picked = [new CopilotSnapshotJob { FilePath = singleFilePath! }];
        }
        else
        {
            picked = [];
        }

        if (picked.Count == 0)
        {
            reasonKey = LocalizationHelper.GetString("CopilotNeedMultiMode");
            return false;
        }

        // 快照只记作业文件路径，文件必须真的存在，否则核心加载作业会失败（界面只会显示"添加任务失败"）
        var missingJob = picked.FirstOrDefault(job => !JobFileExists(job.FilePath));
        if (missingJob is not null)
        {
            reasonKey = LocalizationHelper.GetStringFormat("CopilotJobFileMissing", JobTitle(missingJob), missingJob.FilePath);
            return false;
        }

        snapshot = new CopilotBattleSnapshot {
            // 记下「作业」页当时是哪种模式：单作业就按单作业下发（不启用多作业那套"找关卡"），
            // 多作业列表就按列表下发 —— 和点自动战斗的"开始"效果一致
            SingleJob = !page.UseCopilotList,
            Formation = page.Form,
            SupportUnitUsage = page.UseSupportUnitUsage ? (int)page.SupportUnitUsage : 0,
            AddTrust = page.AddTrust,
            IgnoreRequirements = page.IgnoreRequirements,
            UseSanityPotion = page.UseSanityPotion,
            UseStone = page.UseStone,
            FormationIndex = page.UseFormation ? page.FormationIndex : 0,
            LoopTimes = page.Loop ? Math.Max(1, page.LoopTimes) : 1,
            UserAdditionals = page.AddUserAdditional
                ? [.. page.GetUserAdditionals().Select(op => new CopilotSnapshotUserAdditional {
                    Name = op.Name,
                    Skill = op.Skill,
                    Module = op.Module,
                })]
                : [],
            Jobs = [.. picked.Select(job => new CopilotSnapshotJob {
                FilePath = CopyJobToSnapshot(job.FilePath),
                IsRaid = job.IsRaid,
                StageName = job.StageName,
            })],
        };
        reasonKey = string.Empty;
        return true;
    }

    /// <summary>
    /// 判断作业文件是否真的在磁盘上。作业路径一般形如 config\copilot\xxx.json（相对 MAA 用户目录），核心按用户目录解析。
    /// </summary>
    private static bool JobFileExists(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        return File.Exists(filePath) || File.Exists(Path.Combine(PathsHelper.BaseDir, filePath));
    }

    /// <summary>
    /// 快照专用目录：把作业页当前的作业文件复制一份进来。
    /// 同名文件只有在**内容完全一样**时才复用（例如"6-2 普通"和"6-2 突袭"两条记录指向同一个作业文件，
    /// 这样列表里显示的名字就不会退化成 xxx_2）；内容不同就新建一份（xxx_2、xxx_3 …），
    /// 所以同名作业改了内容照样能同步进来（旧的副本由 SaveItem 先删掉）。
    /// </summary>
    private static string CopyJobToSnapshot(string filePath)
    {
        var source = File.Exists(filePath) ? filePath : Path.Combine(PathsHelper.BaseDir, filePath);
        var relativeDir = Path.Combine("config", "copilot_snapshot");
        var absoluteDir = Path.Combine(PathsHelper.BaseDir, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var name = Path.GetFileName(source);
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var target = Path.Combine(absoluteDir, name);
        for (var i = 2; File.Exists(target) && !SameFileContent(target, source); i++)
        {
            target = Path.Combine(absoluteDir, stem + "_" + i + extension);
        }

        if (!File.Exists(target))
        {
            File.Copy(source, target);
        }

        return Path.Combine(relativeDir, Path.GetFileName(target));
    }

    /// <summary>
    /// 「编辑」时把这一小任务的作业复制一份到 config\copilot\（作业页的作业目录）给页面用。
    /// 同名文件不覆盖：内容一样就直接复用，内容不同才换个名字（xxx_2.json），
    /// 免得把你为同一关下载的另一个作业顶掉。
    /// </summary>
    private static string CopyJobForPage(string filePath)
    {
        var source = File.Exists(filePath) ? filePath : Path.Combine(PathsHelper.BaseDir, filePath);
        var relativeDir = Path.Combine("config", "copilot");
        var absoluteDir = Path.Combine(PathsHelper.BaseDir, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var name = Path.GetFileName(source);
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var target = Path.Combine(absoluteDir, name);
        for (var i = 2; File.Exists(target) && !SameFileContent(target, source); i++)
        {
            target = Path.Combine(absoluteDir, stem + "_" + i + extension);
        }

        if (!File.Exists(target))
        {
            File.Copy(source, target);
        }

        return Path.Combine(relativeDir, Path.GetFileName(target));
    }

    private static bool SameFileContent(string left, string right)
    {
        try
        {
            return File.ReadAllText(left) == File.ReadAllText(right);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// 点「编辑」时先把作业页的作业目录清空（config\copilot\ 整个删掉），
    /// 再把这一小任务的作业复制进去 —— 这样页面看到的就正好是这一步的作业，名字也干净。
    /// </summary>
    private static void ClearCopilotJobDir()
    {
        var sourceDir = Path.Combine(PathsHelper.BaseDir, "config", "copilot");
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        try
        {
            Directory.Delete(sourceDir, recursive: true);
        }
        catch (IOException)
        {
            // 删不掉（被占用）就算了：后面的复制遇到同名会加后缀
        }
    }

    /// <summary>
    /// 清掉老版本留下的编辑区目录（config\copilot_edit\）：现在编辑直接复制到 config\copilot\，不用它了。
    /// 只删"没有任何作业页 / 小任务引用"的文件，删空后把目录也去掉。
    /// </summary>
    private static void CleanupLegacyEditArea()
    {
        var absoluteDir = Path.Combine(PathsHelper.BaseDir, "config", "copilot_edit");
        if (!Directory.Exists(absoluteDir))
        {
            return;
        }

        var referenced = CollectReferencedJobFiles();
        foreach (var file in Directory.EnumerateFiles(absoluteDir))
        {
            if (referenced.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // 删不掉就算了
            }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(absoluteDir).Any())
            {
                Directory.Delete(absoluteDir);
            }
        }
        catch (IOException)
        {
            // 目录里还有文件（被别人引用着）就留着
        }
    }

    private static void LoadSnapshotToPage(CopilotBattleSnapshot snapshot)
    {
        var page = Instances.CopilotViewModel;

        page.Form = snapshot.Formation;
        page.UseSupportUnitUsage = snapshot.SupportUnitUsage != 0;
        page.SupportUnitUsage = (CopilotViewModel.CopilotSupportMode)snapshot.SupportUnitUsage;
        page.AddTrust = snapshot.AddTrust;
        page.IgnoreRequirements = snapshot.IgnoreRequirements;
        page.UseSanityPotion = snapshot.UseSanityPotion;
        page.UseStone = snapshot.UseStone;
        page.UseFormation = snapshot.FormationIndex != 0;
        page.FormationIndex = snapshot.FormationIndex;

        // 只有 1 个作业（或没有作业）：载入到页面的"单作业"位置（输入框），
        // 多作业列表保持原样不动 —— 免得为了编辑一个单作业快照，把用户自己的多作业列表清空。
        if (snapshot.Jobs.Count <= 1)
        {
            // 单作业模式下和自动战斗一起生效的开关也回填，改完"保存"才不会丢
            page.Loop = snapshot.LoopTimes > 1;
            page.LoopTimes = Math.Max(1, snapshot.LoopTimes);
            page.AddUserAdditional = snapshot.UserAdditionals.Count > 0;
            page.UserAdditional = [.. snapshot.UserAdditionals.Select(op => new AsstCopilotTask.UserAdditional {
                Name = op.Name,
                Skill = op.Skill,
                Module = op.Module,
            })];
            page.Filename = snapshot.Jobs.Count == 1 ? ResolveJobPath(CopyJobForPage(snapshot.Jobs[0].FilePath)) : string.Empty;
            return;
        }

        // 2 个及以上：整个列表就是这个快照的内容，清空后照快照重建
        page.CopilotItemViewModels.Clear();
        var index = 0;
        foreach (var job in snapshot.Jobs)
        {
            var useOverride = !string.IsNullOrEmpty(job.StageName);
            var name = useOverride ? job.StageName! : Path.GetFileNameWithoutExtension(job.FilePath);
            page.CopilotItemViewModels.Add(new CopilotItemViewModel(name, CopyJobForPage(job.FilePath), job.IsRaid, 0, true, useOverride) { Index = index++ });
        }

        page.SaveCopilotTask();
    }

    /// <summary>
    /// 快照里存的是相对路径（config\copilot_snapshot\xxx.json）。单作业模式要把文件真的载入页面，
    /// 所以这里按需补成绝对路径，免得工作目录不是程序目录时读不到（文件已存在或是绝对路径时原样返回）。
    /// </summary>
    /// <param name="filePath">快照里的作业路径。</param>
    /// <returns>可用于载入的路径。</returns>
    private static string ResolveJobPath(string filePath)
        => File.Exists(filePath) ? filePath : Path.Combine(PathsHelper.BaseDir, filePath);

    private void SaveItems()
    {
        // 列表、勾选、作业一有变化就把「剿灭导航要切的关卡名」重新算一遍（读它后面作战小任务的作业）
        RefreshAnnihilationStages();
        SetTaskConfig<CopilotTask>(_ => false, t => t.SubTasks = [.. Items.Select(i => i.Model)]);
    }

    /// <summary>
    /// 刷新「剿灭导航」要切的剿灭关卡名：读它后面第一个作战小任务里的作业（优先勾选的作业，
    /// 其次第一个作业）。读到了界面显示 ✓，读不到显示 ✗（下发任务时会跳过这一步并写错误日志）。
    /// 关卡名同时写进步骤名（如"剿灭作战（龙门市区）"），列表和运行日志里都能直接看到切到哪一关。
    /// </summary>
    private void RefreshAnnihilationStages()
    {
        var annihilationOption = NavChapterOptions.FirstOrDefault(o => o.IsAnnihilation);

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Model is not { Kind: CopilotSubTaskKind.Nav, NavAnnihilation: true } model)
            {
                continue;
            }

            model.NavAnnihilationStage =
                CopilotAnnihilationNavHelper.ResolveAfter(Items.Skip(i + 1).Select(item => item.Model));

            if (annihilationOption is not null)
            {
                model.Name = NavStepName(annihilationOption, null, null, model.NavAnnihilationStage);
            }

            Items[i].RefreshAnnihilationStage();
        }
    }

    public override void RefreshUI(BaseTask baseTask)
    {
        if (baseTask is CopilotTask copilot)
        {
            _isRefreshing = true;
            Items.Clear();
            foreach (var sub in copilot.SubTasks)
            {
                // 导航步骤的名字统一成"活动代号 + 活动名"（如 "GO It's my GO"）：
                // 老配置里存的是没带代号的名字，读出来时按当前表刷新一下，免得列表里显示不一致
                if (sub.Kind == CopilotSubTaskKind.Nav)
                {
                    var option = sub.NavAnnihilation
                        ? NavChapterOptions.FirstOrDefault(o => o.IsAnnihilation)
                        : !string.IsNullOrEmpty(sub.NavResourceStage)
                            ? NavChapterOptions.FirstOrDefault(o => o.ResourceStage == sub.NavResourceStage)
                            : string.IsNullOrEmpty(sub.NavSideStory)
                                ? NavChapterOptions.FirstOrDefault(o => o.Chapter == sub.NavChapter)
                                : NavChapterOptions.FirstOrDefault(o => o.SideStory == sub.NavSideStory);
                    if (option is not null)
                    {
                        sub.Name = NavStepName(option, sub.Difficulty, sub.Mode, sub.NavAnnihilationStage);
                    }
                }
                else if (sub.Kind == CopilotSubTaskKind.Battle)
                {
                    sub.Name = NormalizeBattleName(sub.Name);
                }

                Items.Add(new CopilotSubTaskItem(sub));
            }

            AdvancedItems.Clear();
            AdvancedOwner = null;
            _isRefreshing = false;

            // 老版本的「更新」会误删自己引用上的快照副本：读配置时顺手从「作业」页的同名作业补回来
            RepairMissingSnapshotCopies();

            // 顺手清掉没人引用的快照副本（历史版本反复更新会攒下 xxx_2、xxx_3、xxx_4 …）
            SweepUnusedSnapshotCopies();

            // 剿灭导航的关卡名每次读配置都重新解析一遍（作业可能已经换过了）
            RefreshAnnihilationStages();

            StatusMessage = string.Empty;
            Refresh();
        }
    }

    public override (bool? IsSuccess, IEnumerable<int> TaskId) SerializeTask(BaseTask? baseTask, int? taskId = null) => (this as ISerialize).Serialize(baseTask, taskId);

    private interface ISerialize : ITaskQueueModelSerialize
    {
        (bool? IsSuccess, IEnumerable<int> TaskId) ITaskQueueModelSerialize.Serialize(BaseTask? baseTask, int? taskId)
        {
            if (baseTask is not CopilotTask copilot)
            {
                return (null, []);
            }

            // 按列表顺序展开成一条执行链：勾选的战斗小任务 → Copilot 任务；勾选的导航小任务 → ChapterNavigation 任务
            var chain = new List<(CopilotSubTask Model, List<CopilotSnapshotJob> Jobs)>();
            var skipped = new List<string>();
            foreach (var sub in copilot.SubTasks)
            {
                if (!sub.IsChecked)
                {
                    continue;
                }

                if (sub.Kind == CopilotSubTaskKind.Battle && sub.Battle is { } battle)
                {
                    var checkedJobs = battle.Jobs.Where(j => j.IsChecked).ToList();
                    var usableJobs = checkedJobs.Where(j => JobFileExists(j.FilePath)).ToList();
                    foreach (var lost in checkedJobs.Where(j => !JobFileExists(j.FilePath)))
                    {
                        skipped.Add(LocalizationHelper.GetStringFormat("CopilotJobFileMissingShort", sub.Name, lost.FilePath));
                    }

                    // 没有可执行的作业（一个都没勾选 / 作业文件都没了）：不提前取消勾选，
                    // 而是按顺序占位——轮到这一步时立刻完成，由 HandleTaskFinished 判定并取消勾选
                    chain.Add((sub, usableJobs));
                    continue;
                }

                // 导航小任务：后面没有战斗小任务也要执行
                if (sub.Kind == CopilotSubTaskKind.Nav)
                {
                    chain.Add((sub, []));
                }
            }

            // 剿灭导航要切的关卡名，下发前再解析一次（用户可能刚改过后面的作业，列表还没触发保存）
            CopilotSettingsUserControlModel.Instance.RefreshAnnihilationStages();

            if (skipped.Count > 0)
            {
                // 这里的代码在嵌套接口内部，访问实例属性要走单例
                CopilotSettingsUserControlModel.Instance.StatusMessage = string.Join(Environment.NewLine, skipped);
                foreach (var message in skipped)
                {
                    Instances.TaskQueueViewModel.AddLog(message, MaaWpfGui.Constants.UiLogColor.Error);
                }
            }

            if (chain.Count == 0)
            {
                // 没有可执行的步骤：作为"跳过"处理（例如全部关卡都已打完、作业文件都不见了），避免报添加失败
                return (null, []);
            }

            if (taskId is int id && id > 0)
            {
                // 重新下发参数只能作用于已追加的单个任务：链上只有一步且是战斗时才支持
                // （勾了「章尾剧情」时后面还跟着剧情任务，重新下发改不到它们，直接不支持）
                if (chain.Count != 1 || chain[0].Model.Kind != CopilotSubTaskKind.Battle || chain[0].Model.EndPlot)
                {
                    return (false, []);
                }

                CopilotSettingsUserControlModel.RegisterRun(id, copilot, chain[0].Model, chain[0].Jobs);
                return (Instances.AsstProxy.AsstSetTaskParamsEncoded(id, BuildAsstTask(chain[0].Model.Battle!, chain[0].Jobs)), [id]);
            }

            var taskIds = new List<int>();
            foreach (var (model, jobs) in chain)
            {
                if (model.Kind == CopilotSubTaskKind.Nav)
                {
                    if (!string.IsNullOrEmpty(model.NavResourceStage))
                    {
                        // 资源关导航 → 核心的 ChapterNavigation 任务（resource_stage）：
                        // 点「资源」标签进资源关页面 → 认该产物的入口卡片（认不到就左滑/右滑），
                        // 停在关卡列表 —— 不选具体关卡
                        var (resourceSuccess, resourceId) = Instances.AsstProxy.AsstAppendTaskWithEncoding(
                            TaskType.Copilot,
                            new AsstChapterNavigationTask { ResourceStage = model.NavResourceStage });
                        if (!resourceSuccess || resourceId <= 0)
                        {
                            return (false, []);
                        }

                        taskIds.Add(resourceId);
                        CopilotSettingsUserControlModel.RegisterRun(resourceId, copilot, model, []);
                        continue;
                    }

                    if (model.NavAnnihilation)
                    {
                        // 剿灭导航 → 核心的 ChapterNavigation 任务（annihilation_stage）：
                        // 剿灭标签 → 进入 → 左上角返回 → 右下角切换 → 在关卡列表里 OCR 认出关卡名并点击。
                        // 关卡名来自"这一步后面第一个作战任务的作业"，读不到就不知道该切哪一关：
                        // 写一条明确的错误日志后跳过这一步（列表里那一步的图标也会是 ✗）。
                        if (string.IsNullOrEmpty(model.NavAnnihilationStage))
                        {
                            var message = LocalizationHelper.GetStringFormat("CopilotNavAnnihilationNoStage", model.Name);
                            CopilotSettingsUserControlModel.Instance.StatusMessage = message;
                            Instances.TaskQueueViewModel.AddLog(message, MaaWpfGui.Constants.UiLogColor.Error);
                            continue;
                        }

                        var (annihilationSuccess, annihilationId) = Instances.AsstProxy.AsstAppendTaskWithEncoding(
                            TaskType.Copilot,
                            new AsstChapterNavigationTask { AnnihilationStage = model.NavAnnihilationStage });
                        if (!annihilationSuccess || annihilationId <= 0)
                        {
                            return (false, []);
                        }

                        taskIds.Add(annihilationId);
                        CopilotSettingsUserControlModel.RegisterRun(annihilationId, copilot, model, []);
                        continue;
                    }

                    // 导航小任务 → 核心的 ChapterNavigation 任务：
                    // 就是理智作战那套（StageBegin 进入选关界面 + Episode{N} 章节导航），只是目标换成"某一章"
                    var (navSuccess, navId) = Instances.AsstProxy.AsstAppendTaskWithEncoding(
                        TaskType.Copilot,
                        string.IsNullOrEmpty(model.NavSideStory)
                            ? new AsstChapterNavigationTask {
                                Chapter = Math.Clamp(model.NavChapter, 0, NavChapterCount - 1),
                                Difficulty = model.Difficulty,
                            }
                            : new AsstChapterNavigationTask {
                                SideStory = model.NavSideStory,
                                Mode = model.Mode,
                            });
                    if (!navSuccess || navId <= 0)
                    {
                        return (false, []);
                    }

                    taskIds.Add(navId);
                    CopilotSettingsUserControlModel.RegisterRun(navId, copilot, model, []);
                    continue;
                }

                // jobs 为空 = 这一步没有可执行的作业：下发一个空任务占位（核心会立刻"通过"），
                // 轮到它时才算完成、再取消勾选
                var (isSuccess, appendedId) = jobs.Count == 0
                    ? Instances.AsstProxy.AsstAppendTaskWithEncoding(TaskType.Copilot, new AsstCustomTask { CustomTasks = [] })
                    : Instances.AsstProxy.AsstAppendTaskWithEncoding(TaskType.Copilot, BuildAsstTask(model.Battle!, jobs));
                if (!isSuccess || appendedId <= 0)
                {
                    return (false, []);
                }

                // 勾了「章尾剧情」：作业打完后还要往章节末尾滑地图找剧情关、点进去播完；
                // 这个次数做完这一步才算完成，所以战斗这条先不标记完成，交给最后一条剧情任务去标记。
                var endPlotTimes = model.EndPlot ? Math.Clamp(model.EndPlotTimes, 1, 9999) : 0;
                CopilotSettingsUserControlModel.RegisterRun(
                    appendedId, copilot, model, jobs, markStepDone: endPlotTimes == 0);
                taskIds.Add(appendedId);

                for (var run = 0; run < endPlotTimes; run++)
                {
                    // 一条 = 找一次剧情关 + 播完一次：核心会先看画面里有没有剧情图标，认不到才继续往章节末尾滑
                    var (plotSuccess, plotId) = Instances.AsstProxy.AsstAppendTaskWithEncoding(
                        TaskType.Copilot,
                        new AsstCustomTask { CustomTasks = ["Copilot@EndPlotClick", "Copilot@EndPlotSwipe"] });
                    if (!plotSuccess || plotId <= 0)
                    {
                        return (false, []);
                    }

                    var isLastPlot = run == endPlotTimes - 1;
                    CopilotSettingsUserControlModel.RegisterRun(
                        plotId,
                        copilot,
                        model,
                        [],
                        endPlotRun: true,
                        markStepDone: isLastPlot,
                        doneMessage: isLastPlot
                            ? LocalizationHelper.GetStringFormat("CopilotEndPlotDone", model.Name, endPlotTimes)
                            : null);
                    taskIds.Add(plotId);
                }
            }

            return (true, taskIds);
        }

        static AsstCopilotTask BuildAsstTask(CopilotBattleSnapshot snapshot, List<CopilotSnapshotJob> jobs)
        {
            var userAdditionals = snapshot.UserAdditionals
                .Select(op => new AsstCopilotTask.UserAdditional {
                    Name = op.Name,
                    Skill = op.Skill,
                    Module = op.Module,
                })
                .ToList();

            // 单作业模式：和「作业」页点"开始"走完全相同的构造函数（FileName + 各开关），
            // 效果一致 —— 不做多作业那套"自己找关卡"；以后改自动战斗的单作业流程，这里自动跟着变。
            if (snapshot.SingleJob && jobs.Count == 1)
            {
                return CopilotViewModel.BuildSingleJobTask(
                    jobs[0].FilePath,
                    snapshot.Formation,
                    snapshot.SupportUnitUsage,
                    snapshot.AddTrust,
                    snapshot.IgnoreRequirements,
                    userAdditionals,
                    snapshot.LoopTimes,
                    snapshot.FormationIndex,
                    snapshot.UseStone ? int.MaxValue : 0,
                    snapshot.UseSanityPotion);
            }

            return new AsstCopilotTask() {
                MultiTasks = [.. jobs.Select((job, index) => new AsstCopilotTask.MultiTask {
                    Index = index,
                    FileName = job.FilePath,
                    IsRaid = job.IsRaid,
                    StageName = job.StageName,
                })],
                Formation = snapshot.Formation,
                SupportUnitUsage = snapshot.SupportUnitUsage,
                AddTrust = snapshot.AddTrust,
                IgnoreRequirements = snapshot.IgnoreRequirements,
                UserAdditionals = userAdditionals,
                UseSanityPotion = snapshot.UseSanityPotion,
                Stone = snapshot.UseStone ? int.MaxValue : 0,
                FormationIndex = snapshot.FormationIndex,
            };
        }
    }
}

/// <summary>
/// 战斗任务中的一个小任务的界面包装。
/// </summary>
public class CopilotSubTaskItem : PropertyChangedBase
{
    public CopilotSubTaskItem(CopilotSubTask model)
    {
        Model = model;
    }

    public CopilotSubTask Model { get; }

    private bool _isEditing;

    /// <summary>
    /// Gets or sets a value indicating whether 名字处于编辑状态（仅点重命名按钮后为真）。
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => SetAndNotify(ref _isEditing, value);
    }

    public string Name
    {
        get => Model.Name;
        set {
            Model.Name = value;
            NotifyOfPropertyChange();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether 该小任务是否勾选执行（跑完会自动取消勾选，和作业列表一致）。
    /// </summary>
    public bool IsChecked
    {
        get => Model.IsChecked;
        set {
            if (Model.IsChecked == value)
            {
                return;
            }

            Model.IsChecked = value;
            NotifyOfPropertyChange();
        }
    }

    /// <summary>
    /// Gets a value indicating whether 该小任务是战斗（作业页快照）。
    /// </summary>
    public bool IsBattle => Model.Kind == CopilotSubTaskKind.Battle;

    /// <summary>
    /// Gets a value indicating whether 该小任务是导航（章节入口切换，只有删除按钮）。
    /// </summary>
    public bool IsNav => Model.Kind == CopilotSubTaskKind.Nav;

    /// <summary>
    /// Gets a value indicating whether 该小任务是「剿灭导航」（图标显示对 / 错）。
    /// </summary>
    public bool IsAnnihilationNav => Model.Kind == CopilotSubTaskKind.Nav && Model.NavAnnihilation;

    /// <summary>
    /// Gets a value indicating whether 剿灭导航已经从"下一个作战任务的作业"里读到了剿灭关卡名。
    /// 读到 = 图标 ✓（这一步能执行），读不到 = 图标 ✗（下发时会跳过并写错误日志）。
    /// </summary>
    public bool HasAnnihilationStage => !string.IsNullOrEmpty(Model.NavAnnihilationStage);

    /// <summary>Gets a value indicating whether 显示战斗图标（斜向短剑）。</summary>
    public bool ShowBattleIcon => !IsNav;

    /// <summary>Gets a value indicating whether 显示普通导航图标（向右箭头）。</summary>
    public bool ShowNavIcon => IsNav && !IsAnnihilationNav;

    /// <summary>Gets a value indicating whether 显示剿灭导航"识别到了"的图标（✓）。</summary>
    public bool ShowAnnihilationOkIcon => IsAnnihilationNav && HasAnnihilationStage;

    /// <summary>Gets a value indicating whether 显示剿灭导航"没识别到"的图标（✗）。</summary>
    public bool ShowAnnihilationFailIcon => IsAnnihilationNav && !HasAnnihilationStage;

    /// <summary>
    /// Gets 剿灭导航图标的提示：图钉同样是拖拽把手，所以第一行固定写"标签顺序可拖动"，
    /// 第二行再写读到了就切到哪一关、读不到时去哪儿找关卡名。
    /// </summary>
    public string AnnihilationIconTip => IsAnnihilationNav
        ? LocalizationHelper.GetString("LabelSequenceTip") + Environment.NewLine
          + LocalizationHelper.GetStringFormat(
              HasAnnihilationStage ? "CopilotNavAnnihilationOkTip" : "CopilotNavAnnihilationFailTip",
              Model.NavAnnihilationStage ?? string.Empty)
        : string.Empty;

    /// <summary>
    /// 剿灭关卡名重新解析完之后刷新名字、图标和提示（图标可能从 ✗ 变成 ✓，也可能反过来）。
    /// </summary>
    public void RefreshAnnihilationStage()
    {
        NotifyOfPropertyChange(nameof(Name));
        NotifyOfPropertyChange(nameof(IsAnnihilationNav));
        NotifyOfPropertyChange(nameof(HasAnnihilationStage));
        NotifyOfPropertyChange(nameof(ShowBattleIcon));
        NotifyOfPropertyChange(nameof(ShowNavIcon));
        NotifyOfPropertyChange(nameof(ShowAnnihilationOkIcon));
        NotifyOfPropertyChange(nameof(ShowAnnihilationFailIcon));
        NotifyOfPropertyChange(nameof(AnnihilationIconTip));
    }

    public string KindText => Model.Kind switch {
        CopilotSubTaskKind.Battle => LocalizationHelper.GetString("CopilotSubBattle"),
        CopilotSubTaskKind.Nav => LocalizationHelper.GetString("CopilotSubNav"),
        _ => Model.Kind.ToString(),
    };
}

/// <summary>
/// "导航"小任务的目标选项（章节选择器 ComboBox 的条目）：要么是某一章，要么是某个活动。
/// </summary>
public class NavChapterOption
{
    /// <summary>主线 10~14 章有「标准 / 磨难」两个模式（对应核心的 PreStageNormalHard 档）。</summary>
    public const int DifficultyChapterMin = 10;

    /// <summary>主线 10~14 章有「标准 / 磨难」两个模式（对应核心的 PreStageNormalHard 档）。</summary>
    public const int DifficultyChapterMax = 14;

    /// <summary>
    /// 「剿灭作战」导航（列在第 0 章上面）。要切哪一个剿灭关卡不在这里选：
    /// 由"这一步后面第一个作战任务的作业"决定，见 CopilotAnnihilationNavHelper。
    /// </summary>
    public NavChapterOption()
    {
        IsAnnihilation = true;
        Display = LocalizationHelper.GetString("CopilotNavAnnihilation");
    }

    public NavChapterOption(int chapter)
    {
        Chapter = chapter;
        Display = LocalizationHelper.GetStringFormat("CopilotNavChapterItem", chapter);
    }

    public NavChapterOption(string sideStoryCode, string sideStoryName, string modes)
    {
        SideStory = sideStoryCode;
        Modes = modes;

        // 活动名前面带上活动代号（如 "SL 火山旅梦"）：列表里一眼能看出是哪个活动，也方便按代号搜索。
        // 活动名来自游戏内中文名，不随界面语言变化，所以这里不用本地化格式串。
        Display = $"{sideStoryCode} {sideStoryName}";
    }

    /// <summary>
    /// 资源关导航项：资源关代号（如 "CE-6"）+ 显示文本（"关卡代号 产物"，和活动项一个格式，如"CE 龙门币"）
    /// + 搜索别名（完整关卡代号，只在搜索时用）。只切到资源关页面为止，不选具体关卡。
    /// </summary>
    /// <param name="resourceStage">理智作战里的资源关代号（任务名），如 "CE-6"、"PR-A-1"。</param>
    /// <param name="display">显示文本。</param>
    /// <param name="searchAliases">完整关卡代号（如 "CE-6"、"PR-A-1"/"PR-A-2"），只用于搜索。</param>
    public NavChapterOption(string resourceStage, string display, string[] searchAliases)
    {
        ResourceStage = resourceStage;
        Display = display;
        SearchAliases = searchAliases;
    }

    /// <summary>
    /// Gets 资源关导航要跑的资源关代号（活动项、章节项、剿灭项为 null），如 "CE-6"、"PR-A-1"。
    /// </summary>
    public string? ResourceStage { get; }

    /// <summary>
    /// Gets 搜索别名（资源关项才有：完整关卡代号，如 "CE-6"、"PR-A-1"/"PR-A-2"）。
    /// 只用于搜索，不参与显示 —— 显示的是"关卡代号 产物"。
    /// </summary>
    public IReadOnlyList<string> SearchAliases { get; } = [];

    /// <summary>
    /// Gets a value indicating whether 这一项是资源关导航。
    /// </summary>
    public bool IsResource => !string.IsNullOrEmpty(ResourceStage);

    /// <summary>
    /// Gets 章节号（活动项、剿灭项为 null）。
    /// </summary>
    public int? Chapter { get; }

    /// <summary>
    /// Gets a value indicating whether 这一项是「剿灭作战」导航（放在第 0 章上面）。
    /// </summary>
    public bool IsAnnihilation { get; }

    /// <summary>
    /// Gets a value indicating whether 该章节需要选「标准 / 磨难」（主线 10~14 章）。
    /// </summary>
    public bool HasDifficulty => Chapter is >= DifficultyChapterMin and <= DifficultyChapterMax;

    /// <summary>
    /// Gets 活动代码（章节项为 null）。
    /// </summary>
    public string? SideStory { get; }

    /// <summary>
    /// Gets 该活动有哪些关卡模式（章节项为 null）："EX" 或 "EXS"，来自 ss.xlsx 的 C 列。
    /// </summary>
    public string? Modes { get; }

    /// <summary>
    /// Gets a value indicating whether 该活动能切 EX 模式。
    /// </summary>
    public bool HasEx => Modes?.Contains("EX", StringComparison.Ordinal) == true;

    /// <summary>
    /// Gets a value indicating whether 该活动能切 S 模式（只有一部分活动有）。
    /// </summary>
    public bool HasS => Modes?.Contains('S') == true;

    /// <summary>
    /// Gets a value indicating whether 该目标（活动）能选关卡模式。
    /// </summary>
    public bool HasStageMode => HasEx || HasS;

    /// <summary>
    /// Gets 显示文本（"第 8 章" 或 "SL 火山旅梦"）。
    /// </summary>
    public string Display { get; }

    /// <summary>
    /// 可搜索下拉框（MakeComboBoxSearchable）是按 ToString() 过滤的：
    /// 显示文本 + 搜索别名，这样按"CE 龙门币"和按完整关卡代号"CE-6"都能搜到（显示仍用 Display）。
    /// </summary>
    /// <returns>用于搜索的文本。</returns>
    public override string ToString()
    {
        var text = SearchAliases.Count == 0 ? Display : $"{Display} {string.Join(' ', SearchAliases)}";

        // 再附一份去掉空格的写法：这样带代号搜（"SL"）、按名字搜（"火山旅梦"）、
        // 连在一起写（"SL火山旅梦"）都能命中；显示仍然只用 Display。
        var compact = string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));
        return compact == text ? text : $"{text} {compact}";
    }
}

/// <summary>
/// 高级设置中的作业项（勾选 + 拖动排序）。
/// </summary>
public class CopilotJobItem : PropertyChangedBase
{
    public CopilotJobItem(CopilotSnapshotJob model)
    {
        Model = model;
    }

    public CopilotSnapshotJob Model { get; }

    public bool IsChecked
    {
        get => Model.IsChecked;
        set {
            if (Model.IsChecked == value)
            {
                return;
            }

            Model.IsChecked = value;
            NotifyOfPropertyChange();
        }
    }

    public bool IsRaid => Model.IsRaid;

    public string Title => string.IsNullOrEmpty(Model.StageName)
        ? System.IO.Path.GetFileName(Model.FilePath)
        : Model.StageName!;
}
