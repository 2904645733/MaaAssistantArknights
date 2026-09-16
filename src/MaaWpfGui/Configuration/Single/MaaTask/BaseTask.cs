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
[JsonDerivedType(typeof(SwitchThemeTask), typeDiscriminator: nameof(SwitchThemeTask))]
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

    /// <summary>
    /// Gets or sets a value indicating whether 这一步是「剿灭导航」（Kind=Nav 时使用）。
    /// 剿灭导航先点选关界面的「剿灭」标签、再点「进入」，之后与理智作战切剿灭关卡一致
    /// （左上角返回 → 右下角切换 → 在关卡列表里认出关卡名并点击）。
    /// 要切哪个剿灭关卡由"这一步后面第一个作战任务的作业"决定，见 <see cref="NavAnnihilationStage"/>。
    /// </summary>
    public bool NavAnnihilation { get; set; }

    /// <summary>
    /// Gets or sets 剿灭导航要切到的剿灭关卡名（如 "龙门市区"）。
    /// 由界面从"这一步后面第一个作战任务的作业"里读出来（作业项里的覆盖名优先，否则读作业文件的 stage_name）；
    /// 读到了界面显示 ✓（这一步能执行），读不到显示 ✗（下发时跳过并写错误日志）。
    /// </summary>
    public string? NavAnnihilationStage { get; set; }

    /// <summary>
    /// Gets or sets 资源关导航的目标代号（Kind=Nav 时使用，如 "CE-6"、"PR-A-1"；留空表示不是资源关导航）。
    /// 就是理智作战里那个资源关任务名：核心直接跑它（先点「资源」标签进资源关页面，
    /// 再"认该产物的入口卡片，认不到就左滑 / 右滑"），但把后面"选具体关卡"那一步挡掉 ——
    /// 只到资源关页面为止，不选具体关卡。
    /// </summary>
    public string? NavResourceStage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 勾了「章尾剧情」（Kind=Battle 时使用）：
    /// 这一小任务里的作业全部打完后，往章节末尾（编号大的那头）滑地图找剧情关，认出剧情图标就点进去把剧情播完。
    /// </summary>
    public bool EndPlot { get; set; }

    /// <summary>
    /// Gets or sets 章尾剧情要做的次数（默认 1）。
    /// 勾了「章尾剧情」时，这个次数做完这一步才算完成（战斗部分跑完先不取消勾选）。
    /// </summary>
    public int EndPlotTimes { get; set; } = 1;
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
/// 战斗小任务快照：记录作业页的状态（单作业 / 多作业列表、勾选项）与相关设置。
/// </summary>
public class CopilotBattleSnapshot
{
    /// <summary>
    /// Gets or sets a value indicating whether 这个快照是照「作业」页的"单作业模式"（没勾"多作业模式"）记的。
    /// 是的话下发时走「作业」页"开始"那套单作业参数（FileName + 各开关），效果和自动战斗单作业完全一致
    /// （不会启用多作业那套"自己找关卡"的流程）；否则按多作业列表下发。
    /// </summary>
    public bool SingleJob { get; set; }

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

    /// <summary>使用源石（理智不足时碎源石，和理智作战一样）</summary>
    public bool UseStone { get; set; }

    /// <summary>信用作战编队索引</summary>
    public int FormationIndex { get; set; }

    /// <summary>循环次数（自动战斗勾了"循环"时的次数；没勾是 1）</summary>
    public int LoopTimes { get; set; } = 1;

    /// <summary>自定干员（作业页"自定干员"列表，和自动战斗一样随快照记录）</summary>
    public List<CopilotSnapshotUserAdditional> UserAdditionals { get; set; } = [];

    /// <summary>快照中的作业（单作业模式时只有 1 个；多作业模式时是勾选的列表项）</summary>
    public List<CopilotSnapshotJob> Jobs { get; set; } = [];
}

/// <summary>
/// 快照里的自定干员（核心只用名字 + 技能序号，模组是预留字段）。
/// </summary>
public class CopilotSnapshotUserAdditional
{
    /// <summary>干员名（OCR 文本，跟随客户端语言）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>技能序号（0~3）</summary>
    public int Skill { get; set; }

    /// <summary>模组编号（核心暂未使用，保留原值以便"编辑"时回填）</summary>
    public int Module { get; set; }
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
