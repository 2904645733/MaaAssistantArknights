// <copyright file="CopilotAnnihilationNavHelper.cs" company="MaaAssistantArknights">
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
using System.IO;
using System.Linq;
using MaaWpfGui.Configuration.Single.MaaTask;
using MaaWpfGui.Models.Copilot;
using Newtonsoft.Json;

namespace MaaWpfGui.Helper;

/// <summary>
/// 战斗任务的「剿灭导航」用到的查找逻辑。
/// 要切哪一个剿灭关卡不在界面上单独选，而是从"这一步后面第一个作战小任务的作业"里读出来：
/// 作业项里填的覆盖名优先，其次是作业文件里的 <c>stage_name</c>。
/// 读到了说明这一步能执行（界面显示 ✓），读不到（后面没有作战小任务 / 作业里没有关卡名）核心就没法
/// 在剿灭关卡列表里认出目标（界面显示 ✗）。
/// </summary>
public static class CopilotAnnihilationNavHelper
{
    /// <summary>
    /// 已知剿灭关卡的各种写法 → 游戏里显示的中文名（关卡列表里 OCR 要认的就是中文名）。
    /// 作业里的 stage_name 可能是中文名、英文别名，也可能是 camp_01 这种关卡 id。
    /// 表里没有的写法原样返回，交给 OCR 去认（以后加新剿灭关卡也不用改这里）。
    /// </summary>
    private static readonly (string Alias, string Name)[] Aliases = [
        ("Chernobog", "切尔诺伯格"), ("camp_01", "切尔诺伯格"), ("level_camp_01", "切尔诺伯格"),
        ("LungmenOutskirts", "龙门外环"), ("camp_02", "龙门外环"), ("level_camp_02", "龙门外环"),
        ("LungmenDowntown", "龙门市区"), ("camp_03", "龙门市区"), ("level_camp_03", "龙门市区"),
    ];

    /// <summary>
    /// 从"导航小任务后面第一个作战小任务"里读出剿灭关卡名。
    /// </summary>
    /// <param name="following">这一步导航之后的所有小任务（按列表顺序）。</param>
    /// <returns>剿灭关卡名（如 "龙门市区"）；读不到返回 null。</returns>
    public static string? ResolveAfter(IEnumerable<CopilotSubTask> following)
    {
        foreach (var sub in following)
        {
            if (sub.Kind != CopilotSubTaskKind.Battle || sub.Battle is not { } battle)
            {
                continue;
            }

            // 优先用勾选着（会真的执行）的作业，一个都没勾选就退回第一个
            var job = battle.Jobs.FirstOrDefault(j => j.IsChecked) ?? battle.Jobs.FirstOrDefault();
            return job is null ? null : FromJob(job);
        }

        return null;
    }

    /// <summary>
    /// 从单个作业里读出剿灭关卡名：作业项里填的覆盖名优先，其次读作业文件里的 stage_name。
    /// </summary>
    /// <param name="job">作业项。</param>
    /// <returns>剿灭关卡名；读不到返回 null。</returns>
    public static string? FromJob(CopilotSnapshotJob job)
    {
        var raw = string.IsNullOrWhiteSpace(job.StageName) ? ReadStageNameFromFile(job.FilePath) : job.StageName;
        return Normalize(raw);
    }

    /// <summary>
    /// 把作业里写的关卡名统一成游戏里显示的中文名。
    /// 作业里的 stage_name 常见写法有四种：中文显示名（如"燃烧街区"）、关卡 id（如 camp_r_23）、
    /// 完整 levelId（如 obt/campaign/level_camp_r_23）、老三张剿灭的英文代号（如 LungmenDowntown）。
    /// 剿灭关卡列表里 OCR 要认的只有中文显示名，所以这里统一查游戏数据
    /// （resource/Arknights-Tile-Pos/overview.json）换成显示名；
    /// 查不到就原样返回交给 OCR（游戏数据还没更新时不至于直接卡死）。
    /// </summary>
    /// <param name="stageName">作业里写的关卡名。</param>
    /// <returns>剿灭关卡名（游戏里的中文显示名）；空则返回 null。</returns>
    public static string? Normalize(string? stageName)
    {
        if (string.IsNullOrWhiteSpace(stageName))
        {
            return null;
        }

        var name = stageName.Trim();
        if (LookupDisplayName(name) is { } display)
        {
            return display;
        }

        // 作业里也可能写完整路径（obt/campaign/level_camp_r_23），取最后一段再查一次
        var last = name.Split('\\', '/').Last().Trim();
        if (!string.Equals(last, name, StringComparison.Ordinal) && LookupDisplayName(last) is { } lastDisplay)
        {
            return lastDisplay;
        }

        // 老的三张剿灭在游戏数据里叫 camp_01 ~ camp_03，作业里可能写英文代号
        foreach (var (alias, chinese) in Aliases)
        {
            if (string.Equals(name, alias, StringComparison.OrdinalIgnoreCase)
                || string.Equals(last, alias, StringComparison.OrdinalIgnoreCase))
            {
                return chinese;
            }
        }

        return name;
    }

    /// <summary>
    /// 查游戏数据里的关卡显示名：先按 stageId / levelId（唯一 ID）找，再按显示名找。
    /// 故意不按 Code 找 —— Code 是地区名（比如"维多利亚"），会命中同一地区的别的关卡。
    /// </summary>
    /// <param name="id">作业里写的关卡名 / 关卡 id。</param>
    /// <returns>中文显示名；查不到返回 null。</returns>
    private static string? LookupDisplayName(string id)
    {
        foreach (var map in DataHelper.MapData)
        {
            if (string.Equals(map.StageId, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(map.LevelId, id, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(map.Name) ? null : map.Name;
            }
        }

        foreach (var map in DataHelper.MapData)
        {
            if (string.Equals(map.Name, id, StringComparison.OrdinalIgnoreCase))
            {
                return map.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// 读作业文件里的 stage_name（作业是作业站下载的 JSON，核心也是这么读的）。
    /// 路径可能是相对的（config\copilot\xxx.json），按 MAA 用户目录解析。
    /// </summary>
    /// <param name="filePath">作业文件路径。</param>
    /// <returns>作业里的关卡名；读不到返回 null。</returns>
    private static string? ReadStageNameFromFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var path = File.Exists(filePath) ? filePath : Path.Combine(PathsHelper.BaseDir, filePath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<CopilotModel>(json)?.StageName;
        }
        catch (Exception)
        {
            // 作业文件坏了/不是作业 JSON：当作读不到，界面上显示 ✗，不要去打断用户操作
            return null;
        }
    }
}
