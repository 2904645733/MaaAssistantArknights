// <copyright file="NavChapterOption.cs" company="MaaAssistantArknights">
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
using System.Linq;
using MaaWpfGui.Helper;

namespace MaaWpfGui.ViewModels.UserControl.TaskQueue;

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
    /// Initializes a new instance of the <see cref="NavChapterOption"/> class.
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
    /// Initializes a new instance of the <see cref="NavChapterOption"/> class.
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
