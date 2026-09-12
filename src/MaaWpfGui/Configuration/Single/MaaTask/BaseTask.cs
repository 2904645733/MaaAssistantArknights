// <copyright file="BaseTask.cs" company="MaaAssistantArknights">
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
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MaaWpfGui.Helper;
using MaaWpfGui.Models;
using static MaaWpfGui.Main.AsstProxy;

namespace MaaWpfGui.Configuration.Single.MaaTask;

// [JsonDerivedType(typeof(VideoRecognition), typeDiscriminator: nameof(VideoRecognition))]
[JsonDerivedType(typeof(StartUpTask), typeDiscriminator: nameof(StartUpTask))]
[JsonDerivedType(typeof(CloseDownTask), typeDiscriminator: nameof(CloseDownTask))]
[JsonDerivedType(typeof(FightTask), typeDiscriminator: nameof(FightTask))]
[JsonDerivedType(typeof(AwardTask), typeDiscriminator: nameof(AwardTask))]
[JsonDerivedType(typeof(MallTask), typeDiscriminator: nameof(MallTask))]
[JsonDerivedType(typeof(InfrastTask), typeDiscriminator: nameof(InfrastTask))]
[JsonDerivedType(typeof(RecruitTask), typeDiscriminator: nameof(RecruitTask))]
[JsonDerivedType(typeof(RoguelikeTask), typeDiscriminator: nameof(RoguelikeTask))]
[JsonDerivedType(typeof(CopilotTask), typeDiscriminator: nameof(CopilotTask))]
[JsonDerivedType(typeof(SSSCopilotTask), typeDiscriminator: nameof(SSSCopilotTask))]
[JsonDerivedType(typeof(SingleStepTask), typeDiscriminator: nameof(SingleStepTask))]
[JsonDerivedType(typeof(DepotTask), typeDiscriminator: nameof(DepotTask))]
[JsonDerivedType(typeof(OperBoxTask), typeDiscriminator: nameof(OperBoxTask))]
[JsonDerivedType(typeof(UserDataUpdateTask), typeDiscriminator: nameof(UserDataUpdateTask))]
[JsonDerivedType(typeof(ReclamationTask), typeDiscriminator: nameof(ReclamationTask))]
[JsonDerivedType(typeof(DepotMaintainTask), typeDiscriminator: nameof(DepotMaintainTask))]
[JsonDerivedType(typeof(CustomTask), typeDiscriminator: nameof(CustomTask))]
public class BaseTask : NotifyPropertyChangedWithValue
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public string NameOrTaskType => string.IsNullOrEmpty(Name) ? LocalizationHelper.GetString(TaskType.ToString()) : Name;

    public bool? IsEnable { get; set; } = true;

    /// <summary>
    /// Gets 任务类型，用于添加任务时使用
    /// </summary>
    public TaskType TaskType { get; init; }
}

#pragma warning disable SA1402 // File may only contain a single type
public class CloseDownTask : BaseTask
{
}

public class CopilotTask : BaseTask
{
    public CopilotTask() => TaskType = TaskType.Copilot;

    /// <summary>
    /// Gets or sets 战斗任务的小任务列表（战斗快照 / 导航），按顺序执行。
    /// </summary>
    public List<CopilotSubTask> SubTasks { get; set; } = [];
}

/// <summary>
/// 战斗任务中的一个小任务。
/// </summary>
public class CopilotSubTask
{
    /// <summary>
    /// Gets or sets 小任务类型：战斗（作业快照）或导航（页面入口）。
    /// </summary>
    public CopilotSubTaskKind Kind { get; set; } = CopilotSubTaskKind.Battle;

    /// <summary>
    /// Gets or sets 小任务名称（用户自定义）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether 是否勾选执行。
    /// 执行完会自动置为 false（与作业列表一致），中断后继续执行不会重复跑。
    /// </summary>
    public bool IsChecked { get; set; } = true;

    /// <summary>
    /// Gets or sets 战斗快照（Kind=Battle 时使用）。
    /// </summary>
    public CopilotBattleSnapshot? Battle { get; set; }

    /// <summary>
    /// Gets or sets 导航目标章节号（Kind=Nav 时使用；0~17，对应核心的 Episode0~Episode17 章节导航任务）。
    /// </summary>
    public int NavChapter { get; set; }

    /// <summary>
    /// Gets or sets 导航目标活动代码（Kind=Nav 时使用；如 AS、AT，非空时优先于 NavChapter）。
    /// </summary>
    public string? NavSideStory { get; set; }

    /// <summary>
    /// Gets or sets 导航难度（Kind=Nav 且目标是主线 10~14 章时使用；"Hard" = 磨难、"Normal" = 标准）。
    /// 非空时核心会在"前往章节"之后再切难度（复用理智作战那套 ChapterDifficulty* 任务）。
    /// </summary>
    public string? Difficulty { get; set; }

    /// <summary>
    /// Gets or sets 活动内的关卡模式（Kind=Nav 且目标是活动时使用："EX" 或 "S"；留空 = 普通关，不切模式）。
    /// 非空时核心会在「进入活动」之后全屏扫对应模式按钮的模板图并点击。
    /// </summary>
    public string? Mode { get; set; }
}

/// <summary>
/// 小任务类型。
/// </summary>
public enum CopilotSubTaskKind
{
    Battle,
    Nav,
}

/// <summary>
/// 战斗小任务快照：记录作业页多作业列表（勾选项）与相关设置。
/// </summary>
public class CopilotBattleSnapshot
{
    /// <summary>自动编队</summary>
    public bool Formation { get; set; }

    /// <summary>借助战（0=不借）</summary>
    public int SupportUnitUsage { get; set; }

    /// <summary>追加信赖干员</summary>
    public bool AddTrust { get; set; }

    /// <summary>忽略干员要求</summary>
    public bool IgnoreRequirements { get; set; }

    /// <summary>使用理智药</summary>
    public bool UseSanityPotion { get; set; }

    /// <summary>信用作战编队索引</summary>
    public int FormationIndex { get; set; }

    /// <summary>快照中的作业（勾选的列表项）</summary>
    public List<CopilotSnapshotJob> Jobs { get; set; } = [];
}

/// <summary>
/// 快照中的一个作业。
/// </summary>
public class CopilotSnapshotJob
{
    /// <summary>作业 JSON 文件路径</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>是否突袭关</summary>
    public bool IsRaid { get; set; }

    /// <summary>导航识别名覆盖（可为空，核心自动读取作业内关卡名）</summary>
    public string? StageName { get; set; }

    /// <summary>是否勾选执行；打完的关卡会自动置为 false，用于断点续跑</summary>
    public bool IsChecked { get; set; } = true;
}

public class SSSCopilotTask : BaseTask
{
}

public class SingleStepTask : BaseTask
{
}

public class DepotTask : BaseTask
{
}

public class OperBoxTask : BaseTask
{
}

#pragma warning restore SA1402 // File may only contain a single type
