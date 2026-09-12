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
    /// Gets 导航目标列表：先是第 0~17 章，接着是活动入口（和游戏里"第 17 章往下就是活动"一致）。
    /// </summary>
    public List<NavChapterOption> NavChapterOptions { get; } = [
        .. Enumerable.Range(0, NavChapterCount).Select(i => new NavChapterOption(i)),
        .. NavSideStories.Select(s => new NavChapterOption(s.Code, s.Name, s.Modes)),
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
    /// Gets or sets 选择器输入框里的搜索词。下拉框做成了可搜索的（和"自动肉鸽 · 开局干员"用的是同一个
    /// MakeComboBoxSearchable），打字即过滤；输入关键字后不点下拉项、直接点"添加"时也按这个文本找目标。
    /// </summary>
    public string NavSearchText
    {
        get => _navSearchText;
        set => SetAndNotify(ref _navSearchText, value);
    }

    private const string NavDifficultyNormal = "Normal";
    private const string NavDifficultyHard = "Hard";
    private const string NavModeEX = "EX";
    private const string NavModeS = "S";

    private string _navDifficulty = NavDifficultyNormal;

    /// <summary>
    /// Gets or sets 选中的难度（只对主线 10~14 章有效）："Normal" = 标准、"Hard" = 磨难。
    /// </summary>
    public string NavDifficulty
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
    /// Gets or sets a value indicating whether 选的是「标准」（默认）。
    /// </summary>
    public bool NavDifficultyIsNormal
    {
        get => !NavDifficultyIsHard;
        set
        {
            if (value)
            {
                NavDifficulty = NavDifficultyNormal;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether 选的是「磨难」。
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
    /// 10~14 章记录了难度时加上（标准 / 磨难）；其余就是选项本身的显示文本。
    /// </summary>
    /// <param name="option">选中的目标。</param>
    /// <param name="difficulty">这一步记录的难度（老配置里可能为空）。</param>
    /// <param name="mode">这一步记录的活动模式（老配置里可能为空）。</param>
    /// <returns>用于显示的名字。</returns>
    private static string NavStepName(NavChapterOption option, string? difficulty, string? mode)
    {
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
    /// 把选择器里选中的目标（某一章 / 某个活动）添加成一个"导航"小任务。
    /// </summary>
    public void AddSelectedNav()
    {
        // 允许"打字搜索 → 直接点添加"：没在下拉框里点选时，用输入的文本找一个明确的目标
        var option = SelectedNavOption ?? ResolveNavOption(NavSearchText);
        if (option is null)
        {
            StatusMessage = LocalizationHelper.GetString("CopilotNavNeedPick");
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
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// 按输入文本找导航目标：完全一致优先，其次"只有一个候选"时也算（章节名、活动代号、活动名都能搜）。
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
            .Where(option => option.Display.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        return matches.FirstOrDefault(option => string.Equals(option.Display, keyword, StringComparison.CurrentCultureIgnoreCase))
               ?? (matches.Count == 1 ? matches[0] : null);
    }

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
    }

    private static readonly Dictionary<int, RunInfo> _runMap = [];

    private static void RegisterRun(int taskId, CopilotTask task, CopilotSubTask subTask, List<CopilotSnapshotJob> sentJobs)
    {
        _runMap[taskId] = new RunInfo { Task = task, SubTask = subTask, SentJobs = sentJobs, LastStarted = -1 };
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
    /// </summary>
    public static void HandleTaskFinished(int taskId)
    {
        if (!_runMap.Remove(taskId, out var info))
        {
            return;
        }

        if (info.LastStarted >= 0 && info.LastStarted < info.SentJobs.Count)
        {
            info.SentJobs[info.LastStarted].IsChecked = false;
            Instances.TaskQueueViewModel.AddLog(
                LocalizationHelper.GetStringFormat("CopilotJobDone", JobTitle(info.SentJobs[info.LastStarted])),
                MaaWpfGui.Constants.UiLogColor.Success);
        }
        else if (info.SubTask.Kind == CopilotSubTaskKind.Battle)
        {
            // 轮到这一步时才发现没有可执行的作业：这时候才算它完成
            MarkStepDone(info.SubTask, LocalizationHelper.GetStringFormat("CopilotSubNoJob", info.SubTask.Name));
            return;
        }

        // 这条小任务（不论战斗还是导航）已经跑完 → 取消勾选，中断后继续执行不会重复跑
        MarkStepDone(
            info.SubTask,
            info.SubTask.Kind == CopilotSubTaskKind.Nav
                ? NavDoneText(info.SubTask)
                : LocalizationHelper.GetStringFormat("CopilotSubDone", info.SubTask.Name));
    }

    /// <summary>
    /// 导航小任务完成的日志文本：章节写"已在第 N 章"（10~14 章带模式时写"已在第 N 章（标准/磨难）"），
    /// 活动写"已在&lt;活动中文名&gt;"（不带活动代号）。
    /// </summary>
    /// <param name="sub">导航小任务。</param>
    /// <returns>日志文本。</returns>
    private static string NavDoneText(CopilotSubTask sub)
    {
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
            StatusMessage = reasonKey;
            return;
        }

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
        page.UseCopilotList = true;
        LoadSnapshotToPage(snapshot);
        StatusMessage = LocalizationHelper.GetString("CopilotEditLoaded");
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

        if (!TryCaptureFromPage(out var snapshot, out var reasonKey))
        {
            StatusMessage = reasonKey;
            return;
        }

        // 保存 = 以作业页当前内容为准：换上新副本的同时，把这一步不再引用的旧副本删掉
        var oldPaths = item.Model.Battle is { } oldBattle ? oldBattle.Jobs.Select(j => j.FilePath).ToList() : [];
        item.Model.Battle = snapshot;
        DeleteSnapshotCopies(oldPaths, item);
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
    /// 生成不与现有小任务重名的默认名（战斗-N）。
    /// </summary>
    private string NextBattleName()
    {
        var prefix = LocalizationHelper.GetString("CopilotSubBattle") + "-";
        var index = Items.Count + 1;
        string candidate;
        do
        {
            candidate = prefix + index++;
        }
        while (Items.Any(i => string.Equals(i.Name, candidate, StringComparison.Ordinal)));

        return candidate;
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
            DeleteSnapshotCopies(battle.Jobs.Select(j => j.FilePath), null);
        }

        SaveItems();
    }

    /// <summary>
    /// 删除作业快照副本。只删 config\copilot_snapshot\ 目录内、且"本任务里没有其它小任务引用"的文件，
    /// 绝不碰作业页下载的 config\copilot\。
    /// </summary>
    /// <param name="paths">候选路径（一般是这个小任务保存前的旧作业）。</param>
    /// <param name="owner">被替换/删除的那个小任务自己（它不算引用者）。</param>
    private static void DeleteSnapshotCopies(IEnumerable<string> paths, CopilotSubTaskItem? owner)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in Instance.Items)
        {
            if (ReferenceEquals(step, owner) || step.Model.Battle is not { } other)
            {
                continue;
            }

            foreach (var job in other.Jobs)
            {
                referenced.Add(job.FilePath);
            }
        }

        var snapshotDir = Path.Combine(PathsHelper.BaseDir, "config", "copilot_snapshot");
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(path) || referenced.Contains(path))
            {
                continue;
            }

            var absolute = Path.Combine(PathsHelper.BaseDir, path);
            if (!absolute.StartsWith(snapshotDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(absolute))
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
        if (page.CopilotTabIndex != 0 || !page.UseCopilotList)
        {
            reasonKey = LocalizationHelper.GetString("CopilotNeedMultiMode");
            return false;
        }

        var selectedJobs = page.CopilotItemViewModels.Where(i => i.IsChecked).ToList();
        if (selectedJobs.Count == 0)
        {
            reasonKey = LocalizationHelper.GetString("CopilotNeedMultiMode");
            return false;
        }

        // 快照只记作业文件路径，文件必须真的存在，否则核心加载作业会失败（界面只会显示"添加任务失败"）
        var missingJob = selectedJobs.FirstOrDefault(job => !JobFileExists(job.FilePath));
        if (missingJob is not null)
        {
            reasonKey = LocalizationHelper.GetStringFormat("CopilotJobFileMissing", missingJob.Name, missingJob.FilePath);
            return false;
        }

        snapshot = new CopilotBattleSnapshot {
            Formation = page.Form,
            SupportUnitUsage = page.UseSupportUnitUsage ? (int)page.SupportUnitUsage : 0,
            AddTrust = page.AddTrust,
            IgnoreRequirements = page.IgnoreRequirements,
            UseSanityPotion = page.UseSanityPotion,
            FormationIndex = page.UseFormation ? page.FormationIndex : 0,
            Jobs = [.. selectedJobs.Select(job => new CopilotSnapshotJob {
                FilePath = CopyJobToSnapshot(job.FilePath),
                IsRaid = job.IsRaid,
                StageName = job.IsNavNameOverride ? job.Name : null,
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
    /// 快照专用目录：把用到的作业文件复制一份进来。
    /// 这样在「作业」页点"清除任务"（它会删掉 config\copilot 下下载的作业）也不会把已记录的小任务弄坏，
    /// 同时不动作业页原有的行为。
    /// </summary>
    private static string CopyJobToSnapshot(string filePath)
    {
        var source = File.Exists(filePath) ? filePath : Path.Combine(PathsHelper.BaseDir, filePath);
        var relativeDir = Path.Combine("config", "copilot_snapshot");
        var absoluteDir = Path.Combine(PathsHelper.BaseDir, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var name = Path.GetFileName(source);
        var target = Path.Combine(absoluteDir, name);
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
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

    private static void LoadSnapshotToPage(CopilotBattleSnapshot snapshot)
    {
        var page = Instances.CopilotViewModel;

        page.Form = snapshot.Formation;
        page.UseSupportUnitUsage = snapshot.SupportUnitUsage != 0;
        page.SupportUnitUsage = (CopilotViewModel.CopilotSupportMode)snapshot.SupportUnitUsage;
        page.AddTrust = snapshot.AddTrust;
        page.IgnoreRequirements = snapshot.IgnoreRequirements;
        page.UseSanityPotion = snapshot.UseSanityPotion;
        page.UseFormation = snapshot.FormationIndex != 0;
        page.FormationIndex = snapshot.FormationIndex;

        page.CopilotItemViewModels.Clear();
        var index = 0;
        foreach (var job in snapshot.Jobs)
        {
            var useOverride = !string.IsNullOrEmpty(job.StageName);
            var name = useOverride ? job.StageName! : Path.GetFileNameWithoutExtension(job.FilePath);
            page.CopilotItemViewModels.Add(new CopilotItemViewModel(name, job.FilePath, job.IsRaid, 0, true, useOverride) { Index = index++ });
        }

        page.SaveCopilotTask();
    }

    private void SaveItems()
    {
        SetTaskConfig<CopilotTask>(_ => false, t => t.SubTasks = [.. Items.Select(i => i.Model)]);
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
                    var option = string.IsNullOrEmpty(sub.NavSideStory)
                        ? NavChapterOptions.FirstOrDefault(o => o.Chapter == sub.NavChapter)
                        : NavChapterOptions.FirstOrDefault(o => o.SideStory == sub.NavSideStory);
                    if (option is not null)
                    {
                        sub.Name = NavStepName(option, sub.Difficulty, sub.Mode);
                    }
                }

                Items.Add(new CopilotSubTaskItem(sub));
            }

            AdvancedItems.Clear();
            AdvancedOwner = null;
            _isRefreshing = false;
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
                if (chain.Count != 1 || chain[0].Model.Kind != CopilotSubTaskKind.Battle)
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

                CopilotSettingsUserControlModel.RegisterRun(appendedId, copilot, model, jobs);
                taskIds.Add(appendedId);
            }

            return (true, taskIds);
        }

        static AsstCopilotTask BuildAsstTask(CopilotBattleSnapshot snapshot, List<CopilotSnapshotJob> jobs)
        {
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
                UseSanityPotion = snapshot.UseSanityPotion,
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
    /// Gets 章节号（活动项为 null）。
    /// </summary>
    public int? Chapter { get; }

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
    /// 可搜索下拉框（MakeComboBoxSearchable）是按 ToString() 过滤的，所以要和 Display 保持一致。
    /// </summary>
    /// <returns>显示文本。</returns>
    public override string ToString() => Display;
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
