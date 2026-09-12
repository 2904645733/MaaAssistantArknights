// <copyright file="AsstChapterNavigationTask.cs" company="MaaAssistantArknights">
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
using MaaWpfGui.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaaWpfGui.Models.AsstTasks;

/// <summary>
/// 导航到指定章节的任务（战斗任务的"导航"小任务用）。
/// 核心侧复用理智作战进入选关界面的 StageBegin 与章节导航 Episode{N}，只切页面、不选具体关卡、不战斗。
/// </summary>
public class AsstChapterNavigationTask : AsstBaseTask
{
    public override AsstTaskType TaskType => AsstTaskType.ChapterNavigation;

    /// <summary>Gets or sets 目标章节号（0~17，对应核心的 Episode0~Episode17）。</summary>
    [JsonProperty("chapter", NullValueHandling = NullValueHandling.Ignore)]
    public int? Chapter { get; set; }

    /// <summary>Gets or sets 目标活动代码（如 AS、AT，对应 SideStory/{代码}/{代码}@EnterSideStoryNew.png）。</summary>
    [JsonProperty("side_story", NullValueHandling = NullValueHandling.Ignore)]
    public string? SideStory { get; set; }

    /// <summary>Gets or sets 难度（仅主线 10~14 章有效：Hard = 磨难、Normal = 标准；留空表示不切换难度）。</summary>
    [JsonProperty("difficulty", NullValueHandling = NullValueHandling.Ignore)]
    public string? Difficulty { get; set; }

    /// <summary>Gets or sets 活动内的关卡模式（仅活动有效："EX" 或 "S"；留空表示不切模式，进活动后默认就是普通关）。</summary>
    [JsonProperty("mode", NullValueHandling = NullValueHandling.Ignore)]
    public string? Mode { get; set; }

    public override (AsstTaskType TaskType, JObject Params) Serialize() => (TaskType, JObject.FromObject(this));
}
