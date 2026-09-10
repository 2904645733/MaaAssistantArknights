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
    /// Gets 章节选择器条目（第 0 章 ~ 第 17 章）。
    /// </summary>
    public List<NavChapterOption> NavChapterOptions { get; } =
        [.. Enumerable.Range(0, NavChapterCount).Select(i => new NavChapterOption(i))];

    private bool _isNavPickerOpen;

    /// <summary>
    /// Gets or sets a value indicating whether 章节选择器已展开（点"添加导航小任务"后出现）。
    /// </summary>
    public bool IsNavPickerOpen
    {
        get => _isNavPickerOpen;
        set => SetAndNotify(ref _isNavPickerOpen, value);
    }

    private int _selectedNavChapter;

    /// <summary>
    /// Gets or sets 章节选择器中选中的章节号（0~17）。
    /// </summary>
    public int SelectedNavChapter
    {
        get => _selectedNavChapter;
        set => SetAndNotify(ref _selectedNavChapter, value);
    }

    /// <summary>
    /// 点击"添加导航小任务"：展开 / 收起章节选择器。
    /// </summary>
    public void ToggleNavPicker() => IsNavPickerOpen = !IsNavPickerOpen;

    /// <summary>
    /// 把选择器里选中的章节添加成一个"导航"小任务（追加到列表末尾，可拖拽插到任意位置）。
    /// </summary>
    public void AddSelectedNav()
    {
        var chapter = Math.Clamp(SelectedNavChapter, 0, NavChapterCount - 1);
        var subTask = new CopilotSubTask {
            Kind = CopilotSubTaskKind.Nav,
            Name = LocalizationHelper.GetStringFormat("CopilotSubNavName", chapter),
            NavChapter = chapter,
        };

        Items.Add(new CopilotSubTaskItem(subTask));
        SaveItems();
        IsNavPickerOpen = false;
        StatusMessage = string.Empty;
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
    /// 核心开始执行某个作业时调用：把此前已开始的作业标记为已完成（取消勾选）。
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
        }

        info.LastStarted = jobIndex;
    }

    /// <summary>
    /// 任务链结束时调用：把最后开始的作业标记为已完成。
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
        }

        // 这条小任务（不论战斗还是导航）已经跑完 → 取消勾选，中断后继续执行不会重复跑。
        // 要通过界面包装去改：它既写模型又通知界面（列表一直显示着，只改模型界面不会刷新）。
        var owner = CopilotSettingsUserControlModel.Instance;
        var item = owner.Items.FirstOrDefault(i => ReferenceEquals(i.Model, info.SubTask));
        if (item is not null)
        {
            item.IsChecked = false;
        }
        else
        {
            info.SubTask.IsChecked = false;
        }
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
        SaveItems();
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
                FilePath = job.FilePath,
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
                Items.Add(new CopilotSubTaskItem(sub));
            }

            AdvancedItems.Clear();
            AdvancedOwner = null;
            _isRefreshing = false;
            StatusMessage = string.Empty;
            IsNavPickerOpen = false;
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

                    if (usableJobs.Count > 0)
                    {
                        chain.Add((sub, usableJobs));
                    }

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
                    var chapter = Math.Clamp(model.NavChapter, 0, NavChapterCount - 1);
                    var (navSuccess, navId) = Instances.AsstProxy.AsstAppendTaskWithEncoding(
                        TaskType.Copilot,
                        new AsstChapterNavigationTask { Chapter = chapter });
                    if (!navSuccess || navId <= 0)
                    {
                        return (false, []);
                    }

                    taskIds.Add(navId);
                    CopilotSettingsUserControlModel.RegisterRun(navId, copilot, model, []);
                    continue;
                }

                var (isSuccess, appendedId) = Instances.AsstProxy.AsstAppendTaskWithEncoding(TaskType.Copilot, BuildAsstTask(model.Battle!, jobs));
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
/// "导航"小任务的章节选项（章节选择器 ComboBox 的条目）。
/// </summary>
public class NavChapterOption
{
    public NavChapterOption(int chapter)
    {
        Value = chapter;
        Display = LocalizationHelper.GetStringFormat("CopilotNavChapterItem", chapter);
    }

    /// <summary>
    /// Gets 章节号（0 ~ 17）。
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Gets 显示文本（如"第 8 章"）。
    /// </summary>
    public string Display { get; }
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
